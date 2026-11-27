// "Listo para jugar" on the Play page: a check of up to 3 s before playing (heavy programs, free RAM, temperatures,
// ping, graphics driver, power plan). It only reads; each fix runs from its own button and then rechecks that row.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Tablero
{
    // What one check read. Only the fields of its row are filled.
    public class ReadyData
    {
        public List<string> Heavy;      // progs, ram: programs worth closing
        public double Free, Total;      // ram, GB
        public double? Temp;            // gpu, cpu: °C
        public List<int> Pings;         // ping: ms, -1 = lost
        public string Now, Last;        // driver: installed and latest version
        public Guid Plan;               // plan: active
        public Guid? Target;            // plan: the one to switch to; null = the active one is fine
    }

    public class ReadyRow : Vm
    {
        public string Id;
        public ReadyData Data;
        public bool Ok { get; set; }
        public string Text { get; set; }
        public string Fix { get; set; }  // button text; null = no button
        public bool Bad { get { return !Ok; } }
        public bool HasFix { get { return !Ok && Fix != null; } }
        public void Set(ReadyRow r)
        {
            Data = r.Data; Ok = r.Ok; Text = r.Text; Fix = r.Fix;
            foreach (string p in new[] { "Ok", "Bad", "Text", "Fix", "HasFix" }) Raise(p);
        }
    }

    public static class Ready
    {
        public static readonly string[] Ids = { "progs", "ram", "gpu", "cpu", "ping", "driver", "plan" };
        public const int Limit = 3000; // ms for the whole check; what does not answer in time is left out
        static readonly Guid Saver = new Guid("a1841308-3542-4fab-bc81-f71556f20b4a"), Ultimate = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");

        // ---- Rules (pure: the self-test feeds them made-up data) ----
        public static bool RamLow(double free, double total) { return free < 2 || free < total * 0.15; }
        public static bool Hot(string id, double c) { return c >= (id == "gpu" ? 83 : 85); }
        public static int? Median(List<int> ms) { List<int> b = ms.Where(x => x >= 0).OrderBy(x => x).ToList(); return b.Count == 0 ? (int?)null : b[b.Count / 2]; }
        public static bool PingBad(List<int> ms) { int? m = Median(ms); return m == null || m > 80 || ms.Any(x => x < 0); }
        // Power saver or Balanced with High (or Ultimate) performance available. Any other plan, Razer Cortex's own included, is fine.
        public static Guid? PlanTarget(Guid active, ICollection<Guid> plans)
        {
            if (active != Saver && active != Laptop.Balanced) return null;
            if (plans.Contains(Laptop.High)) return Laptop.High;
            if (plans.Contains(Ultimate)) return Ultimate;
            return active == Saver && plans.Contains(Laptop.Balanced) ? Laptop.Balanced : (Guid?)null;
        }
        static string PlanName(Guid g) { return g == Saver ? "Ahorro de energía" : g == Laptop.Balanced ? "Equilibrado" : g == Laptop.High ? "Alto rendimiento" : g == Ultimate ? "Máximo rendimiento" : null; }
        static string Gb(double v) { return v.ToString("0.0"); }

        // The row of one check; null = no data.
        public static ReadyRow Make(string id, ReadyData d)
        {
            if (d == null) return null;
            ReadyRow r = new ReadyRow { Id = id, Data = d };
            switch (id)
            {
                case "progs":
                    if (d.Heavy == null) return null;
                    r.Ok = d.Heavy.Count == 0; r.Fix = "Cerrar";
                    r.Text = r.Ok ? "Sin programas pesados abiertos" : "Programas pesados abiertos: " + string.Join(", ", d.Heavy);
                    break;
                case "ram":
                    if (d.Total <= 0) return null;
                    r.Ok = !RamLow(d.Free, d.Total);
                    r.Fix = d.Heavy != null && d.Heavy.Count > 0 ? "Cerrar" : null;
                    r.Text = (r.Ok ? "Memoria libre: " : "Poca memoria libre: ") + Gb(d.Free) + " GB";
                    break;
                case "gpu":
                case "cpu":
                    if (d.Temp == null) return null;
                    r.Ok = !Hot(id, d.Temp.Value);
                    string who = id == "gpu" ? "Gráfica" : "CPU";
                    r.Text = r.Ok ? who + " a " + d.Temp + " °C" : who + " caliente en reposo: " + d.Temp + " °C";
                    break;
                case "ping":
                    if (d.Pings == null || d.Pings.Count == 0) return null;
                    int lost = d.Pings.Count(x => x < 0);
                    int? m = Median(d.Pings);
                    r.Ok = !PingBad(d.Pings); r.Fix = "Ver conexión";
                    r.Text = m == null ? "Sin respuesta al ping" : lost > 0 ? "Pérdida de paquetes: " + lost + " de " + d.Pings.Count : (r.Ok ? "Ping: " : "Ping alto: ") + m + " ms";
                    break;
                case "driver":
                    Version now, last;
                    if (!Version.TryParse(d.Now ?? "", out now) || !Version.TryParse(d.Last ?? "", out last)) return null;
                    r.Ok = now >= last; r.Fix = "Ver driver";
                    r.Text = r.Ok ? "Driver de la gráfica al día" : "Driver de la gráfica: hay uno más nuevo (" + d.Last + ")";
                    break;
                case "plan":
                    r.Ok = d.Target == null; r.Fix = "Cambiar plan";
                    string name = PlanName(d.Plan);
                    r.Text = name == null ? "Plan de energía correcto" : "Plan de energía: " + name;
                    break;
                default: return null;
            }
            return r;
        }

        // Only what needs looking at; in check order.
        public static List<ReadyRow> Rows(Dictionary<string, ReadyData> got)
        {
            return Ids.Where(got.ContainsKey).Select(id => Make(id, got[id])).Where(r => r != null && !r.Ok).ToList();
        }
        public static string Head(int bad) { return bad == 0 ? "Todo listo para jugar." : bad == 1 ? "1 cosa que revisar" : bad + " cosas que revisar"; }
        // One line with what was read: "RAM libre 9,8 GB · GPU 48 °C · ping 41 ms". Each part already translated.
        public static string Summary(Dictionary<string, ReadyData> got)
        {
            List<string> p = new List<string>();
            ReadyData d;
            if (got.TryGetValue("ram", out d) && d.Total > 0) p.Add("RAM libre " + Gb(d.Free) + " GB");
            if (got.TryGetValue("gpu", out d) && d.Temp != null) p.Add("GPU " + d.Temp + " °C");
            if (got.TryGetValue("cpu", out d) && d.Temp != null) p.Add("CPU " + d.Temp + " °C");
            int? m;
            if (got.TryGetValue("ping", out d) && d.Pings != null && (m = Median(d.Pings)) != null) p.Add("ping " + m + " ms");
            return string.Join(" · ", p.Select(L.T));
        }

        // ---- Reading (off the UI thread) ----
        // The Play page list: the 5 programs of this session using the most memory, from 300 MB, in MB.
        public static Dictionary<string, long> Memory()
        {
            int session = Process.GetCurrentProcess().SessionId;
            string[] skip = { "Tweakeroo", "explorer", "dwm", "Memory Compression", "FortniteClient-Win64-Shipping", "VALORANT-Win64-Shipping", "cs2", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "svchost" };
            try { return Process.GetProcesses().Where(p => p.SessionId == session && !skip.Contains(p.ProcessName)).GroupBy(p => p.ProcessName).Select(g => new { g.Key, Mb = g.Sum(p => p.WorkingSet64) / (1 << 20) }).Where(x => x.Mb >= 300).OrderByDescending(x => x.Mb).Take(5).ToDictionary(x => x.Key, x => x.Mb); }
            catch (InvalidOperationException) { return new Dictionary<string, long>(); }
            catch (System.ComponentModel.Win32Exception) { return new Dictionary<string, long>(); }
        }
        // Worth closing: from that list, those from 1 GB, except the launchers the games need.
        static readonly string[] Launchers = { "EpicGamesLauncher", "EpicWebHelper", "steam", "steamwebhelper", "RiotClientServices", "Riot Client", "RiotClientUx", "vgtray" };
        public static List<string> Heavy() { return Memory().Where(x => x.Value >= 1024 && !Launchers.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).Select(x => x.Key).ToList(); }

        public static ReadyData Fetch(string id, Game game, Hw hw, string dir)
        {
            switch (id)
            {
                case "progs": return new ReadyData { Heavy = Heavy() };
                case "ram": return new ReadyData { Free = Display.FreeRamGb(), Total = Display.TotalRamGb(), Heavy = Heavy() };
                case "gpu":
                    IntPtr b = Marshal.AllocHGlobal(128);
                    try { Sample s = new Sample(); new NvGpu().Read(s, b); return s.GpuTemp == null ? null : new ReadyData { Temp = s.GpuTemp }; }
                    finally { Marshal.FreeHGlobal(b); }
                case "cpu":
                    if (!Tracker.CpuTempAvailable) return null;
                    using (Pdh p = new Pdh(Probe.Heat)) { double? c = Probe.Celsius(p); return c == null ? null : new ReadyData { Temp = c }; }
                case "ping":
                    string host = Probe.Host(game);
                    return new ReadyData { Pings = Enumerable.Range(0, 3).Select(i => NetTest.Ping(host, 800).Result).ToList() };
                case "driver":
                    if (hw == null) return null;
                    if (Prefs.Now.AutoCheck && !Drivers.Fresh) Drivers.Check(dir, hw);
                    return Drivers.Advice(hw) == null ? null : new ReadyData { Now = Drivers.Installed(hw.Driver), Last = Drivers.Known.Version };
                case "plan":
                    // On a laptop on battery, Balanced is what the laptop profile wants.
                    if (System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline) return null;
                    Guid a = Power.Active();
                    return new ReadyData { Plan = a, Target = PlanTarget(a, Power.Schemes()) };
            }
            return null;
        }

        // Every check at once; one that fails or does not answer within Limit is left out.
        public static async Task<Dictionary<string, ReadyData>> Gather(IEnumerable<string> ids, Game game, Hw hw, string dir)
        {
            Dictionary<string, Task<ReadyData>> t = ids.ToDictionary(id => id, id => Task.Run(() => Fetch(id, game, hw, dir)));
            await Task.WhenAny(Task.WhenAll(t.Values), Task.Delay(Limit));
            return t.Where(x => x.Value.Status == TaskStatus.RanToCompletion && x.Value.Result != null).ToDictionary(x => x.Key, x => x.Value.Result);
        }

        // ---- Self-test ----
        public static IEnumerable<string> Texts()
        {
            List<string> r = new List<string> { "¿Listo para jugar?", "Revisando…", "Cerrar", "Ver conexión", "Ver driver", "Cambiar plan", "No se pudo cambiar: x", Head(0), Head(1), Head(3) };
            foreach (Dictionary<string, ReadyData> g in new[] { Example(false), Example(true) })
            {
                r.AddRange(new[] { "RAM libre 9,8 GB", "GPU 48 °C", "CPU 50 °C", "ping 41 ms" });
                foreach (string id in Ids) { ReadyRow x = Make(id, g.ContainsKey(id) ? g[id] : null); if (x != null) r.Add(x.Text); }
            }
            ReadyData hot = new ReadyData { Temp = 90 }, lost = new ReadyData { Pings = new List<int> { 40, -1, 41 } }, none = new ReadyData { Pings = new List<int> { -1, -1, -1 } };
            foreach (ReadyRow x in new[] { Make("gpu", hot), Make("cpu", hot), Make("cpu", new ReadyData { Temp = 50 }), Make("ping", lost), Make("ping", none), Make("progs", new ReadyData { Heavy = new List<string>() }),
                Make("ram", new ReadyData { Free = 1.5, Total = 16 }), Make("driver", new ReadyData { Now = "621.14", Last = "621.24" }), Make("plan", new ReadyData { Plan = Saver, Target = Laptop.High }), Make("plan", new ReadyData { Plan = Ultimate }), Make("plan", new ReadyData { Plan = Guid.NewGuid() }) })
                r.Add(x.Text);
            return r;
        }
        // Sample results: all fine, or 3 things to look at (also the SHOT captures).
        public static Dictionary<string, ReadyData> Example(bool bad)
        {
            return new Dictionary<string, ReadyData> {
                { "progs", new ReadyData { Heavy = bad ? new List<string> { "chrome", "Discord" } : new List<string>() } },
                { "ram", new ReadyData { Free = 9.8, Total = 32, Heavy = bad ? new List<string> { "chrome", "Discord" } : new List<string>() } },
                { "gpu", new ReadyData { Temp = 48 } },
                { "ping", new ReadyData { Pings = bad ? new List<int> { 112, 118, 109 } : new List<int> { 41, 40, 43 } } },
                { "driver", new ReadyData { Now = "621.24", Last = "621.24" } },
                { "plan", bad ? new ReadyData { Plan = Laptop.Balanced, Target = Laptop.High } : new ReadyData { Plan = Laptop.High } } };
        }
        internal static void Test(Action<bool, string> Is)
        {
            Is(RamLow(1.9, 64) && RamLow(2.2, 16) && !RamLow(3, 16) && !RamLow(2, 8), "listo: memoria libre");
            Is(Make("gpu", new ReadyData { Temp = 83 }).Bad && Make("gpu", new ReadyData { Temp = 82 }).Ok && Make("cpu", new ReadyData { Temp = 85 }).Bad && Make("cpu", new ReadyData { Temp = 84 }).Ok && Make("gpu", new ReadyData()) == null, "listo: temperaturas");
            Is(!PingBad(new List<int> { 41, 40, 43 }) && Median(new List<int> { 41, 40, 43 }) == 41 && PingBad(new List<int> { 90, 30, 95 }) && PingBad(new List<int> { 40, -1, 41 }) && PingBad(new List<int> { -1, -1, -1 }) && !PingBad(new List<int> { 80, 79, 81 }), "listo: ping");
            Guid cortex = Guid.NewGuid();
            Is(PlanTarget(Saver, new[] { Saver, Laptop.Balanced, Laptop.High }) == Laptop.High && PlanTarget(Laptop.Balanced, new[] { Laptop.Balanced, Laptop.High }) == Laptop.High
                && PlanTarget(Laptop.Balanced, new[] { Laptop.Balanced }) == null && PlanTarget(Laptop.High, new[] { Laptop.Balanced, Laptop.High }) == null
                && PlanTarget(cortex, new[] { cortex, Laptop.Balanced, Laptop.High }) == null && PlanTarget(Saver, new[] { Saver, Laptop.Balanced }) == Laptop.Balanced
                && PlanTarget(Laptop.Balanced, new[] { Laptop.Balanced, Ultimate }) == Ultimate, "listo: plan de energía");
            Is(Make("driver", new ReadyData { Now = "621.14", Last = "621.24" }).Bad && Make("driver", new ReadyData { Now = "621.24", Last = "621.24" }).Ok && Make("driver", new ReadyData()) == null, "listo: driver");
            Is(Make("ram", new ReadyData { Free = 1, Total = 16, Heavy = new List<string>() }).Fix == null && Make("ram", new ReadyData { Free = 1, Total = 16, Heavy = new List<string> { "x" } }).Fix == "Cerrar", "listo: cerrar desde la memoria");
            Is(Rows(Example(false)).Count == 0 && Head(0) == "Todo listo para jugar." && Summary(Example(false)) == "RAM libre " + (9.8).ToString("0.0") + " GB · GPU 48 °C · ping 41 ms", "listo: todo bien");
            List<ReadyRow> bad = Rows(Example(true));
            Is(bad.Select(x => x.Id).SequenceEqual(new[] { "progs", "ping", "plan" }) && Head(bad.Count) == "3 cosas que revisar" && bad[0].Text == "Programas pesados abiertos: chrome, Discord", "listo: 3 cosas");
            Is(Rows(new Dictionary<string, ReadyData>()).Count == 0 && Summary(new Dictionary<string, ReadyData>()) == "", "listo: sin datos no se inventa nada");
        }
    }

    // The button and the result card on the Play page.
    partial class Ui
    {
        ObservableCollection<ReadyRow> readyRows = new ObservableCollection<ReadyRow>();

        void StartReady()
        {
            F<Btn>("ReadyGo").Click += async (s, e) => await CheckReady();
            F<ItemsControl>("ReadyRows").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ReadyFix));
        }

        async Task CheckReady()
        {
            Btn b = F<Btn>("ReadyGo");
            b.IsEnabled = false; b.Content = "Revisando…";
            ShowReady(await Ready.Gather(Ready.Ids, game, hw ?? quick, dir));
            b.Content = "¿Listo para jugar?"; b.IsEnabled = true;
        }

        internal void ShowReady(Dictionary<string, ReadyData> got)
        {
            readyRows = new ObservableCollection<ReadyRow>(Ready.Rows(got));
            F<ItemsControl>("ReadyRows").ItemsSource = readyRows;
            F<TextBlock>("ReadySum").Text = Ready.Summary(got);
            ReadyHead();
            FrameworkElement box = F<FrameworkElement>("ReadyBox");
            box.Visibility = Visibility.Visible;
            Fx.Enter(box, 0, 20);
        }

        void ReadyHead()
        {
            int bad = readyRows.Count(r => !r.Ok);
            F<TextBlock>("ReadyTitle").Text = Ready.Head(bad);
            F<FrameworkElement>("ReadyOk").Visibility = bad == 0 ? Visibility.Visible : Visibility.Collapsed;
            F<FrameworkElement>("ReadyWarn").Visibility = bad == 0 ? Visibility.Collapsed : Visibility.Visible;
            F<FrameworkElement>("ReadySum").Visibility = readyRows.Count == 0 && F<TextBlock>("ReadySum").Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            F<FrameworkElement>("ReadyRows").Visibility = readyRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        async void ReadyFix(object sender, RoutedEventArgs e)
        {
            Button b = e.OriginalSource as Button;
            ReadyRow r = b == null ? null : b.DataContext as ReadyRow;
            if (r == null || r.Ok) return;
            try
            {
                if (r.Id == "ping") { Open("Conexión"); return; }
                if (r.Id == "driver") { if (Drivers.Known != null) StretchSession.Open(Drivers.Known.Url); return; }
                b.IsEnabled = false;
                if (r.Id == "plan") Power.Activate(r.Data.Target.Value);
                else await Task.Run(() => Heavy.Close(r.Data.Heavy.Select(n => new HeavyApp { Proc = n }).ToList())); // same gentle close as Heavy.cs, waits up to 5 s
            }
            catch (Exception ex) { Say("No se pudo cambiar: " + ex.Message, false); }
            Dictionary<string, ReadyData> got = await Ready.Gather(new[] { r.Id }, game, hw ?? quick, dir);
            ReadyData d;
            ReadyRow now = got.TryGetValue(r.Id, out d) ? Ready.Make(r.Id, d) : null;
            if (now != null) r.Set(now);
            b.IsEnabled = true;
            ReadyHead();
        }
    }
}
