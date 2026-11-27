// Match data engine. While a game is open: a sample every 2 s (FPS, ping, CPU, GPU, RAM and the process that stood out),
// one report per match (or per game session when the game does not tell where the player is), saved as JSON in
// %LOCALAPPDATA%\Tweakeroo\partidas, the last 50 kept. Outside a game nothing watches: the app's own 2 s game check
// (GameUi.StartGames) calls Tracker.Seen. Nothing of the game is touched: ETW, Windows counters and the game's own logs, read only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Tablero
{
    // One sample, taken every 2 s with a game open. A value that could not be read is null.
    public class Sample
    {
        public DateTime Time { get; set; }       // UTC
        public double? Fps { get; set; }         // average of the last ~10 s (DXGI presents through ETW: needs administrator)
        public double? Low1 { get; set; }        // 1 % low of the last ~10 s: 1000 / the frame time that 1 % of the frames reach
        public double? WorstMs { get; set; }     // slowest frame since the previous sample (pauses over 2 s left out)
        public int? Ping { get; set; }           // ms; -1 = lost; null = not measured (what it measures: Probe.Host)
        public double? Loss { get; set; }        // % lost of the last 30 pings (1 min)
        public double? Cpu { get; set; }         // % of the whole CPU
        public double? CpuTemp { get; set; }     // °C of the ACPI thermal zone; see Tracker.CpuTempAvailable
        public double? Gpu { get; set; }         // % (NVIDIA: NVAPI; others: Windows "GPU Engine" counters, 3D engine)
        public double? GpuTemp { get; set; }     // °C, NVIDIA only
        public double? VramMb { get; set; }      // dedicated video memory in use
        public double? RamMb { get; set; }       // in use
        public double? RamTotalMb { get; set; }
        public string CpuHog { get; set; }       // process (not the game) with the most CPU since the previous sample, without ".exe"
        public double? CpuHogPct { get; set; }   // its % of the whole CPU
        public string DiskHog { get; set; }      // process (not the game) with the most I/O since the previous sample
        public double? DiskHogMbs { get; set; }  // its MB/s: the process I/O counters, mostly disk but also some network and device I/O

        // Likely culprit of this moment: the CPU or disk hog that stands out most, from 5 % of the CPU or 5 MB/s; null = none.
        [ScriptIgnore]
        public string Culprit { get { double c = (CpuHogPct ?? 0) / 5, d = (DiskHogMbs ?? 0) / 5; return Math.Max(c, d) < 1 ? null : c >= d ? CpuHog : DiskHog; } }
    }

    // A stutter or a ping spike of a match. At = minute of the match, "8:32".
    public class Moment
    {
        public DateTime Time { get; set; }  // UTC
        public string At { get; set; }
        public int Ms { get; set; }         // the frame time (stutter) or the ping (spike)
        public string Culprit { get; set; } // Sample.Culprit of that moment; null = none
    }

    // A lag marked with the shortcut (Prefs.MarkKey), with the sample nearest to the key press.
    public class LagMark
    {
        public DateTime Time { get; set; }  // UTC
        public string At { get; set; }      // minute of the match, "8:32"
        public Sample Sample { get; set; }  // null if the match had no samples
    }

    // One match, as saved in partidas\ (one JSON file each).
    public class MatchReport
    {
        public string Game { get; set; }     // Game.Id; Game.Of(r.Game) for the rest
        public string Map { get; set; }      // map or mode; null = not known (the whole game session is one report)
        public DateTime Start { get; set; }  // UTC
        public DateTime End { get; set; }    // UTC
        public double? Fps { get; set; }     // average over the whole match
        public double? Low1 { get; set; }    // 1 % low over the whole match
        public List<Moment> Stutters { get; set; } // a frame over 3 times the usual one and at least 25 ms; at most one per sample (2 s)
        public int? Ping { get; set; }       // average of the pings that came back, ms
        public double? Loss { get; set; }    // % of pings lost
        public List<Moment> Spikes { get; set; }   // pings 30 ms above the median of the match
        public string Culprit { get; set; }  // culprit repeated most in the stutters and spikes; null = none
        public int CulpritTimes { get; set; }
        public List<LagMark> Marks { get; set; }
    }

    // The engine. Seen, Stop and Mark from any thread; Start on the UI thread.
    // Sampled and Finished fire on the background thread "Partida": a UI handler must use Dispatcher.BeginInvoke.
    public static class Tracker
    {
        public const int Every = 2000;     // ms between samples
        internal const int Keep = 50;      // reports kept
        internal const double StutterMs = 25;
        public static event Action<Sample> Sampled;        // each sample, with the game open (menu included)
        public static event Action<MatchReport> Finished;  // each match once saved
        public static Sample Last { get; private set; }    // the latest sample; null without a game open
        public static double CostMs { get; private set; }  // CPU of the sampling thread per sample, ms (average; ETW's own thread not included)

        static string dir;
        static readonly object gate = new object();
        static Thread thread;
        static ManualResetEvent quit;
        static Game playing;
        static Play play;

        static System.Windows.Threading.Dispatcher ui;

        // At start-up, on the UI thread: where the reports go. Reads nothing: it does not delay opening.
        public static void Start(string appDir)
        {
            dir = appDir;
            ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            EtwSession.Stop(Probe.EtwName); // a crash leaves the real-time session running
        }

        // The lag mark shortcut (Prefs.MarkKey) is held only while a game is open: a global shortcut takes the combination
        // from every other program (Ctrl+Shift+M mutes in Teams), and without a game Mark does nothing anyway.
        // A new Prefs.MarkKey applies from the next game.
        static void MarkKey(bool on)
        {
            if (ui != null) ui.BeginInvoke(new Action(() => { if (on) Hotkeys.Bind("marca", Prefs.Now.MarkKey, () => { Action m = Marked; if (Mark() && m != null) m(); }); else Hotkeys.Clear("marca"); }));
        }
        public static event Action Marked; // the shortcut marked a lag (Mark returned true); on the UI thread

        // From the app's game check, every 2 s: the game running, or null. Starts or ends the sampling.
        public static void Seen(Game open)
        {
            lock (gate)
            {
                if (open == playing) return;
                Halt();
                if (open == null || dir == null) return;
                playing = open;
                Play p = play = new Play(open);
                ManualResetEvent q = quit = new ManualResetEvent(false);
                thread = new Thread(() => Loop(p, q)) { IsBackground = true, Name = "Partida" };
                thread.Start();
                MarkKey(true);
            }
        }

        // When the app closes: the match in progress is saved.
        public static void Stop() { lock (gate) Halt(); }
        static void Halt()
        {
            if (thread == null) return;
            MarkKey(false);
            quit.Set();
            thread.Join(5000);
            thread = null; playing = null; play = null;
        }

        // Marks a lag now in the match in progress. Without one (no game, or in the menu) it does nothing and returns false.
        public static bool Mark() { Play p = play; return p != null && p.Mark(DateTime.UtcNow); }

        // CPU temperature only as Windows gives it without a driver: the ACPI thermal zone (the same data as WMI
        // MSAcpi_ThermalZoneTemperature). Many PCs, desktops above all, do not expose one; on others it is the board, not the cores.
        // The first call reads the counter once (tens of ms): better off the UI thread.
        public static bool CpuTempAvailable { get { Thermal(); return hasTemp == true; } }
        public static string CpuTempWhy { get { Thermal(); return hasTemp == true ? null : NoTemp; } } // null when it is available
        internal const string NoTemp = "Windows no da la temperatura de la CPU en este PC. Leerla exigiría un driver de kernel, que los anticheat pueden bloquear.";
        static bool? hasTemp;
        static void Thermal()
        {
            if (hasTemp != null) return;
            bool ok = false;
            try { using (Pdh p = new Pdh(Probe.Heat)) ok = Probe.Celsius(p) != null; }
            catch (Exception) { }
            hasTemp = ok;
        }

        // ---- Reports ----
        public static string Folder(string appDir) { return Path.Combine(appDir, "partidas"); }
        // Newest first. appDir null = the app folder given to Start.
        public static List<MatchReport> Reports(string appDir = null)
        {
            try { return Files(appDir ?? dir).Select(Read).Where(r => r != null).ToList(); }
            catch (Exception) { return new List<MatchReport>(); }
        }
        public static MatchReport Latest(string appDir = null)
        {
            try { return Files(appDir ?? dir).Select(Read).FirstOrDefault(r => r != null); }
            catch (Exception) { return null; }
        }
        // The name starts with the UTC start, so name order is time order.
        static IEnumerable<string> Files(string appDir)
        {
            string f = Folder(appDir);
            return Directory.Exists(f) ? Directory.GetFiles(f, "*.json").OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal).ToList() : new List<string>();
        }
        static MatchReport Read(string file)
        {
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<MatchReport>(File.ReadAllText(file)); }
            catch (Exception) { return null; }
        }
        internal static void Save(string appDir, MatchReport r)
        {
            string f = Folder(appDir);
            Directory.CreateDirectory(f);
            Engine.WriteFile(Path.Combine(f, r.Start.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + r.Game + ".json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(r));
            foreach (string old in Files(appDir).Skip(Keep)) File.Delete(old);
        }

        // ---- The report of a match, from its samples (pure: the self-test feeds it made-up samples) ----
        internal static bool Stutter(Sample s) { return s.WorstMs != null && s.Fps > 0 && s.WorstMs >= Math.Max(StutterMs, 3000 / s.Fps.Value); }
        public static string Clock(TimeSpan d) { if (d < TimeSpan.Zero) d = TimeSpan.Zero; return (int)d.TotalMinutes + ":" + d.Seconds.ToString("00", CultureInfo.InvariantCulture); }

        internal static MatchReport Build(Game g, string map, DateTime start, DateTime end, List<Sample> s, List<DateTime> marks, double? fps, double? low1)
        {
            List<int> pings = s.Where(x => x.Ping != null).Select(x => x.Ping.Value).ToList(), ok = pings.Where(x => x >= 0).ToList();
            PingStats ps = PingStats.Of(pings);
            Func<Sample, bool> spike = x => x.Ping > ps.Median + 30;
            Func<Sample, int, Moment> at = (x, ms) => new Moment { Time = x.Time, At = Clock(x.Time - start), Ms = ms, Culprit = x.Culprit };
            MatchReport r = new MatchReport
            {
                Game = g.Id, Map = map, Start = start, End = end, Fps = fps == null ? null : (double?)Math.Round(fps.Value, 1), Low1 = low1 == null ? null : (double?)Math.Round(low1.Value, 1),
                Stutters = s.Where(Stutter).Select(x => at(x, (int)Math.Round(x.WorstMs.Value))).ToList(),
                Ping = ok.Count == 0 ? null : (int?)Math.Round(ok.Average()),
                Loss = pings.Count == 0 ? null : (double?)Math.Round(ps.Loss, 1),
                Spikes = s.Where(spike).Select(x => at(x, x.Ping.Value)).ToList(),
                Marks = marks.Select(t => new LagMark { Time = t, At = Clock(t - start), Sample = s.OrderBy(x => Math.Abs((x.Time - t).Ticks)).FirstOrDefault() }).ToList()
            };
            IGrouping<string, Sample> top = s.Where(x => (Stutter(x) || spike(x)) && x.Culprit != null).GroupBy(x => x.Culprit).OrderByDescending(x => x.Count()).FirstOrDefault();
            if (top != null) { r.Culprit = top.Key; r.CulpritTimes = top.Count(); }
            return r;
        }

        // ---- The sampling thread ----
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
        static long ThreadCpu() { long a, b, k, u; return GetThreadTimes(GetCurrentThread(), out a, out b, out k, out u) ? k + u : 0; }

        static void Loop(Play p, ManualResetEvent q)
        {
            double spent = 0;
            int n = 0;
            Probe pr = null;
            try
            {
                pr = new Probe(p.Game);
                long wait;
                do
                {
                    Stopwatch w = Stopwatch.StartNew();
                    long c0 = ThreadCpu();
                    // A failure only loses this sample: it must never end the sampling or the app.
                    try
                    {
                        List<double> frames;
                        Sample s = pr.Take(out frames);
                        Done(p.Add(s, pr.Where(), frames));
                        Last = s;
                        Fire(Sampled, s);
                    }
                    catch (Exception) { }
                    spent += (ThreadCpu() - c0) / 10000.0;
                    CostMs = spent / ++n;
                    wait = Every - w.ElapsedMilliseconds;
                } while (!q.WaitOne((int)Math.Max(200, wait)));
            }
            catch (Exception) { }
            finally
            {
                Last = null;
                try { Done(p.End(DateTime.UtcNow)); } catch (Exception) { }
                if (pr != null) pr.Dispose();
            }
        }
        static void Done(List<MatchReport> rs)
        {
            foreach (MatchReport r in rs)
            {
                try { Save(dir, r); } catch (Exception) { }
                Fire(Finished, r);
            }
        }
        static void Fire<T>(Action<T> ev, T x)
        {
            if (ev == null) return;
            foreach (Action<T> a in ev.GetInvocationList()) try { a(x); } catch (Exception) { }
        }
    }

    // One game session. Each sample goes into the match in progress, which opens and closes with where the player is:
    // where = a map or mode (in a match), "" (menu or loading) or null (the game has not said). A game that never says
    // (log unreadable, CS2 without its GSI file yet) gives one report for the whole session. Thread-safe.
    internal sealed class Play
    {
        internal const double Bin = 0.05, MaxGap = 2000; // ms: histogram bin; longer pauses are alt-tab or loading, not stutters
        const int Bins = 5000;                             // up to 250 ms; slower frames go in the last bin
        public readonly Game Game;
        readonly object gate = new object();
        bool told;
        Run cur;
        public Play(Game g) { Game = g; }

        sealed class Run
        {
            public string Map;
            public DateTime Start;
            public readonly List<Sample> Samples = new List<Sample>();
            public readonly List<DateTime> Marks = new List<DateTime>();
            public readonly int[] Hist = new int[Bins + 1]; // every frame time of the match, for its 1 % low
            public long Frames;
            public double Ms;
        }

        // frames: the frame times (ms) since the previous sample. Returns the matches that ended.
        public List<MatchReport> Add(Sample s, string where, IList<double> frames)
        {
            List<MatchReport> done = new List<MatchReport>();
            lock (gate)
            {
                // What came before the game first said where it is (starting up, loading) is not a match.
                if (where != null && !told) { told = true; cur = null; }
                string want = told && !string.IsNullOrEmpty(where) ? where : null;
                if (told && cur != null && cur.Map != want) Close(done, s.Time);
                if (cur == null && (!told || want != null)) cur = new Run { Map = want, Start = s.Time };
                if (cur == null) return done;
                cur.Samples.Add(s);
                foreach (double f in frames)
                {
                    if (f > MaxGap) continue;
                    cur.Hist[Math.Min(Bins, (int)(f / Bin))]++;
                    cur.Frames++;
                    cur.Ms += f;
                }
            }
            return done;
        }

        public bool Mark(DateTime t) { lock (gate) { if (cur == null) return false; cur.Marks.Add(t); return true; } }

        // The game closed or the app is closing.
        public List<MatchReport> End(DateTime t)
        {
            List<MatchReport> done = new List<MatchReport>();
            lock (gate) if (cur != null) Close(done, t);
            return done;
        }

        // Under a minute it is not a match (a menu between two loads), as in "Mis FPS".
        void Close(List<MatchReport> done, DateTime end)
        {
            Run r = cur;
            cur = null;
            if ((end - r.Start).TotalSeconds < FpsLog.MinSeconds) return;
            double? fps = null, low = null;
            if (r.Frames > 0)
            {
                fps = 1000.0 * r.Frames / r.Ms;
                long top = 0;
                for (int i = Bins; i >= 0; i--) { top += r.Hist[i]; if (top * 100 >= r.Frames) { low = 1000 / ((i + 0.5) * Bin); break; } }
            }
            done.Add(Tracker.Build(Game, r.Map, r.Start, end, r.Samples, r.Marks, fps, low));
        }
    }

    // Reads one sample. Each reading stands alone: one that fails leaves its value null and never throws.
    internal sealed class Probe : IDisposable
    {
        internal const string EtwName = "Tweakeroo-Partida"; // not FpsRecorder's: starting a session of the same name stops the other
        internal const string Heat = @"\Thermal Zone Information(*)\Temperature";
        readonly Game game;
        readonly string host;
        readonly Presents etw;
        readonly NvGpu nv;
        readonly Pdh gpus, heat;
        readonly object gate = new object();
        List<KeyValuePair<long, double>> fresh = new List<KeyValuePair<long, double>>(); // (FILETIME, ms) since the last sample
        long prev;
        readonly Queue<KeyValuePair<long, double>> window = new Queue<KeyValuePair<long, double>>(); // last 10 s
        readonly Queue<int> pings = new Queue<int>();
        long[] sys;
        Dictionary<long, long[]> procs;
        long procAt;
        IntPtr buf = IntPtr.Zero, scratch = Marshal.AllocHGlobal(128);
        int size = 1 << 20;
        string valWhere;
        long valAt = -1;
        string valRest = "";

        public Probe(Game g)
        {
            game = g;
            host = Host(g);
            try { etw = new Presents(EtwName, Frame); } catch (Exception) { } // without administrator: no FPS
            try { nv = new NvGpu(); } catch (Exception) { }                  // no NVIDIA driver: Windows counters
            if (nv == null) try { gpus = new Pdh(@"\GPU Engine(*)\Utilization Percentage", @"\GPU Adapter Memory(*)\Dedicated Usage"); } catch (Exception) { }
            if (Tracker.CpuTempAvailable) try { heat = new Pdh(Heat); } catch (Exception) { }
        }

        // What the ping measures. Game servers do not answer pings, and their address cannot be seen without capturing traffic.
        // Fortnite: Epic's measurement server of the chosen region (the same one the network test uses), in the same data centres
        // as the matches: the route to the region, not to the exact server. Valorant and CS2: 1.1.1.1, the home connection and the
        // provider up to the internet; a spike there does reach the game, but a calm ping there does not prove the server route is calm.
        internal static string Host(Game g)
        {
            if (g != Game.Fortnite) return NetTest.Internet;
            try
            {
                string t;
                using (StreamReader r = new StreamReader(new FileStream(IniOp.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))) t = r.ReadToEnd();
                // The region chosen in the game; without it, the one of the last matchmaking.
                foreach (string re in new[] { @"SelectedRegionId=(\w+)", @"regionId\\"":\\""(\w+)" })
                {
                    string id = Regex.Match(t, re).Groups[1].Value.ToLowerInvariant();
                    if (NetTest.Name(id) != "") return NetTest.Host(id);
                }
            }
            catch (Exception) { }
            return NetTest.Internet;
        }

        // On ETW's thread, once per frame of the game.
        void Frame(long t) { lock (gate) { if (prev != 0 && t > prev) fresh.Add(new KeyValuePair<long, double>(t, (t - prev) / 10000.0)); prev = t; } }

        public Sample Take(out List<double> frames)
        {
            Task<int> ping = null;
            try { ping = NetTest.Ping(host, 1000); } catch (Exception) { }
            DateTime now = DateTime.UtcNow;
            Sample s = new Sample { Time = now };
            frames = new List<double>();
            List<double> f = frames;
            Try(() => Frames(s, now, f));
            Try(() => Cpu(s));
            Try(() => Ram(s));
            Try(() => { if (nv != null) nv.Read(s, scratch); else if (gpus != null) Gpus(s); });
            Try(() => { if (heat != null) { heat.Collect(); s.CpuTemp = Celsius(heat); } });
            Try(() => Hogs(s));
            Try(() =>
            {
                if (ping == null || !ping.Wait(1500)) return;
                s.Ping = ping.Result;
                pings.Enqueue(ping.Result);
                if (pings.Count > 30) pings.Dequeue();
                s.Loss = Math.Round(100.0 * pings.Count(x => x < 0) / pings.Count, 1);
            });
            return s;
        }
        static void Try(Action a) { try { a(); } catch (Exception) { } }

        void Frames(Sample s, DateTime now, List<double> got)
        {
            List<KeyValuePair<long, double>> f;
            lock (gate) { f = fresh; fresh = new List<KeyValuePair<long, double>>(); }
            foreach (KeyValuePair<long, double> x in f) { got.Add(x.Value); if (x.Value <= Play.MaxGap) window.Enqueue(x); }
            long from = now.ToFileTimeUtc() - 10 * TimeSpan.TicksPerSecond;
            while (window.Count > 0 && window.Peek().Key < from) window.Dequeue();
            List<double> ok = got.Where(x => x <= Play.MaxGap).ToList();
            if (ok.Count > 0) s.WorstMs = Math.Round(ok.Max(), 1);
            if (window.Count < 2) return;
            double[] ms = window.Select(x => x.Value).OrderByDescending(x => x).ToArray();
            s.Fps = Math.Round(1000 * ms.Length / ms.Sum(), 1);
            s.Low1 = Math.Round(1000 / ms[Math.Max(0, ms.Length / 100 - 1)], 1);
        }

        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        void Cpu(Sample s)
        {
            long i, k, u;
            if (!GetSystemTimes(out i, out k, out u)) return;
            if (sys != null) { long all = k - sys[1] + u - sys[2]; if (all > 0) s.Cpu = Math.Round(100.0 * (all - (i - sys[0])) / all, 1); } // kernel time includes idle
            sys = new[] { i, k, u };
        }

        [StructLayout(LayoutKind.Sequential)] struct Mem { public int Size; public uint Load; public ulong Total, Free, PageTotal, PageFree, VirtualTotal, VirtualFree, Extended; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref Mem m);
        static void Ram(Sample s)
        {
            Mem m = new Mem { Size = Marshal.SizeOf(typeof(Mem)) };
            if (!GlobalMemoryStatusEx(ref m)) return;
            s.RamTotalMb = Math.Round(m.Total / 1048576.0);
            s.RamMb = Math.Round((m.Total - m.Free) / 1048576.0);
        }

        // Without NVIDIA: the 3D engine of every process added up, as Task Manager's GPU column, and the dedicated memory in use.
        // shortcut: with two GPUs both 3D engines add up, split by adapter (luid_) if that ever matters.
        void Gpus(Sample s)
        {
            gpus.Collect();
            List<KeyValuePair<string, double>> e = gpus.Values(0).Where(x => x.Key.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToList();
            if (e.Count > 0) s.Gpu = Math.Round(Math.Min(100, e.Sum(x => x.Value)));
            List<KeyValuePair<string, double>> m = gpus.Values(1);
            if (m.Count > 0) s.VramMb = Math.Round(m.Max(x => x.Value) / 1048576);
        }

        // The hottest ACPI thermal zone, in °C (the counter gives kelvin); null if none or out of range.
        internal static double? Celsius(Pdh p)
        {
            List<KeyValuePair<string, double>> v = p.Values(0);
            if (v.Count == 0) return null;
            double c = v.Max(x => x.Value) - 273.15;
            return c > 0 && c < 125 ? (double?)Math.Round(c) : null;
        }

        // Every process in one call (no OpenProcess: protected ones, anticheats included, count too). SYSTEM_PROCESS_INFORMATION, x64.
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int what, IntPtr buffer, int length, out int needed);
        const int ProcessList = 5, TooSmall = unchecked((int)0xC0000004);
        void Hogs(Sample s)
        {
            int need, st;
            if (buf == IntPtr.Zero) buf = Marshal.AllocHGlobal(size);
            while ((st = NtQuerySystemInformation(ProcessList, buf, size, out need)) == TooSmall) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; size = Math.Max(need, size) + (1 << 16); buf = Marshal.AllocHGlobal(size); }
            if (st != 0) return;
            long at = DateTime.UtcNow.Ticks, dt = at - procAt;
            Dictionary<long, long[]> was = procs;
            procs = new Dictionary<long, long[]>();
            procAt = at;
            Dictionary<string, long> cpu = new Dictionary<string, long>(), io = new Dictionary<string, long>();
            List<int> mine = new List<int>();
            for (int off = 0; ; )
            {
                IntPtr p = buf + off;
                long pid = Marshal.ReadInt64(p, 0x50);
                if (pid != 0) // 0 = idle
                {
                    IntPtr np = Marshal.ReadIntPtr(p, 0x40);
                    string name = np == IntPtr.Zero ? "" : Marshal.PtrToStringUni(np, Marshal.ReadInt16(p, 0x38) / 2);
                    // creation time, user + kernel time, bytes read + written
                    long[] now = { Marshal.ReadInt64(p, 0x20), Marshal.ReadInt64(p, 0x28) + Marshal.ReadInt64(p, 0x30), Marshal.ReadInt64(p, 0xE8) + Marshal.ReadInt64(p, 0xF0) };
                    procs[pid] = now;
                    long[] old;
                    if (string.Equals(name, game.ExeName, StringComparison.OrdinalIgnoreCase)) mine.Add((int)pid);
                    else if (was != null && was.TryGetValue(pid, out old) && old[0] == now[0])
                    {
                        // By name: a browser's twenty processes are one culprit.
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                        Add(cpu, name, now[1] - old[1]);
                        Add(io, name, now[2] - old[2]);
                    }
                }
                int next = Marshal.ReadInt32(p);
                if (next == 0) break;
                off += next;
            }
            if (etw != null) etw.Watch(mine);
            if (was == null || dt <= 0) return;
            if (cpu.Count > 0) { KeyValuePair<string, long> c = cpu.OrderByDescending(x => x.Value).First(); s.CpuHog = c.Key; s.CpuHogPct = Math.Round(100.0 * c.Value / dt / Environment.ProcessorCount, 1); }
            if (io.Count > 0) { KeyValuePair<string, long> d = io.OrderByDescending(x => x.Value).First(); s.DiskHog = d.Key; s.DiskHogMbs = Math.Round(d.Value / 1048576.0 / (dt / (double)TimeSpan.TicksPerSecond), 1); }
        }
        static void Add(Dictionary<string, long> d, string k, long v) { long had; d.TryGetValue(k, out had); d[k] = had + Math.Max(0, v); }

        // Where the player is, from what each game already tells (see Play).
        public string Where()
        {
            try
            {
                if (game == Game.Fortnite)
                {
                    // The lobby is the Frontend world (and Login before it); any other world brought up is a match, as for the crosshair.
                    string w = Macros.Read();
                    return w == null ? null : w.Contains("/Frontend") || w.Contains("/Login") ? "" : FpsLog.MapName(w);
                }
                if (game == Game.Valorant) return ValorantWhere();
                if (game == Game.Cs2) { Cs2Gsi g = Cs2Gsi.Now; return g == null ? null : g.Map; }
            }
            catch (Exception) { }
            return null;
        }

        // Valorant: the last LogMapLoadModel line of ShooterGame.log names the map; with Local World TRUE it is being played
        // (the menu, MainMenuV2, and loading have FALSE), as "Mis FPS" reads it. Only what is new is read, starting at the end:
        // what is already there is from before. A read that fails keeps the last answer.
        const string ValMark = "LogMapLoadModel: Update: [Map Name: ";
        string ValorantWhere()
        {
            try
            {
                using (FileStream f = new FileStream(Path.Combine(Valorant.Logs, "ShooterGame.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (valAt < 0) valAt = f.Length;
                    if (f.Length < valAt) { valAt = 0; valRest = ""; } // a new log
                    f.Seek(valAt, SeekOrigin.Begin);
                    byte[] b = new byte[f.Length - valAt];
                    int n = f.Read(b, 0, b.Length);
                    valAt += n;
                    string[] lines = (valRest + Encoding.UTF8.GetString(b, 0, n)).Split('\n');
                    valRest = lines[lines.Length - 1];
                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        string l = lines[i], name;
                        if (l.IndexOf(ValMark, StringComparison.Ordinal) != 30) continue;
                        int from = 30 + ValMark.Length, to = l.IndexOf(' ', from);
                        string code = to < 0 ? "" : l.Substring(from, to - from);
                        valWhere = l.IndexOf("[Local World: TRUE", StringComparison.Ordinal) < 0 ? "" : FpsLog.ValMaps.TryGetValue(code, out name) ? name : code;
                    }
                }
            }
            catch (Exception) { }
            return valWhere;
        }

        public void Dispose()
        {
            if (etw != null) etw.Dispose(); // the real-time session outlives the process: always stopped
            if (gpus != null) gpus.Dispose();
            if (heat != null) heat.Dispose();
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            Marshal.FreeHGlobal(scratch);
            buf = IntPtr.Zero;
        }
    }

    // NVIDIA GPU through NVAPI (already used for scaling and vibrance): usage, temperature and video memory in use. Throws without the driver.
    internal sealed class NvGpu
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Query(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FEnum([Out] IntPtr[] gpus, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FGet(IntPtr gpu, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FTherm(IntPtr gpu, int sensor, IntPtr data);
        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = Query(id);
            if (p == IntPtr.Zero) throw new EntryPointNotFoundException("NVAPI " + id.ToString("X8"));
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        readonly IntPtr gpu;
        readonly FGet states, memory;
        readonly FTherm therm;
        public NvGpu()
        {
            if (Fn<FInit>(0x0150E828)() != 0) throw new InvalidOperationException("NVAPI");
            IntPtr[] g = new IntPtr[64];
            int n;
            if (Fn<FEnum>(0xE5AC921F)(g, out n) != 0 || n < 1) throw new InvalidOperationException("NVAPI sin GPU");
            gpu = g[0];
            states = Fn<FGet>(0x60DED2ED);  // NvAPI_GPU_GetDynamicPstatesInfoEx
            memory = Fn<FGet>(0x07F9B368);  // NvAPI_GPU_GetMemoryInfo
            therm = Fn<FTherm>(0xE3640A56); // NvAPI_GPU_GetThermalSettings
        }
        // b: 128 bytes to fill. Each struct starts with its size and version.
        public void Read(Sample s, IntPtr b)
        {
            // utilization[0] = graphics engine: present bit, then percentage
            if (Call(b, 72 | 1 << 16, p => states(gpu, p)) && (Marshal.ReadInt32(b, 8) & 1) != 0) s.Gpu = Marshal.ReadInt32(b, 12);
            // all sensors (15); sensor[0].currentTemp is the GPU core
            if (Call(b, 68 | 2 << 16, p => therm(gpu, 15, p)) && Marshal.ReadInt32(b, 4) > 0) s.GpuTemp = Marshal.ReadInt32(b, 20);
            // KB: available dedicated minus currently available dedicated
            if (Call(b, 24 | 2 << 16, p => memory(gpu, p))) s.VramMb = Math.Round((Marshal.ReadInt32(b, 8) - Marshal.ReadInt32(b, 20)) / 1024.0);
        }
        static bool Call(IntPtr b, int version, Func<IntPtr, int> f)
        {
            for (int i = 0; i < 128; i += 4) Marshal.WriteInt32(b, i, 0);
            Marshal.WriteInt32(b, 0, version);
            return f(b) == 0;
        }
    }

    // A PDH query of English counter paths (the same in every Windows language). A path may end its instance with a wildcard.
    internal sealed class Pdh : IDisposable
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQueryW(string source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArrayW(IntPtr counter, int format, ref int size, out int count, IntPtr items);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
        const int AsDouble = 0x200, NoCap = 0x8000, MoreData = unchecked((int)0x800007D2);
        readonly IntPtr q;
        readonly IntPtr[] c;
        public Pdh(params string[] paths)
        {
            int e = PdhOpenQueryW(null, IntPtr.Zero, out q);
            if (e != 0) throw new System.ComponentModel.Win32Exception(e);
            c = paths.Select(x => { IntPtr h; return PdhAddEnglishCounterW(q, x, IntPtr.Zero, out h) == 0 ? h : IntPtr.Zero; }).ToArray();
            PdhCollectQueryData(q); // rates need two collections: the first values come with the next one
        }
        public void Collect() { PdhCollectQueryData(q); }
        // Instance name and value of counter i; empty if it has none, is unknown or has no valid data yet.
        public List<KeyValuePair<string, double>> Values(int i)
        {
            List<KeyValuePair<string, double>> r = new List<KeyValuePair<string, double>>();
            int size = 0, n;
            if (c[i] == IntPtr.Zero || PdhGetFormattedCounterArrayW(c[i], AsDouble | NoCap, ref size, out n, IntPtr.Zero) != MoreData) return r;
            IntPtr b = Marshal.AllocHGlobal(size);
            try
            {
                if (PdhGetFormattedCounterArrayW(c[i], AsDouble | NoCap, ref size, out n, b) != 0) return r;
                // PDH_FMT_COUNTERVALUE_ITEM_W, x64: name pointer, status (0 or 1 = valid), padding, double
                for (int k = 0; k < n; k++)
                    if ((uint)Marshal.ReadInt32(b, k * 24 + 8) <= 1)
                        r.Add(new KeyValuePair<string, double>(Marshal.PtrToStringUni(Marshal.ReadIntPtr(b, k * 24)) ?? "", BitConverter.Int64BitsToDouble(Marshal.ReadInt64(b, k * 24 + 16))));
            }
            finally { Marshal.FreeHGlobal(b); }
            return r;
        }
        public void Dispose() { PdhCloseQuery(q); }
    }
}
