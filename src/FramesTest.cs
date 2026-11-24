using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Tablero
{
    // Self-test of CS2's FPS and maps (PLAN-CS2 §7, tests 5-7). No administrator; files only in the temporary folder, never in the game's.
    static class FramesTest
    {
        static void Is(bool ok, string what) { if (!ok) throw new Exception("FALLA: " + what); }

        public static void Run(string dir)
        {
            Etw(dir);
            Recorder(dir);
            Gsi(dir);
            Cfg(dir);
            bool en = L.En;
            L.En = true;
            try { foreach (string x in new[] { "Partida", FpsRecorder.Admin, "No se pudieron medir los FPS de CS2 (código 87)." }) Is(L.T(x) != x, "sin traducir: " + x); }
            finally { L.En = en; }
        }

        [DllImport("advapi32.dll")] static extern int EventRegister(ref Guid provider, IntPtr callback, IntPtr context, out long handle);
        [DllImport("advapi32.dll")] static extern int EventUnregister(long handle);
        [DllImport("advapi32.dll")] static extern int EventWrite(long handle, ref Descriptor d, int count, ref DataDescriptor data);
        [StructLayout(LayoutKind.Sequential)] struct Descriptor { public ushort Id; public byte Version, Channel, Level, Opcode; public ushort Task; public ulong Keyword; }
        [StructLayout(LayoutKind.Sequential)] struct DataDescriptor { public long Ptr; public int Size, Reserved; }

        // A private session of this process, with its own provider, written to an .etl and read back: checks the offsets of
        // EVENT_TRACE_PROPERTIES, EVENT_TRACE_LOGFILE and EVENT_RECORD, the callback, the clock and the frame filter of Presents.
        // Real-time delivery (the only difference) needs administrator.
        static void Etw(string dir)
        {
            Guid mine = new Guid("6b8f3a52-1d4e-4c7a-9f0e-2a3b4c5d6e7f");
            string etl = Path.Combine(dir, "fps-prueba.etl");
            long reg;
            Is(EventRegister(ref mine, IntPtr.Zero, IntPtr.Zero, out reg) == 0, "EventRegister");
            int got = 0, wrongPid = 0, wrongId = 0, badData = 0, frames = 0, self = Process.GetCurrentProcess().Id;
            long first = 0, last = 0;
            EtwSession.RecordCallback cb = r =>
            {
                if (EtwSession.Provider(r) != mine) return; // the file header comes from ETW itself
                if (EtwSession.Pid(r) != self) wrongPid++;
                if (EtwSession.Id(r) != 42) wrongId++;
                if (EtwSession.DataLength(r) != 16 || (EtwSession.HeaderFlags(r) & 0x40) == 0) badData++;
                if (Presents.Counts(r, mine)) frames++;
                long t = EtwSession.Time(r);
                if (first == 0) first = t;
                last = t;
                got++;
            };
            EtwSession s = null;
            IntPtr payload = Marshal.AllocHGlobal(16);
            try
            {
                s = new EtwSession("Tweakeroo-FPS-Prueba-" + Process.GetCurrentProcess().Id, cb, etl, mine);
                s.EnableProvider(mine, 4, 0x2);
                Descriptor d = new Descriptor { Id = 42, Level = 4, Opcode = 1, Task = 9, Keyword = 0x2 };
                DataDescriptor data = new DataDescriptor { Ptr = payload.ToInt64(), Size = 16 };
                DateTime began = DateTime.UtcNow;
                Stopwatch sw = Stopwatch.StartNew();
                // Like DXGI Present_Start: swap chain pointer, Flags, SyncInterval. One in ten is a test present.
                for (int i = 0; i < 100; i++)
                {
                    Marshal.WriteInt64(payload, 0, 0x1234); Marshal.WriteInt32(payload, 8, i % 10 == 0 ? 1 : 0); Marshal.WriteInt32(payload, 12, 0);
                    EventWrite(reg, ref d, 1, ref data);
                    Thread.Sleep(5);
                }
                sw.Stop();
                s.Dispose(); s = null;
                EtwSession.Replay(etl, cb);
                double span = (last - first) / 1e7, off = Math.Abs((DateTime.FromFileTimeUtc(first) - began).TotalSeconds);
                Is(got == 100 && wrongPid == 0 && wrongId == 0 && badData == 0 && frames == 90 && Math.Abs(span - sw.Elapsed.TotalSeconds) < 0.5 && off < 2,
                    "etw: " + got + "/100 eventos, pid mal " + wrongPid + ", id mal " + wrongId + ", datos mal " + badData + ", fotogramas " + frames + "/90, " + span.ToString("0.00") + " s frente a " + sw.Elapsed.TotalSeconds.ToString("0.00") + " s, reloj a " + off.ToString("0.00") + " s");
            }
            finally { if (s != null) s.Dispose(); EventUnregister(reg); Marshal.FreeHGlobal(payload); GC.KeepAlive(cb); }
            // Without administrator a real-time session is refused: nothing breaks and the reason is kept for the interface.
            if (!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                using (FpsRecorder live = FpsRecorder.Live()) { live.Watch(new[] { 1 }); Is(FpsRecorder.Status == FpsRecorder.Admin && live.Finish(DateTime.UtcNow, 0) == null, "sin administrador: " + FpsRecorder.Status); }
        }

        static readonly DateTime T0 = new DateTime(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);
        // seconds of frames at that FPS from T0, without those of the gap
        static FpsRecorder Fake(int seconds, int fps, int gapFrom = -1, int gapTo = -1)
        {
            FpsRecorder r = new FpsRecorder();
            for (long i = 0; i < seconds * fps; i++) if (i / fps < gapFrom || i / fps >= gapTo) r.Frame(T0.AddTicks(i * TimeSpan.TicksPerSecond / fps));
            return r;
        }

        static void Recorder(string dir)
        {
            // 120 s at 300 FPS: the first 20 s are loading and 5 s with no frames (minimized) do not count
            FpsRecorder r = Fake(120, 300, 50, 55);
            Match m = r.Finish(T0, 240);
            Is(m != null && m.Median == 300 && m.Low == 300 && m.Max == 300 && m.Seconds == 95 && m.Map == "Partida" && m.Cap == 240
                && m.Start == T0.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "fps de cs2: " + (m == null ? "nada" : m.Median + " en " + m.Seconds + " s"));
            Is(r.Finish(T0, 0).Cap == 0 && r.Finish(T0, null).Cap == 300, "límite de cs2: fps_max, o adivinado si no se leyó");
            Is(Fake(79, 300).Finish(T0, 0) == null && Fake(80, 300).Finish(T0, 0).Seconds == 60, "menos de 60 s no es partida");
            // with maps: one match per map, the menu out; a map under a minute does not count
            List<MapSpan> maps = new List<MapSpan> { new MapSpan { Map = "Dust II", From = T0, To = T0.AddSeconds(85) }, new MapSpan { Map = "Mirage", From = T0.AddSeconds(85), To = T0.AddSeconds(120) } };
            List<Match> ms = r.Matches(T0, maps, 0);
            Is(ms.Count == 1 && ms[0].Map == "Dust II" && ms[0].Seconds == 60 && r.Matches(T0, new List<MapSpan>(), 0).Count == 0, "partidas por mapa");
            Is(r.Matches(T0, null, 0).Single().Seconds == 95, "sin mapas, una sola partida");

            // the history: CS2 in its own file, Fortnite and Valorant in theirs
            string fd = Path.Combine(dir, "fps-cs2"); Directory.CreateDirectory(fd);
            Is(FpsLog.Update(fd, Game.Cs2).Count == 0, "cs2 sin historial");
            Match a = new Match { Start = "2026-11-24 18:00:00", Map = "Dust II", Seconds = 95, Median = 300, Low = 280, Max = 320 }, b = new Match { Start = "2026-11-24 19:00:00", Map = "Partida", Seconds = 70, Median = 250, Low = 200, Max = 300 };
            FpsLog.Add(fd, Game.Cs2, a); FpsLog.Add(fd, Game.Cs2, a);
            List<Match> h = FpsLog.Add(fd, Game.Cs2, b);
            Is(h.Count == 2 && h[0].Start == b.Start && File.Exists(Path.Combine(fd, "fps-cs2.json")) && !File.Exists(Path.Combine(fd, "fps.json")) && !File.Exists(Path.Combine(fd, "fps-valorant.json")), "add escribe fps-cs2.json");
            List<Match> back = FpsLog.Update(fd, Game.Cs2);
            Is(back.Count == 2 && back[1].Map == "Dust II" && back[1].Median == 300, "update de cs2 devuelve lo guardado");
            FpsLog.Clear(fd, Game.Fortnite); FpsLog.Clear(fd, Game.Valorant);
            Is(File.Exists(Path.Combine(fd, "fps.json")) && File.Exists(Path.Combine(fd, "fps-valorant.json")) && FpsLog.Update(fd, Game.Cs2).Count == 2, "fortnite y valorant siguen en sus archivos");
            FpsLog.Clear(fd, Game.Cs2);
            a.Start = "2020-01-01 00:00:00";
            FpsLog.Add(fd, Game.Cs2, a);
            Is(FpsLog.Update(fd, Game.Cs2).Count == 0, "lo de antes de borrar no vuelve");
        }

        static string Sample(string token, string map, string phase)
        {
            // Shape of a CS2 post with provider and map (data from the GSI docs; steamid made up).
            return "{\r\n\t\"provider\": {\r\n\t\t\"name\": \"Counter-Strike: Global Offensive\",\r\n\t\t\"appid\": 730,\r\n\t\t\"version\": 14077,\r\n\t\t\"steamid\": \"76561190000000000\",\r\n\t\t\"timestamp\": 1764007200\r\n\t},\r\n"
                + (map == null ? "" : "\t\"map\": {\r\n\t\t\"mode\": \"competitive\",\r\n\t\t\"name\": \"" + map + "\",\r\n\t\t\"phase\": \"" + phase + "\",\r\n\t\t\"round\": 3,\r\n\t\t\"team_ct\": { \"score\": 2, \"timeouts_remaining\": 1 },\r\n\t\t\"team_t\": { \"score\": 1 }\r\n\t},\r\n")
                + "\t\"auth\": {\r\n\t\t\"token\": \"" + token + "\"\r\n\t}\r\n}";
        }

        static void Gsi(string dir)
        {
            const string tok = "0123456789abcdef0123456789abcdef";
            Is(Cs2Gsi.Pretty("de_dust2") == "Dust II" && Cs2Gsi.Pretty("de_train") == "Train" && Cs2Gsi.Pretty("cs_office") == "Office" && Cs2Gsi.Pretty("ar_shoots") == "Shoots" && Cs2Gsi.Pretty("mapa") == "Mapa", "nombres de mapas");
            using (Cs2Gsi g = new Cs2Gsi(tok, 0))
            {
                Is(g.Spans(T0) == null, "sin GSI en la sesión: null, y la sesión es una Partida");
                Is(!g.Post(Sample("otro", "de_dust2", "live"), T0) && !g.Post("{roto", T0) && !g.Post("[1]", T0) && !g.Post("", T0) && !g.Post(Sample(tok, "de_dust2", "live").Replace("auth", "x"), T0), "token incorrecto o json roto: rechazado");
                Is(g.Spans(T0) == null, "lo rechazado no cuenta");
                Is(g.Post(Sample(tok, null, null), T0) && g.Spans(T0).Count == 0, "en el menú no hay partida");
                g.Post(Sample(tok, "de_dust2", "warmup"), T0.AddSeconds(10));
                g.Post(Sample(tok, "de_dust2", "live"), T0.AddSeconds(30));
                Is(g.Phase == "live", "fase del mapa");
                g.Post(Sample(tok, null, null), T0.AddSeconds(100));
                g.Post(Sample(tok, "de_mirage", "live"), T0.AddSeconds(110));
                g.Post(Sample("otro", null, null), T0.AddSeconds(150));
                g.Post(Sample(tok, "cs_office", "live"), T0.AddSeconds(200));
                List<MapSpan> sp = g.Spans(T0.AddSeconds(300));
                Is(sp.Count == 3 && sp[0].Map == "Dust II" && sp[0].From == T0.AddSeconds(10) && sp[0].To == T0.AddSeconds(100) && sp[1].Map == "Mirage" && sp[1].To == T0.AddSeconds(200) && sp[2].Map == "Office" && sp[2].To == T0.AddSeconds(300),
                    "partidas por mapa: " + string.Join(", ", sp.Select(x => x.Map + " " + (x.From - T0).TotalSeconds + "-" + (x.To - T0).TotalSeconds)));
            }
            // Through a real connection on 127.0.0.1: the request arrives in pieces, every request gets a 200
            using (Cs2Gsi g = new Cs2Gsi(tok, 0))
            {
                Func<string, bool, string> send = (body, split) =>
                {
                    using (TcpClient c = new TcpClient())
                    {
                        c.ReceiveTimeout = c.SendTimeout = 5000;
                        c.Connect(IPAddress.Loopback, g.LocalPort);
                        byte[] q = Encoding.UTF8.GetBytes("POST / HTTP/1.1\r\nUser-Agent: Valve/Steam HTTP Client 1.0 (730)\r\nContent-Type: application/json\r\nHost: 127.0.0.1:" + g.LocalPort + "\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body);
                        NetworkStream ns = c.GetStream();
                        int cut = split ? 40 : q.Length;
                        ns.Write(q, 0, cut);
                        if (split) { Thread.Sleep(100); ns.Write(q, cut, q.Length - cut); }
                        using (StreamReader rd = new StreamReader(ns)) return rd.ReadToEnd();
                    }
                };
                Is(send(Sample("otro", "de_nuke", "live"), false).StartsWith("HTTP/1.1 200") && g.Spans(DateTime.UtcNow) == null, "por la red: otro token se contesta y se descarta");
                Is(send(Sample(tok, "de_nuke", "live"), true).StartsWith("HTTP/1.1 200") && g.Spans(DateTime.UtcNow).Single().Map == "Nuke", "por la red: mapa leído");
            }
            // The port taken: the second listener fails and the session goes on without maps
            using (Cs2Gsi g = new Cs2Gsi(tok, 0))
            {
                bool taken = false;
                try { new Cs2Gsi(tok, g.LocalPort).Dispose(); } catch (SocketException) { taken = true; }
                Is(taken, "puerto ocupado: SocketException");
            }
        }

        // The .cfg through a fake Steam in the temporary folder: never in the real game folder.
        static void Cfg(string dir)
        {
            Func<string> realSteam = Cs2.Steam; Func<bool> realRun = Game.Cs2.Running;
            try
            {
                string steam = Path.Combine(dir, "steam-gsi"), data = Path.Combine(dir, "datos-gsi");
                Cs2.Steam = () => steam;
                bool open = false; Game.Cs2.Running = () => open;
                string exe = Game.Cs2.Exe, cfg = Path.Combine(steam, @"steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg"), f = Path.Combine(cfg, "gamestate_integration_tweakeroo.cfg");
                Is(exe.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase), "la prueba no toca la carpeta real: " + exe);
                Cs2Gsi.Ensure(data);
                Is(!Directory.Exists(steam), "sin CS2 instalado no escribe nada");
                Directory.CreateDirectory(Path.GetDirectoryName(exe)); File.WriteAllText(exe, ""); Directory.CreateDirectory(cfg);
                open = true; Cs2Gsi.Ensure(data);
                Is(!File.Exists(f), "con CS2 abierto no escribe");
                open = false; Cs2Gsi.Ensure(data);
                string text = File.ReadAllText(f), tok = Cs2Gsi.Token(data);
                Dictionary<string, object> kv = Kv.Parse(text);
                Is(System.Text.RegularExpressions.Regex.IsMatch(tok, "^[0-9a-f]{32}$") && Kv.Get(kv, "Tweakeroo", "uri") as string == "http://127.0.0.1:3121/" && Kv.Get(kv, "Tweakeroo", "auth", "token") as string == tok
                    && Kv.Get(kv, "Tweakeroo", "data", "provider") as string == "1" && Kv.Get(kv, "Tweakeroo", "data", "map") as string == "1" && Kv.Get(kv, "Tweakeroo", "heartbeat") != null, "cfg de GSI: " + text);
                File.SetLastWriteTimeUtc(f, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                Cs2Gsi.Ensure(data);
                Is(File.GetLastWriteTimeUtc(f).Year == 2020 && Cs2Gsi.Token(data) == tok, "igual: no se reescribe y el token se guarda");
                File.WriteAllText(f, "\"Tweakeroo\" { }");
                Cs2Gsi.Ensure(data);
                Is(File.ReadAllText(f) == text, "distinto: se reescribe");
                open = true; Cs2Gsi.Remove();
                Is(File.Exists(f), "al desinstalar con CS2 abierto no se borra");
                open = false; Cs2Gsi.Remove(); Cs2Gsi.Remove();
                Is(!File.Exists(f) && File.Exists(exe), "al desinstalar se borra solo el cfg");
            }
            finally { Cs2.Steam = realSteam; Game.Cs2.Running = realRun; }
        }
    }
}
