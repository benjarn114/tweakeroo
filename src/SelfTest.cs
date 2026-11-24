using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tablero
{
    // Self-test: uses a throwaway key, a temporary .ini and a temporary folder. Does not touch real settings.
    static class SelfTest
    {
        const string Root = @"HKCU\Software\Tablero_Autoprueba";

        class BadOp : RegOp
        {
            public BadOp(string name) : base(Root, name, "d:1", None) { }
            public override void Write(string key, string value) { if (value == "d:1") throw new InvalidOperationException("fallo a propósito"); base.Write(key, value); }
        }

        static void Is(bool ok, string what) { if (!ok) throw new Exception("FALLA: " + what); }
        static string V(string n) { return RegOp.ReadValue(Root, n); }
        static Tweak Mk(string id, params Op[] ops) { return new Tweak { Id = id, Ops = ops.ToList() }; }

        static string Export(string dir) { File.WriteAllText(Path.Combine(dir, "paleta.txt"), "Ciruela"); return Prefs.Export(dir); }
        public static string Run()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tablero-" + Guid.NewGuid().ToString("N"));
            string realIni = IniOp.File;
            Func<bool> realRunning = IniOp.GameRunning;
            try
            {
                // types: round trip, including "does not exist"
                foreach (string v in new[] { "d:0", "d:4294967295", "q:18446744073709551615", "s:", "s:hola; mundo", "x:%TEMP%", "m:a\nb", "m:", "b:00FF10", Op.None })
                { RegOp.WriteValue(Root, "t", v); Is(V("t") == v, "tipo " + v); }

                // existing and missing
                RegOp.WriteValue(Root, "a", "d:7");
                Tweak t1 = Mk("t1", new RegOp(Root, "a", "d:1", "d:0"), new RegOp(Root, "b", "s:x", Op.None));
                Engine e = new Engine(dir, new[] { t1 });
                Is(t1.Read() == State.Off, "estado inicial");
                Snapshot first = e.Save("uno", "original");
                e.Enable(t1);
                Is(t1.Read() == State.On && V("a") == "d:1" && V("b") == "s:x", "prender");
                Is(new Engine(dir, new[] { t1 }).Mine(t1), "los originales sobreviven a un reinicio de la app");
                e.Disable(t1, false);
                Is(V("a") == "d:7" && V("b") == Op.None, "apagar devuelve lo exacto");

                // partial
                RegOp.WriteValue(Root, "a", "d:1");
                Is(t1.Read() == State.Partial, "parcial");
                e.Enable(t1);
                e.Disable(t1, true);
                Is(V("a") == "d:1" && V("b") == Op.None, "apagar todo respeta lo que ya estaba");
                e.Disable(t1, false);
                Is(V("a") == "d:0", "apagar suelto usa el valor de Windows");

                // restore
                e.Enable(t1);
                int n;
                Is(e.Restore(first, out n).Count == 0 && n == 2 && V("a") == "d:7" && V("b") == Op.None, "restaurar guardado");
                Is(e.DiffCount(first, e.ReadAll()) == 0, "sin diferencias tras restaurar");
                Is(e.List().Single().Name == "uno", "lista de guardados");
                bool refused = false;
                try { e.Delete(first); } catch (InvalidOperationException) { refused = true; }
                Is(refused, "el original no se borra");
                for (int i = 0; i < 12; i++) { e.Save("auto", "auto"); System.Threading.Thread.Sleep(5); }
                Is(e.List().Count(x => x.Kind == "auto") == 10 && e.List().Any(x => x.Kind == "original"), "limite de automaticos");
                File.WriteAllText(Path.Combine(dir, "guardados", "roto.json"), "{no es json");
                Is(e.List().Count == 11, "un guardado roto no tumba la lista");

                // failure halfway
                Tweak bad = Mk("bad", new RegOp(Root, "a", "d:1", "d:0"), new BadOp("c"));
                Engine e2 = new Engine(dir + "2", new[] { bad });
                bool threw = false;
                try { e2.Enable(bad); } catch (InvalidOperationException) { threw = true; }
                Is(threw && V("a") == "d:7" && V("c") == Op.None && !e2.Mine(bad), "fallo a medias se deshace");

                // shared token
                RegOp.WriteValue(Root, "tok", "s:AutoHDREnable=0;SwapEffectUpgradeEnable=1;");
                TokenOp tk = new TokenOp(Root, "tok", "VRROptimizeEnable", "s:1", "s:0");
                tk.Write("", "s:1");
                Is(V("tok") == "s:AutoHDREnable=0;SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;", "token agrega");
                tk.Write("", Op.None);
                Is(V("tok") == "s:AutoHDREnable=0;SwapEffectUpgradeEnable=1;", "token quita");

                // ini: keeps BOM, order and the rest
                IniOp.File = Path.Combine(dir, "g.ini");
                byte[] before = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("[A]\r\nx=1\r\nñ=ü\r\n\r\n[B]\r\nx=9\r\n")).ToArray();
                File.WriteAllBytes(IniOp.File, before);
                IniOp ix = new IniOp("B", "x", "5", "9"), iy = new IniOp("A", "nueva", "1", "0");
                IniOp.GameRunning = () => false;
                {
                    ix.Write("", "s:5"); iy.Write("", "s:1");
                    Is(File.ReadAllText(IniOp.File) == "[A]\r\nx=1\r\nñ=ü\r\nnueva=1\r\n\r\n[B]\r\nx=5\r\n", "ini escribe en su seccion");
                    ix.Write("", "s:9"); iy.Write("", Op.None);
                    Is(File.ReadAllBytes(IniOp.File).SequenceEqual(before), "ini vuelve byte a byte");
                }
                IniOp.GameRunning = () => true;
                bool blocked = false;
                try { ix.Write("", "s:5"); } catch (InvalidOperationException) { blocked = true; }
                Is(blocked, "con el juego abierto no se toca el ini");
                IniOp.File = realIni;
                IniOp.GameRunning = realRunning;

                // single bit
                RegOp.WriteValue(Root, "bit", "s:510");
                BitOp bo = new BitOp(Root, "bit", 4);
                bo.Write("", "d:0"); Is(V("bit") == "s:506" && bo.Read("") == "d:0", "bit apaga");
                bo.Write("", "d:1"); Is(V("bit") == "s:510", "bit vuelve");

                // a plan that no longer exists does not block
                SchemeOp so = new SchemeOp(Power.Active().ToString(), Power.Active().ToString());
                Is(so.Took("", "s:" + Guid.NewGuid()), "plan borrado cae al de respaldo");

                // palettes: readable text and visible controls
                foreach (Palette p in Theme.All)
                    foreach (string x in "Ink/Bg Ink/Rail Ink/Card Ink/CardHover Ink2/Bg Ink2/Rail Ink2/Card Ink2/CardHover CtaInk/Cta BtnInk/Btn BtnInk/BtnHover Danger/Card Danger/CardHover OnInk/On On/Card Cta/Bg On/Off Btn/Rail Btn/Card Line/Bg Line/Rail Line/Card".Split(new[] { (char)32 }))
                    {
                        string[] q = x.Split(new[] { (char)47 });
                        double need = x == "On/Off" ? 1.5 : q[0] == "Btn" ? 1.25 : q[0] == "Line" ? 2.4 : q[0] == "On" || x == "Cta/Bg" ? 3 : 4.5;
                        Is(Theme.Contrast(p, q[0], q[1]) >= need, "contraste " + p.Name + " " + x + " = " + Theme.Contrast(p, q[0], q[1]).ToString("0.00"));
                    }

                // themes: text over each background, their palette and music, the same scene for the same seed, and the cost per frame
                foreach (Look lk in Theme.Looks.Where(x => !x.Simple))
                {
                    Scene sc = Scene.Make(lk.Id), twin = Scene.Make(lk.Id);
                    sc.Skip(300); twin.Skip(300);
                    Is(sc.Fb.SequenceEqual(twin.Fb), "misma semilla, mismo fondo: " + lk.Id);
                    Is(Theme.All.Contains(lk.Pal) && Sound.Themes.Contains(Theme.MusicOf(lk, "")), "paleta y música de " + lk.Id);
                    Look l0 = lk;
                    Func<string, string, double, double> over = (ink, under, share) => Theme.OverScene(l0.Pal, ink, under, under == "Bg" ? Theme.BgOver : Theme.RailOver, sc.Fb, share);
                    // Side panels: over all of the background. Page: 90 % of it for normal text, 98 % for titles (large text, 3:1).
                    foreach (string ink in new[] { "Ink", "Ink2" })
                        Is(over(ink, "Rail", 0) >= 4.5 && over(ink, "Bg", 0.1) >= 4.5, "contraste " + lk.Id + " " + ink + ": menú " + over(ink, "Rail", 0).ToString("0.00") + ", página " + over(ink, "Bg", 0.1).ToString("0.00"));
                    Is(over("Ink", "Bg", 0.02) >= 3, "títulos sobre " + lk.Id + " = " + over("Ink", "Bg", 0.02).ToString("0.00"));
                }
                Is(Theme.MusicOf(Theme.LookOf("simple"), "arcade") == "arcade" && Theme.MusicOf(Theme.LookOf("lluvia"), "") == "lluvia" && Theme.LookOf("rara").Simple, "la música va con la temática; Simple no la cambia");
                string sceneCost = string.Join(", ", Scene.Ids.Select(id =>
                {
                    System.Diagnostics.Stopwatch made = System.Diagnostics.Stopwatch.StartNew();
                    Scene.Make(id).Skip(0);
                    made.Stop();
                    double took = Scene.Cost(id, 300);
                    Is(took < 4, "el fondo " + id + " tarda " + took.ToString("0.00") + " ms por fotograma");
                    return id + " " + took.ToString("0.00") + " ms (crearlo, " + made.ElapsedMilliseconds + " ms)";
                }));

                // advisor
                List<Tweak> cat = Catalog.Build();
                Func<string, Hw, bool> yes = (id, h) => Advisor.For(cat.First(x => x.Id == id), h).Yes;
                Hw desk = new Hw { Cpu = "AMD Ryzen 5 9600X", Cores = 6, RamGb = 32, GpuCount = 1, VramGb = 12, Nvidia = true, Nvme = true, Ssd = true, DiskFreeGb = 200, Wifi = true };
                Hw cable = new Hw { Cpu = "AMD Ryzen 9 7950X3D", Cores = 16, RamGb = 32, GpuCount = 2, Ethernet = true, Ssd = true, DiskFreeGb = 20 };
                Hw lap = new Hw { Cpu = "Intel Core i3", Cores = 2, RamGb = 8, GpuCount = 1, VramGb = 2, Laptop = true, Wifi = true };
                Is(yes("pw-wifi", desk) && !yes("net-eee", desk), "wifi: sin ajustes de cable");
                Is(yes("net-eee", cable) && !yes("pw-wifi", cable), "cable: sin ajustes de wifi");
                Is(yes("pw-plan", desk) && !yes("pw-plan", cable) && !yes("pw-plan", lap), "plan de energia segun CPU y portatil");
                Is(yes("nv-shader", desk) && !yes("nv-shader", cable), "cache segun espacio");
                Is(yes("ui-transp", lap) && !yes("ui-transp", desk), "apariencia solo en PC justo");
                Is(yes("fn-texture", lap) && !yes("fn-texture", desk), "texturas segun memoria de video");
                foreach (Hw h in new[] { desk, cable, lap }) Is(!yes("pw-hvci", h), "el riesgo nunca se recomienda");

                // stretched: only narrower than the display and with its best refresh rate
                Res nat = new Res { W = 1920, H = 1080, Hz = 144 };
                List<Res> st = Display.Stretched(new[] { nat, new Res { W = 1440, H = 1080, Hz = 60 }, new Res { W = 1440, H = 1080, Hz = 144 }, new Res { W = 1600, H = 900, Hz = 144 }, new Res { W = 1280, H = 960, Hz = 60 }, new Res { W = 800, H = 600, Hz = 60 }, new Res { W = 2560, H = 1600, Hz = 60 } }, nat);
                Is(st.Count == 2 && st[0].Name == "1440 × 1080" && st[0].Hz == 144 && st[1].W == 1280, "resoluciones estiradas");

                // FPS from the log: steady 240 with a dip, lobby and gaps left out
                StringBuilder log = new StringBuilder("LogInit: sin hora\r\n");
                DateTime t0 = new DateTime(2026, 11, 22, 3, 0, 0, DateTimeKind.Utc);
                long fr = 0;
                Action<double, string> line = (sec, x) => log.Append("[" + t0.AddSeconds(sec).ToString("yyyy.MM.dd-HH.mm.ss:fff", System.Globalization.CultureInfo.InvariantCulture) + "][" + (fr % 1000).ToString().PadLeft(3) + "]" + x + "\r\n");
                line(0, "LogWorld: Bringing World /Game/Maps/Frontend.Frontend up for play (max tick rate 60)");
                for (int i = 1; i < 400; i++) { fr += 15; line(i * 0.25, "LogX: lobby"); }
                line(100, "LogWorld: Bringing World /VKPlay/Maps/VKPlay_EmptyOcean.X up for play (max tick rate 240)");
                for (int i = 1; i < 600; i++) { fr += i >= 200 && i < 220 ? 25 : 60; line(100 + i * 0.25, "LogX: juego"); }
                fr += 5000; line(300, "LogX: tras un hueco");
                line(301, "LogLoad: LoadMap: /Game/Maps/Frontend?closed");
                List<Match> ms = FpsLog.Read(new StringReader(log.ToString()));
                Is(ms.Count == 1 && ms[0].Map == "Creativo" && ms[0].Median == 240 && ms[0].Cap == 240 && ms[0].Low >= 100 && ms[0].Low <= 240 && ms[0].Max == 240 && ms[0].Seconds > 100, "fps del registro");
                Func<string, int, Match> mk = (s, f) => new Match { Start = s, Map = "Creativo", Median = f, Cap = 0 };
                Is(FpsLog.Compare(new List<Match> { mk("2026-01-03", 300), mk("2026-01-04", 320), mk("2026-01-01", 200), mk("2026-01-02", 220) }, "2026-01-02 12:00").Contains("210 FPS en 2 partidas. Después: 310"), "comparacion antes y despues");
                Is(FpsLog.Compare(new List<Match> { mk("2026-01-03", 300) }, "2026-01-02").StartsWith("La comparación"), "sin datos no compara");

                // estimate and review
                Hw my = new Hw { Cpu = "AMD Ryzen 5 9600X", Gpu = "NVIDIA GeForce RTX 3060", RamGb = 32, RamMhz = 6400, Ddr = 5, RamModules = 2, Hz = 200, MaxHz = 200, ScreenW = 2560, ScreenH = 1440, Ssd = true, DiskName = "x", DiskFreeGb = 200, Wifi = true, Nvidia = true, Socket = "AM5" };
                Estimate em = Estimator.For(my, 2560, 1440);
                Is(em.Low > 150 && em.High < 450 && em.Low < em.High && em.GpuLimits, "estimacion razonable: " + em.Low + "-" + em.High);
                Is(Estimator.For(new Hw { Cpu = "Intel Core i5-12400F", Gpu = "AMD Radeon RX 6600 XT" }, 1920, 1080) != null && Estimator.For(new Hw { Cpu = "?", Gpu = "?" }, 1920, 1080) == null, "estimacion sin inventar");
                Is(Estimator.For(new Hw { Cpu = "Intel Core i5-12400F", Gpu = "NVIDIA GeForce RTX 4070 Ti" }, 3840, 2160).Low > Estimator.For(new Hw { Cpu = "Intel Core i5-12400F", Gpu = "NVIDIA GeForce RTX 4060" }, 3840, 2160).Low, "las Ti no se confunden");
                List<Check> rv = Review.For(my, new List<Match> { new Match { Median = 238, Cap = 240 } });
                Is(rv.Any(c => c.Title.StartsWith("Ninguna compra necesaria")) && rv.All(c => c.Kind != Kind.Comprar), "con FPS de sobra no manda a comprar");
                my.Hz = 60; Is(Review.For(my, new List<Match>())[0].Title.StartsWith("La pantalla usa 60 Hz"), "hz por debajo del maximo");
                Hw poor = new Hw { Cpu = "Intel Core i5-7400", Gpu = "NVIDIA GeForce GTX 1050", RamGb = 8, RamMhz = 2400, Ddr = 4, RamModules = 1, Hz = 144, MaxHz = 144, ScreenW = 1920, ScreenH = 1080, DiskName = "x", DiskFreeGb = 10, Ethernet = true };
                List<Check> pv = Review.For(poor, new List<Match>());
                Is(pv.Count(c => c.Kind == Kind.Comprar) == 4 && pv.Any(c => c.Title == "Cuello de botella: la gráfica" || c.Title == "Cuello de botella: el procesador"), "PC justo recibe consejos: " + string.Join("; ", pv.Select(c => c.Title)));
                Is(Review.For(new Hw(), new List<Match>()).All(c => c.Kind != Kind.Comprar), "sin datos no aconseja comprar");
                Is(Review.Real(new List<Match> { new Match { Median = 60, Cap = 60 } }, 144) == 0, "un limite bajo no cuenta como FPS reales");

                // Fortnite keys: keeps the player's set, not the default one
                {
                    MemoryStream mem = new MemoryStream();
                    Action<string> fs = x => { mem.Write(BitConverter.GetBytes(x.Length + 1), 0, 4); byte[] by = Encoding.ASCII.GetBytes(x + "\0"); mem.Write(by, 0, by.Length); };
                    Action<string, string, string> act = (nm, k1, k2) => { fs("ActionName"); fs("NameProperty"); fs(nm); fs("KeyBind1"); fs("KeyName"); fs("NameProperty"); fs(k1); fs("KeyBind2"); fs("KeyName"); fs("NameProperty"); fs(k2); };
                    Action<string, string> set = (e1, r1) => { fs("UserActionBindings"); fs("ArrayProperty"); for (int i = 0; i < 120; i++) act("Accion" + i, "None", "None"); act("PerformBuildingEditInteraction", e1, "MouseScrollDown"); act("EditSelect", "LeftMouseButton", "None"); act("BuildingEditReset", r1, "None"); act("Reload", "R", "None"); };
                    fs("bEditConfirmOnReleaseEnabled"); fs("BoolProperty"); mem.Write(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0x10 }, 0, 9);
                    set("G", "RightMouseButton"); set("E", "MouseScrollDown"); fs("CustomGamepadActionBindings"); fs("ArrayProperty"); act("PerformBuildingEditInteraction", "Gamepad_X", "None"); fs("UserActionBindings"); fs("ArrayProperty"); act("PerformBuildingEditInteraction", "Q", "None"); fs("CustomGamepadActionBindings"); fs("ArrayProperty"); for (int i = 0; i < 120; i++) act("Mando" + i, "Gamepad_Y", "None");
                    Binds bd = Binds.Parse(mem.ToArray());
                    Is(bd != null && bd.Edit == 'E' && bd.Select == 1 && bd.Reset == Macros.WheelDown && bd.HasConfirm && bd.Confirm, "teclas leidas");
                    Is(bd.Conflict('R') == "Reload" && bd.Conflict('P') == null, "tecla ocupada");
                    MemoryStream z = new MemoryStream(); z.Write(Encoding.ASCII.GetBytes("ECFD"), 0, 4); z.Write(new byte[] { 1, 2, 3, 0x78, 0x9c }, 0, 5);
                    using (System.IO.Compression.DeflateStream df = new System.IO.Compression.DeflateStream(z, System.IO.Compression.CompressionMode.Compress, true)) df.Write(mem.ToArray(), 0, (int)mem.Length);
                    byte[] back = Binds.Inflate(z.ToArray()); Is(back != null && Binds.Parse(back).Edit == 'E', "archivo comprimido");
                    Is(Binds.Inflate(new byte[200]) == null && Binds.Inflate(new byte[0]) == null && Binds.Parse(new byte[5000]) == null, "archivo raro");
                }
                // network: math and where the problem is
                {
                    PingStats ps = PingStats.Of(new[] { 20, 22, 20, -1, 80, 20 });
                    Is(ps.Sent == 6 && ps.Lost == 1 && ps.Median == 20 && ps.Max == 80 && ps.Spikes == 1 && Math.Abs(ps.Jitter - 124.0 / 4) < 0.01, "cuentas de ping: " + ps.Jitter);
                    Is(PingStats.Of(new[] { -1, -1 }).Silent && PingStats.Of(new int[0]).Loss == 0, "ping sin respuesta");
                    Func<int, int, PingStats> good = (many, v) => PingStats.Of(Enumerable.Repeat(v, many).ToList());
                    PingStats ok = good(50, 5), far = good(50, 140), off = good(50, -1), lossy = PingStats.Of(Enumerable.Range(0, 50).Select(i => i % 10 == 0 ? -1 : 20).ToList());
                    Func<PingStats, PingStats, PingStats, bool, string> vd = (x, y, z1, wf) => NetTest.Verdict(new NetResult { Router = x, Internet = y, Epic = z1, Wifi = wf });
                    Is(vd(lossy, lossy, lossy, true).StartsWith("El problema empieza en el Wi-Fi") && vd(lossy, lossy, lossy, false).StartsWith("El problema empieza en el cable"), "fallo en casa");
                    Is(vd(ok, lossy, lossy, true).Contains("proveedor de internet") && vd(off, lossy, lossy, true).StartsWith("El router no contesta"), "fallo del proveedor");
                    Is(vd(ok, ok, lossy, true).Contains("ruta hasta Epic") && vd(ok, ok, far, true).Contains("140 ms") && vd(ok, ok, ok, true) == "Conexión estable.", "ruta, distancia y bien");
                    Is(vd(ok, off, off, true).StartsWith("Sin respuesta de internet") && vd(ok, ok, off, true).StartsWith("Los servidores de Epic no contestan"), "sin respuesta");
                    Func<string, int, System.Threading.Tasks.Task<int>> png = NetTest.Ping;
                    try
                    {
                        NetTest.Ping = (h, ms2) => System.Threading.Tasks.Task.FromResult(h.Contains("-eu.") ? 30 : h.Contains("-br.") ? 90 : -1);
                        Is(System.Threading.Tasks.Task.Run(() => NetTest.Nearest()).Result == "eu", "region mas cercana");
                        NetTest.Ping = (h, ms2) => System.Threading.Tasks.Task.FromResult(-1); Is(System.Threading.Tasks.Task.Run(() => NetTest.Nearest()).Result == null, "ninguna region contesta");
                    }
                    finally { NetTest.Ping = png; }
                    Is(Wifi.Advice(true, 50, 6, new List<string> { "steam" }).Count(c => c.Kind == Kind.Gratis) == 4 && Wifi.Advice(false, 0, 0, new List<string>()).All(c => c.Kind == Kind.Bien), "consejos de red");
                    NetResult nr = new NetResult { Date = "2026-11-22 20:00:00", Router = ok, Internet = ok, Epic = far, Region = "br" }; NetTest.Save(dir, nr); Is(NetTest.Load(dir).Epic.Median == 140, "medicion guardada"); File.WriteAllText(Path.Combine(dir, "red.json"), "{x"); Is(NetTest.Load(dir) == null, "medicion ilegible");
                }

                // settings: odd values go back to default, and export and import let nothing slip through
                Prefs wasP = Prefs.Now; int wasL = Fx.Level; bool lang = L.En;
                try
                {
                    Prefs pr = Prefs.Parse("{\"Page\":\"Otra\",\"Confirm\":999,\"Motion\":7,\"Tray\":true}");
                    Is(pr.Confirm == 15 && pr.Motion == 0 && pr.Tray && !pr.AutoCheck, "ajustes raros");
                    // Any whole second from 5 to 30; a file from before still has "Page", which is ignored.
                    Is(new[] { 5, 7, 23, 30 }.All(q => Prefs.Parse("{\"Page\":\"Macros\",\"Confirm\":" + q + ",\"Tray\":true}").Confirm == q) && new[] { 4, 31, -1, 0 }.All(q => Prefs.Parse("{\"Confirm\":" + q + "}").Confirm == 15) && Prefs.Parse("{\"Page\":\"Macros\",\"Tray\":true}").Tray, "tiempo de confirmar de 5 a 30");
                    Is(Prefs.Parse("{no").AutoCheck && Prefs.Parse("null").Confirm == 15, "ajustes ilegibles");
                    Prefs snd = Prefs.Parse("{\"SoundSaved\":true,\"Music\":\"rock\",\"MusicMode\":5,\"MusicVolume\":300,\"Sfx\":false,\"SfxVolume\":-4}");
                    Is(snd.Music == "noche" && snd.MusicMode == 0 && snd.MusicVolume == 100 && !snd.Sfx && snd.SfxVolume == 0, "sonido saneado");
                    Is(pr.Music == "noche" && pr.MusicMode == 0 && pr.MusicVolume == 30 && pr.Sfx && pr.SfxVolume == 60, "ajustes de antes reciben el sonido por defecto");
                    Prefs mute = Prefs.Default(); mute.Music = ""; mute.Sfx = false; mute.MusicVolume = 0; mute.MusicMode = 2;
                    Prefs muted = Prefs.Parse(mute.Json()); Is(muted.Music == "" && !muted.Sfx && muted.MusicVolume == 0 && muted.MusicMode == 2, "sonido apagado se guarda");
                    Is(Prefs.Default().Theme == "noche" && Prefs.Parse("{}").Theme == "simple" && Prefs.Parse("{\"Theme\":\"rock\"}").Theme == "simple" && Prefs.Parse("{\"Theme\":\"lluvia\"}").Theme == "lluvia", "temática: Noche al instalar, Simple con ajustes de antes o raros");
                    string tn = Path.Combine(dir, "tema-nuevo"), tu = Path.Combine(dir, "tema-actualiza"); Directory.CreateDirectory(Path.Combine(tu, "guardados"));
                    Prefs.Load(tn); bool themeNew = Prefs.Now.Theme == "noche"; Prefs.Load(tu); bool themeUpd = Prefs.Now.Theme == "simple";
                    Prefs.Now.Theme = "arcade"; Prefs.Save(tu); Prefs.Now = Prefs.Default(); Prefs.Load(tu);
                    Is(themeNew && themeUpd && Prefs.Now.Theme == "arcade", "temática guardada y leída; quien actualiza sin ajustes guardados sigue en Simple");
                    string a1 = Path.Combine(dir, "aj1"), a2 = Path.Combine(dir, "aj2"); Directory.CreateDirectory(a1); Directory.CreateDirectory(a2);
                    Prefs.Now = Prefs.Default(); Prefs.Now.Confirm = 23; Prefs.Save(a1);
                    Prefs.Now = Prefs.Default(); Prefs.Load(a1); Is(Prefs.Now.Confirm == 23 && !File.ReadAllText(Path.Combine(a1, "ajustes.json")).Contains("Page"), "guardar ajustes");
                    MacroConfig mm = Macros.Default(); mm.Accepted = true; mm.Keys["doble"] = 5; mm.On.Add("doble"); Macros.Save(a1, mm);
                    string ex = Export(a1);
                    Prefs.Now = Prefs.Default(); Prefs.Import(a2, ex);
                    MacroConfig im = Macros.Load(a2);
                    Is(Prefs.Now.Confirm == 23 && im.Keys["doble"] == 5 && !im.Accepted && File.ReadAllText(Path.Combine(a2, "paleta.txt")) == "Ciruela", "importar ajustes sin aceptar el riesgo");
                    foreach (string mal in new[] { "{}", "x", "null", "{\"App\":\"Otra\",\"Prefs\":{}}" })
                    {
                        bool no = false; try { Prefs.Import(a2, mal); } catch (InvalidDataException) { no = true; }
                        Is(no, "no importa: " + mal);
                    }
                    Prefs.Import(a2, "{\"App\":\"Tweakeroo\",\"Prefs\":{},\"Palette\":\"..\\\\x\"}"); Is(File.ReadAllText(Path.Combine(a2, "paleta.txt")) == "Ciruela" && Macros.Load(a2).On.Count == 0, "importar sin macros ni paleta valida");
                }
                finally { Prefs.Now = wasP; Fx.Level = wasL; L.En = lang; }
                // version history: the installed one appears once and everything is in both languages
                List<VersionRow> vr = VersionRow.All();
                Is(vr.Count(v => v.Current) == 1 && vr.All(v => v.Missing == 0 && v.Notes.Count > 0 && v.Date.Length == 10) && vr.Select(v => Version.Parse(v.Version)).SequenceEqual(vr.Select(v => Version.Parse(v.Version)).OrderByDescending(v => v)), "historial de versiones");
                // clearing the FPS does not let them come back from the logs
                string fl = FpsLog.Folder, fd = Path.Combine(dir, "fps-borrar");
                try
                {
                    FpsLog.Folder = Path.Combine(fd, "logs"); Directory.CreateDirectory(FpsLog.Folder);
                    StringBuilder logText = new StringBuilder();
                    DateTime begin = DateTime.UtcNow.AddHours(-2);
                    Func<DateTime, int, string> stampL = (at, num) => "[" + at.ToString("yyyy.MM.dd-HH.mm.ss", System.Globalization.CultureInfo.InvariantCulture) + ":" + at.Millisecond.ToString("000") + "][" + (num % 1000).ToString().PadLeft(3) + "]";
                    logText.Append(stampL(begin, 0) + "LogWorld: Bringing World /VKPlay/M.M up for play\n");
                    for (int i = 1; i < 260; i++) logText.Append(stampL(begin.AddSeconds(i * 0.5), i * 50) + "LogX: y\n");
                    File.WriteAllText(Path.Combine(FpsLog.Folder, "FortniteGame.log"), logText.ToString());
                    Is(FpsLog.Update(fd, Game.Fortnite).Count == 1, "partida leida antes de borrar");
                    FpsLog.Clear(fd, Game.Fortnite);
                    Is(FpsLog.Update(fd, Game.Fortnite).Count == 0 && FpsLog.Update(fd, Game.Fortnite).Count == 0, "lo borrado no vuelve");
                }
                finally { FpsLog.Folder = fl; }

                // valorant: maps, FPS and a session that recovers
                const string vp = "[2026.11.22-01.14.10:324][793]LogMapLoadModel: ", vm = "Update: [Map Name: RangeV2 | Changed: FALSE] [Local World: {0} | Changed: TRUE] [Match Setup: FALSE | Changed: FALSE] [Map Ready: {1} | Changed: FALSE]";
                Is(MapWatch.Loaded(vp + string.Format(vm, "TRUE", "TRUE")) == true && MapWatch.Loaded(vp + string.Format(vm, "TRUE", "FALSE")) == false && MapWatch.Loaded(vp + "Display: UpdateMapReady: [x]") == null && MapWatch.Loaded("corta") == null, "mapa cargado");
                {
                    // the log of 2026-11-23 (account data removed): what was already there is not read again, the new log after the game restarts it is, and a locked log ends nothing
                    string vs0 = Valorant.Saved;
                    try
                    {
                        Valorant.Saved = Path.Combine(dir, "vsaved"); Directory.CreateDirectory(Valorant.Logs);
                        string vlog = Path.Combine(Valorant.Logs, "ShooterGame.log");
                        const string url = " [Map Complete: FALSE | Changed: FALSE] [URL: 0.0.0.0:7310/Game/Maps/{0}/{0}?Name=Jugador]\r\n";
                        string oldLine = "[2026.11.23-03.49.48:862][752]LogMapLoadModel: Update: [Map Name: Infinity | Changed: FALSE] [Local World: TRUE | Changed: FALSE] [Match Setup: TRUE | Changed: FALSE] [Map Ready: TRUE | Changed: TRUE]" + string.Format(url, "Infinity");
                        File.WriteAllText(vlog, string.Concat(Enumerable.Repeat("[2026.11.23-03.39.00:000][  0]LogX: partida anterior\r\n", 2000)) + oldLine);
                        MapWatch vw = new MapWatch(new DateTime(2026, 11, 23, 3, 39, 12, DateTimeKind.Utc));
                        Is(!vw.Read() && vw.Error == null, "lo que ya estaba en el registro no cuenta");
                        File.WriteAllText(vlog, "Log file open, 11/23/26 00:39:55\r\n"
                            + "[2026.11.23-03.40.13:265][272]LogMapLoadModel: Update: [Map Name: MainMenuV2 | Changed: FALSE] [Local World: FALSE | Changed: FALSE] [Match Setup: FALSE | Changed: FALSE] [Map Ready: TRUE | Changed: TRUE] [Map Complete: FALSE | Changed: FALSE] [URL: /Game/Maps/Menu/MainMenuV2?Name=Jugador]\r\n"
                            + "[2026.11.23-03.41.09:088][796]LogMapLoadModel: Update: [Map Name: Duality | Changed: TRUE] [Local World: FALSE | Changed: FALSE] [Match Setup: FALSE | Changed: FALSE] [Map Ready: FALSE | Changed: TRUE] [Map Complete: FALSE | Changed: FALSE] [URL: :7777/Engine/Maps/Entry]\r\n"
                            + "[2026.11.23-03.41.09:723][798]LogShooterGameUserSettings: Resolution changed to: [2091 x 1440]\r\n"
                            + "[2026.11.23-03.41.10:066][798]LogMapLoadModel: Display: UpdateMapReady: [PlayerCharacterLoaded: TRUE] [MatchLoadInCompleted: TRUE] [PlayerCameraReady: FALSE]\r\n"
                            + "[2026.11.23-03.41.10:066][798]LogMapLoadModel: Update: [Map Name: Duality | Changed: FALSE] [Local World: TRUE | Changed: TRUE] [Match Setup: TRUE | Changed: TRUE] [Map Ready: FALSE | Changed: FALSE]" + string.Format(url, "Duality"), new UTF8Encoding(true));
                        Is(!vw.Read() && vw.Error == null, "registro nuevo: mundo local sin mapa listo");
                        string ready = "[2026.11.23-03.41.10:144][799]LogMapLoadModel: Update: [Map Name: Duality | Changed: FALSE] [Local World: TRUE | Changed: FALSE] [Match Setup: TRUE | Changed: FALSE] [Map Ready: TRUE | Changed: TRUE]" + string.Format(url, "Duality");
                        File.AppendAllText(vlog, ready.Substring(0, 90)); bool half = vw.Read();
                        File.AppendAllText(vlog, ready.Substring(90)); bool full = vw.Read();
                        Is(!half && full, "Duality listo estira, aunque la línea llegue a medias");
                        File.AppendAllText(vlog, "[2026.11.23-03.46.37:810][388]LogMapLoadModel: Update: [Map Name: MainMenuV2 | Changed: TRUE] [Local World: FALSE | Changed: TRUE] [Match Setup: FALSE | Changed: TRUE] [Map Ready: FALSE | Changed: TRUE] [Map Complete: FALSE | Changed: TRUE] [URL: /Game/Maps/Menu/MainMenuV2?Name=Jugador]\r\n");
                        Is(!vw.Read(), "vuelta al menú");
                        bool lockedRead;
                        using (new FileStream(vlog, FileMode.Open, FileAccess.Read, FileShare.None)) lockedRead = vw.Read();
                        string lockErr = vw.Error;
                        File.AppendAllText(vlog, ready);
                        Is(!lockedRead && lockErr != null && vw.Read() && vw.Error == null, "registro bloqueado: se reintenta sin cortar nada (" + lockErr + ")");
                    }
                    finally { Valorant.Saved = vs0; }
                }
                {
                    StringBuilder vl = new StringBuilder();
                    DateTime v0 = new DateTime(2026, 11, 22, 1, 0, 0, DateTimeKind.Utc);
                    Action<double, int, string> put = (s, f, x) => vl.Append("[" + v0.AddSeconds(s).ToString("yyyy.MM.dd-HH.mm.ss", System.Globalization.CultureInfo.InvariantCulture) + ":" + v0.AddSeconds(s).Millisecond.ToString("000") + "][" + (f % 1000).ToString().PadLeft(3) + "]" + x + "\n");
                    Func<string, string, string> up = (m, o) => "LogMapLoadModel: Update: [Map Name: " + m + " | Changed: TRUE] [Local World: " + o + " | Changed: TRUE]";
                    put(0, 0, up("MainMenuV2", "FALSE")); for (int i = 1; i < 200; i++) put(i * 0.25, i * 15, "LogX: menu");
                    put(60, 0, up("Triad", "FALSE")); put(61, 0, up("Triad", "TRUE")); for (int i = 1; i < 500; i++) put(61 + i * 0.25, i * 100, i == 40 ? up("Triad", "TRUE") : "LogX: juego");
                    put(190, 0, up("MainMenuV2", "FALSE")); for (int i = 1; i < 400; i++) put(190 + i * 0.25, i * 15, "LogX: menu");
                    List<Match> vs = FpsLog.Read(new StringReader(vl.ToString()), true);
                    Is(vs.Count == 1 && vs[0].Map == "Haven" && vs[0].Median == 400 && vs[0].Seconds > 90, "fps de valorant: " + string.Join(" ", vs.Select(m => m.Map + m.Median)));
                    Is(FpsLog.Read(new StringReader(vl.ToString())).Count == 0, "un registro de valorant no vale como Fortnite");
                }
                Is(Valorant.Checks(null, 2).Count == 3 && Valorant.Checks(new Dictionary<string, string> { { "FullscreenMode", "1" }, { "bShouldLetterbox", "False" } }, 0).Count(c => c.Ok) == 3 && Valorant.Checks(new Dictionary<string, string> { { "FullscreenMode", "0" }, { "bShouldLetterbox", "True" } }, 0).Count(c => c.Ok) == 1, "ajustes de valorant");
                Is(!Valorant.Checks(null, 2).Any(c => c.HasUrl) && Valorant.Checks(new Dictionary<string, string> { { "FullscreenMode", "0" }, { "bShouldLetterbox", "True" } }, 0).Count(c => c.Fix != null && c.Act == "Arreglar") == 2, "arreglar solo con ajustes leidos");
                {
                    Func<bool> gRun = Game.Valorant.Running; Func<int> nvGet = NvScale.Get; Action<int> nvSet = NvScale.Set;
                    try
                    {
                        // the Valorant ini: only those keys change, the rest byte for byte, and the backup is made once
                        bool gOpen = false; Game.Valorant.Running = () => gOpen;
                        string gi = Path.Combine(dir, "GameUserSettings.ini");
                        byte[] gBom = { 0xEF, 0xBB, 0xBF };
                        string gBody = "[/Script/ShooterGame.ShooterGameUserSettings]\r\nbUseVSync=False\r\nFullscreenMode=0\r\nbShouldLetterbox=True\n[Señal]\r\nsg.ResolutionQuality=100.000000";
                        string gWant = "[/Script/ShooterGame.ShooterGameUserSettings]\r\nbUseVSync=False\r\nFullscreenMode=1\r\nLastConfirmedFullscreenMode=1\r\nPreferredFullscreenMode=1\r\nbLastConfirmedShouldLetterbox=False\r\nbShouldLetterbox=False\n[Señal]\r\nsg.ResolutionQuality=100.000000";
                        byte[] gOrig = gBom.Concat(Encoding.UTF8.GetBytes(gBody)).ToArray();
                        File.WriteAllBytes(gi, gOrig); Valorant.Fix(gi);
                        Is(File.ReadAllBytes(gi).SequenceEqual(gBom.Concat(Encoding.UTF8.GetBytes(gWant))) && File.ReadAllBytes(gi + ".tweakeroo").SequenceEqual(gOrig), "ini de valorant byte a byte: " + File.ReadAllText(gi));
                        Valorant.Fix(gi); Is(File.ReadAllBytes(gi).SequenceEqual(gBom.Concat(Encoding.UTF8.GetBytes(gWant))) && File.ReadAllBytes(gi + ".tweakeroo").SequenceEqual(gOrig), "la copia se hace una sola vez");
                        string gi2 = Path.Combine(dir, "Otro.ini"); File.WriteAllText(gi2, "a=1"); Valorant.Fix(gi2);
                        Is(File.ReadAllText(gi2) == "a=1\n[/Script/ShooterGame.ShooterGameUserSettings]\nFullscreenMode=1\nLastConfirmedFullscreenMode=1\nPreferredFullscreenMode=1\nbShouldLetterbox=False\nbLastConfirmedShouldLetterbox=False\n", "sin seccion de pantalla se crea al final: " + File.ReadAllText(gi2));
                        File.WriteAllBytes(gi, gOrig); gOpen = true; string gNo = null;
                        try { Valorant.Fix(gi); } catch (InvalidOperationException ex) { gNo = ex.Message; }
                        Is(gNo == "Cierra Valorant antes de cambiar sus ajustes." && File.ReadAllBytes(gi).SequenceEqual(gOrig), "con Valorant abierto no escribe");
                        // NVIDIA scaling: Fix records the old value and requests 2
                        int nvNow = 1; List<int> nvSets = new List<int>(); NvScale.Get = () => nvNow; NvScale.Set = x => { nvSets.Add(x); nvNow = x; };
                        string nvDir = Path.Combine(dir, "nv");
                        Check nvRow = Valorant.Checks(null, 0, "nvidia", nvDir).First(c => c.Title == "Escala de la gráfica");
                        Is(!nvRow.Ok && nvRow.Act == "Arreglar" && Valorant.Checks(null, 0, "nvidia", nvDir).Any(c => c.Title == "Anular el modo de los juegos" && c.Act == "Abrir panel" && c.Url.StartsWith(@"shell:AppsFolder\NVIDIACorp")), "escala pendiente con boton");
                        nvRow.Fix();
                        Is(nvSets.SequenceEqual(new[] { 2 }) && File.ReadAllText(Path.Combine(nvDir, "escalado.json")).Contains("\"Old\": 1") && Valorant.Checks(null, 0, "nvidia", nvDir).First(c => c.Title == "Escala de la gráfica").Ok, "arreglar la escala guarda la vieja y pone 2");
                        nvNow = 0; NvScale.Set = x => { }; gNo = null;
                        try { nvRow.Fix(); } catch (InvalidOperationException ex) { gNo = ex.Message; }
                        Is(gNo == "La gráfica no aceptó el cambio de escala." && File.ReadAllText(Path.Combine(nvDir, "escalado.json")).Contains("\"Old\": 1"), "si la grafica no acepta, avisa");
                        Is(Valorant.Checks(null, 0, "amd", nvDir).Count(c => c.Title == "Escala de la gráfica" && c.Act == "Abrir AMD Software" && c.Url != null) == 1 && !Valorant.Checks(null, 0, "amd", nvDir).Any(c => c.Fix != null), "AMD solo abre su programa");
                    }
                    finally { Game.Valorant.Running = gRun; NvScale.Get = nvGet; NvScale.Set = nvSet; }
                }
                {
                    Func<List<string>> mOn = Monitors.On; Action<string, bool> mSet = Monitors.Set; Func<bool> vRun = Game.Valorant.Running; Func<Res> dCur = Display.Current, dSav = Display.Saved; Action<Res> dSet = Display.Set; Action dRes = Display.Reset; Func<List<Res>> dMod = Display.Modes;
                    try
                    {
                        string vd2 = Path.Combine(dir, "val"); Directory.CreateDirectory(vd2);
                        Res cur = nat, wide = new Res { W = 1440, H = 1080, Hz = 144 }; List<string> on = new List<string> { "m1", "m2" }; bool run = false;
                        Display.Current = () => cur; Display.Saved = () => nat; Display.Set = x => cur = x; Display.Reset = () => cur = nat; Display.Modes = () => new List<Res> { nat, wide };
                        Monitors.On = () => on.ToList(); Monitors.Set = (id, o) => { if (o) on.Add(id); else on.Remove(id); }; Game.Valorant.Running = () => run;
                        ValorantSession s1 = new ValorantSession(vd2); s1.Begin(wide); Is(cur.W == 1920 && on.Count == 2 && s1.Pending && !s1.CanApply, "apagado no toca nada"); s1.End(); Is(!s1.Pending, "y se cierra");
                        s1.Stretch = true; s1.Begin(nat); Is(on.Count == 2 && !s1.CanApply, "con la normal no apaga monitores"); s1.End();
                        s1.Begin(wide); s1.Confirm(wide); Is(cur.W == 1920 && on.Count == 0, "confirmada vuelve a la normal"); run = true; Is(s1.Arrive(true) && cur.W == 1440, "estira");
                        Is(File.ReadAllText(Path.Combine(vd2, "valorant-registro.txt")).Contains("estirada a mano"), "queda en el registro de la sesión");
                        s1.End(); Is(cur.W == 1920 && on.Count == 0 && s1.Pending, "con el juego abierto solo vuelve la pantalla"); s1.Arrive(true);
                        // the app closes halfway and comes back
                        run = false; ValorantSession s2 = new ValorantSession(vd2); Is(s2.Pending && s2.Applied, "queda pendiente"); s2.End(); Is(cur.W == 1920 && on.Count == 2 && !s2.Pending, "al volver lo repone todo");
                        Monitors.Set = (id, o) => { if (o) throw new InvalidOperationException("no"); on.Remove(id); }; s2.Begin(wide); bool kept = false; try { s2.End(); } catch (InvalidOperationException) { kept = true; }
                        Is(kept && s2.Pending, "si un monitor no vuelve, queda pendiente"); Monitors.Set = (id, o) => on.Add(id); new ValorantSession(vd2).End(); Is(on.Count == 2, "y se reintenta");
                        on.Clear(); s1 = new ValorantSession(vd2); s1.Begin(wide); s1.End(); Is(on.Count == 0, "los que ya estaban desactivados se dejan igual");
                        // the display does not come back when closing: the session stays pending and the trace says why
                        Display.Reset = () => { throw new InvalidOperationException("sin reset"); }; s1.Begin(wide); s1.Arrive(true); kept = false;
                        try { s1.End(); } catch (InvalidOperationException) { kept = true; }
                        Is(kept && s1.Pending && File.ReadAllText(Path.Combine(vd2, "valorant-registro.txt")).Contains("no se pudo volver a la normal (pantalla 1440 × 1080): InvalidOperationException: sin reset"), "si la pantalla no vuelve, queda en el registro");
                        Display.Reset = () => cur = nat; s1.End(); Is(!s1.Pending && cur.W == 1920, "y al reintentar se cierra");
                    }
                    finally { Monitors.On = mOn; Monitors.Set = mSet; Game.Valorant.Running = vRun; Display.Current = dCur; Display.Saved = dSav; Display.Set = dSet; Display.Reset = dRes; Display.Modes = dMod; }
                }
                // colors
                Is(Vivid.Ramp(Vivid.All[0].Pick).Select((v, i) => Math.Abs(v - i % 256 * 257)).Max() <= 1, "curva normal");
                foreach (ColorProfile cp in Vivid.All) { ushort[] r = Vivid.Ramp(cp.Pick); for (int i = 1; i < 256; i++) Is(r[i] >= r[i - 1], "curva creciente " + cp.Id); }
                Is(Vivid.Clean(null).Profile == Vivid.None && Vivid.Clean(new ColorPick { Profile = "otro" }).Profile == Vivid.None && Vivid.Clean(new ColorPick { Profile = Vivid.Mine, Vib = 900, Bright = -3, Contrast = 50, Gamma = double.NaN }).Vib == 100 && Vivid.Clean(new ColorPick { Profile = "vivo", Vib = 1 }).Vib == 88, "color raro");
                {
                    Func<int?> gv = Vivid.GetVib; Action<int> sv = Vivid.SetVib; Func<ushort[]> gr = Vivid.GetRamp; Func<ushort[], bool> sr2 = Vivid.SetRamp;
                    try
                    {
                        int v = 100; ushort[] base0 = Vivid.Ramp(Vivid.All[0].Pick), rp = base0;
                        Vivid.GetVib = () => v; Vivid.SetVib = x => v = x; Vivid.GetRamp = () => rp; Vivid.SetRamp = x => { rp = x; return true; };
                        ColorPick vivo = Vivid.All.First(x => x.Id == "vivo").Pick;
                        string cdir = Path.Combine(dir, "color"); ColorSession c1 = new ColorSession(cdir);
                        Is(c1.Apply(Vivid.All[0].Pick) && v == 100 && !File.Exists(Path.Combine(cdir, "color.json")), "sin cambios no toca nada");
                        Is(c1.Apply(vivo) && v == 88 && rp[128] > base0[128], "vivo"); rp = base0; c1.Keep(); Is(rp[128] > base0[128], "se vuelve a poner si se pierde");
                        Is(c1.Apply(Vivid.All.First(x => x.Id == "maximo").Pick) && v == 100 && rp.SequenceEqual(base0), "cambiar de perfil parte del original"); c1.Apply(vivo);
                        new ColorSession(cdir).Restore(); Is(v == 100 && rp.SequenceEqual(base0) && !File.Exists(Path.Combine(cdir, "color.json")), "tras un cierre a medias vuelve");
                        Vivid.GetRamp = () => null; c1 = new ColorSession(cdir); Is(!c1.Apply(vivo) && v == 88, "sin brillo, la intensidad si"); c1.Restore(); Is(v == 100, "y vuelve");
                        Vivid.GetVib = () => null; c1 = new ColorSession(cdir); c1.Apply(vivo); c1.Restore(); Is(v == 100, "sin NVIDIA no toca la intensidad");
                        File.WriteAllText(Path.Combine(cdir, "color.json"), "{roto"); new ColorSession(cdir).Restore();
                    }
                    finally { Vivid.GetVib = gv; Vivid.SetVib = sv; Vivid.GetRamp = gr; Vivid.SetRamp = sr2; }
                    byte[] px = { 200, 100, 50, 0 }, po = new byte[4]; Vivid.Paint(px, po, Vivid.All[1].Pick, 50); Is(px.Take(3).Zip(po, (x, y) => Math.Abs(x - y)).All(x => x <= 1), "vista previa neutra");
                }
                Prefs pc = Prefs.Parse("{\"Game\":\"otro\",\"Colors\":{\"valorant\":{\"Profile\":\"propio\",\"Vib\":200,\"Bright\":50,\"Contrast\":50,\"Gamma\":1},\"x\":{}}}");
                Is(pc.Game == "fortnite" && pc.Colors.Count == 1 && pc.ColorOf(Game.Valorant).Vib == 100 && pc.ColorOf(Game.Fortnite).Profile == Vivid.None && Prefs.Parse("{}").Colors != null, "ajustes por juego");
                Prefs ph = Prefs.Parse("{\"Game\":\"valorant\",\"Paths\":{\"fortnite\":\"D:\\\\Juegos\\\\otro.exe\",\"valorant\":\"D:\\\\Riot\\\\VALORANT-Win64-Shipping.exe\",\"x\":\"D:\\\\x.exe\"},\"Hidden\":[\"fortnite\",\"valorant\",\"x\"]}");
                Is(ph.Paths.Count == 1 && ph.Paths["valorant"] == @"D:\Riot\VALORANT-Win64-Shipping.exe" && ph.Hidden.Count == 0 && ph.Game == "valorant", "rutas a mano y no se ocultan todos");
                Prefs pq = Prefs.Parse("{\"Game\":\"valorant\",\"Hidden\":[\"valorant\",\"valorant\"]}");
                Is(pq.Hidden.Count == 1 && pq.Game == "fortnite" && Prefs.Parse("{}").Paths != null && Prefs.Parse("{}").Hidden != null, "el juego oculto no queda a la vista");
                Is(Game.OfTweak("fn-vsync") == "fortnite" && Game.OfTweak("val-gpu") == "valorant" && Game.OfTweak("pw-plan") == null, "tweaks por juego");

                // Valorant: aspect ratio
                Res big = new Res { W = 2560, H = 1440, Hz = 200 };
                Is(Display.ValorantBest(big).W == 2091 && Display.ValorantBest(nat).W == 1568 && Display.Stretched(new[] { big, new Res { W = 1440, H = 1080 }, new Res { W = 2091, H = 1440 } }, big, 1.45).Count == 1, "la mas estirada de Valorant");
                Is(Custom.Invalid("1440", "1080", big, 1.45).Contains("1566") && Custom.Invalid("1566", "1080", big, 1.45) == null && Custom.Invalid("1440", "1080", big) == null, "regla de 1,45");
                // restarts: only when needed
                {
                    Func<DateTime> bt = Pending.Boot; Func<string, DateTime?> st0 = Pending.Started; Func<bool> vr0 = Game.Valorant.Running; Func<bool> gr0 = IniOp.GameRunning;
                    try
                    {
                        string pd = Path.Combine(dir, "pend"); Directory.CreateDirectory(pd);
                        DateTime boot = DateTime.UtcNow.AddHours(-1), expl = DateTime.UtcNow.AddHours(-1); bool vg = false;
                        Pending.Boot = () => boot; Pending.Started = prc => prc == "explorer" ? expl : (DateTime?)null; Game.Valorant.Running = () => vg; IniOp.GameRunning = () => false;
                        Func<string, string, Tweak> tw = (id, rs) => new Tweak { Id = id, Restart = rs };
                        Tweak pcT = tw("p", "el PC"), se = tw("s", "la sesión"), ga = tw("val-x", "el juego"), ch = tw("c", "Chrome"), no = tw("n", "");
                        Pending p1 = new Pending(pd);
                        foreach (Tweak t in new[] { pcT, se, ga, ch, no }) p1.Change(t);
                        Is(p1.Need(pcT) && p1.Need(se) && !p1.Need(ga) && !p1.Need(ch) && !p1.Need(no), "reinicio solo si hace falta");
                        Is(p1.Ask(new[] { se, pcT }).Contains("Reinicia el PC") && p1.Ask(new[] { se }).Contains("Cierra sesión") && p1.Ask(new[] { no, ga }) == "", "un solo aviso");
                        p1.Change(pcT); Is(!p1.Need(pcT), "volver atras no pide reinicio"); p1.Change(pcT); Is(new Pending(pd).Need(pcT), "se recuerda");
                        boot = DateTime.UtcNow.AddSeconds(1); Is(!new Pending(pd).Need(pcT), "tras reiniciar no lo pide"); expl = DateTime.UtcNow.AddSeconds(1); Is(!new Pending(pd).Need(se), "tras volver a entrar tampoco");
                        vg = true; p1.Change(ga); Is(p1.Need(ga), "con el juego abierto si"); vg = false; Is(!p1.Need(ga), "cerrado no");
                    }
                    finally { Pending.Boot = bt; Pending.Started = st0; Game.Valorant.Running = vr0; IniOp.GameRunning = gr0; }
                }
                // while playing
                {
                    List<Boost> was = Boost.All;
                    Is(was.Select(b => b.Id).Distinct().Count() == was.Count, "ids de al jugar");
                    try
                    {
                        string state = "a", bd = Path.Combine(dir, "boost");
                        Boost.All = new List<Boost> { new Boost { Id = "1", Name = "1", On = () => { string o = state; state = "b"; return o; }, Off = o => state = o }, new Boost { Id = "2", Name = "2", On = () => { throw new InvalidOperationException(); }, Off = o => { } } };
                        BoostSession b1 = new BoostSession(bd); Is(b1.Use(new[] { "1", "2" }).Single() == "2" && state == "b" && b1.Count == 1, "poner"); b1.Use(new[] { "1" }); Is(state == "b", "no repite");
                        new BoostSession(bd).Use(new string[0]); Is(state == "a", "tras un cierre a medias se quita"); Is(new BoostSession(bd).Count == 0, "y queda limpio");
                        File.WriteAllText(Path.Combine(bd, "aljugar.json"), "{x"); Is(new BoostSession(bd).Count == 0, "archivo roto");
                    }
                    finally { Boost.All = was; }
                }
                // driver
                Is(Drivers.Installed("32.0.16.2114") == "621.14" && Drivers.Installed("31.0.15.5222") == "552.22" && Drivers.Installed("") == "" && Drivers.Installed(null) == "" && Drivers.Installed("21.2") == "", "version del driver");
                {
                    DriverInfo kn = Drivers.Known; Func<string, string> dl = Drivers.Download;
                    try
                    {
                        Hw gh = new Hw { Gpu = "NVIDIA GeForce RTX 3060", Nvidia = true, Driver = "32.0.16.2114" };
                        string asked = null; Drivers.Download = u => { asked = u; return "{ \"Success\" : \"1\", \"Version\" : \"621.24\", \"ReleaseDateTime\" : \"Thu Nov 12, 2026\", \"DetailsURL\" : \"https://www.nvidia.com/en-us/drivers/details/285320/\" }"; };
                        Drivers.Known = null; Is(Drivers.Advice(gh) == null, "sin dato no aconseja"); Drivers.Check(dir, gh); Check dc = Drivers.Advice(gh);
                        Is(asked.Contains("pfid=929") && dc.Kind == Kind.Gratis && dc.Url.EndsWith("285320/") && dc.Text.Contains("621.14") && dc.Text.Contains("12/11/2026") && Drivers.Fresh, "driver nuevo");
                        gh.Driver = "32.0.16.2124"; Is(Drivers.Advice(gh).Ok, "driver al dia");
                        Drivers.Known = null; Drivers.Load(dir); Is(Drivers.Known != null && Drivers.Known.Version == "621.24", "se recuerda");
                        Drivers.Download = u => "{ \"Version\" : \"999.1\", \"DetailsURL\" : \"https://malo.com/\" }"; Drivers.Check(dir, gh); Is(Drivers.Known.Version == "621.24", "otra web no vale");
                        Drivers.Download = u => { throw new System.Net.WebException("x"); }; Drivers.Check(dir, gh); Is(Drivers.Known.Version == "621.24", "sin internet");
                        asked = null; Drivers.Download = u => { asked = u; return ""; }; Drivers.Check(dir, new Hw { Gpu = "AMD Radeon RX 6600" }); Is(asked == null, "AMD no pregunta a NVIDIA"); Drivers.Check(dir, new Hw { Gpu = "NVIDIA GeForce GTX 1060", Nvidia = true }); Is(asked.Contains("pfid=845"), "rama antigua");
                        File.Delete(Path.Combine(dir, "driver.json"));
                    }
                    finally { Drivers.Known = kn; Drivers.Download = dl; }
                }
                // what applies right away: real read, without changing anything
                Is(Live.Mouse.Length == 3 && Live.Keys(Live.Sticky) > 0 && Live.Keys(Live.Filter) > 0 && Live.Keys(Live.Toggle) > 0 && Live.Keys(Live.MouseKeys) > 0, "lectura de teclas y raton");
                // an old Original also restores the tweaks that came later
                {
                    RegOp.WriteValue(Root, "viejo", "d:3"); RegOp.WriteValue(Root, "nuevo", "d:8");
                    Tweak ta = Mk("ta", new RegOp(Root, "viejo", "d:1", "d:0")), tb = Mk("tb", new RegOp(Root, "nuevo", "d:1", "d:0"));
                    Snapshot so2 = new Engine(dir + "2", new[] { ta }).Save("Original", "original");
                    Engine en = new Engine(dir + "2", new[] { ta, tb }); en.Enable(ta); en.Enable(tb);
                    Is(en.DiffCount(so2, en.ReadAll()) == 2, "cuenta lo nuevo"); int nn; en.Restore(so2, out nn);
                    Is(nn == 2 && V("viejo") == "d:3" && V("nuevo") == "d:8" && en.DiffCount(so2, en.ReadAll()) == 0, "Original devuelve lo nuevo");
                }

                // macros: what they send, and that they only do it in Creative with Fortnite in front
                MacroConfig mc = Macros.Default();
                string realBinds = Binds.Folder; Binds.Folder = Path.Combine(dir, "sin-teclas");
                Func<List<Act>, string> seq = x => string.Join(" ", x.Select(c => c.Vk < 0 ? "(" + -c.Vk + ")" : Macros.KeyName(c.Vk)[0] == 'C' ? (c.Vk == 1 ? "L" : "R") + (c.Down ? "+" : "-") : Macros.KeyName(c.Vk) + (c.Down ? "+" : "-")));
                // one tap: both edits fire on press, even if the key is not released, with their pause in between
                Is(seq(Macros.Press(mc, "doble")) == "F+ F- L+ L- (80) F+ F- L+ L-" && Macros.Release(mc, "doble").Count == 0, "doble de un toque: " + seq(Macros.Press(mc, "doble")));
                Is(seq(Macros.Press(mc, "edit")) == "F+ F- L+ L-" && seq(Macros.Press(mc, "editreset")) == "F+ F- L+ L- (80) F+ F- R+ R-" && seq(Macros.Press(mc, "reset")) == "F+ F- R+ R-", "un toque, las demas");
                mc.ConfirmOnRelease = false;
                Is(seq(Macros.Press(mc, "doble")) == "F+ F- L+ L- F+ F- (80) F+ F- L+ L- F+ F-", "doble sin confirmar al soltar: " + seq(Macros.Press(mc, "doble")));
                mc.ConfirmOnRelease = true; mc.Hold = true;
                Is(seq(Macros.Press(mc, "doble")) == "F+ F- L+" && seq(Macros.Release(mc, "doble")) == "L- (80) F+ F- L+ L-", "doble manteniendo: " + seq(Macros.Release(mc, "doble")));
                Is(seq(Macros.Press(mc, "reset")) == "F+ F- R+ R-" && Macros.Release(mc, "reset").Count == 0, "reset manteniendo");
                mc.ConfirmOnRelease = false;
                Is(seq(Macros.Release(mc, "edit")) == "L- F+ F-", "manteniendo sin confirmar al soltar");
                Is(seq(Macros.Press(Macros.Default(), "salir")) == "F+ F-" && Macros.Release(Macros.Default(), "salir").Count == 0, "salir de edicion");
                // the stuck case: Fortnite confirms on release and the app did not know, so it sent one edit too many
                {
                    string bf = Binds.Folder; MacroConfig stuck = Macros.Default(); stuck.ConfirmOnRelease = false; stuck.Hold = true;
                    try
                    {
                        Binds.Folder = Path.Combine(dir, "sav"); Directory.CreateDirectory(Path.Combine(Binds.Folder, "x"));
                        Is(!Macros.Sync(stuck) && !stuck.ConfirmOnRelease, "sin archivo no cambia nada");
                        MemoryStream mem = new MemoryStream();
                        Action<string> put = x => { mem.Write(BitConverter.GetBytes(x.Length + 1), 0, 4); byte[] by = Encoding.ASCII.GetBytes(x + "\0"); mem.Write(by, 0, by.Length); };
                        put("bEditConfirmOnReleaseEnabled"); put("BoolProperty"); mem.Write(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0x10 }, 0, 9); put("UserActionBindings"); put("ArrayProperty");
                        for (int i = 0; i < 120; i++) { put("ActionName"); put("NameProperty"); put("Accion" + i); put("KeyName"); put("NameProperty"); put("None"); }
                        foreach (string[] k in new[] { new[] { "PerformBuildingEditInteraction", "I" }, new[] { "EditSelect", "O" }, new[] { "BuildingEditReset", "RightMouseButton" } }) { put("ActionName"); put("NameProperty"); put(k[0]); put("KeyName"); put("NameProperty"); put(k[1]); }
                        MemoryStream file = new MemoryStream(); file.Write(new byte[] { 0x45, 0x43, 0x46, 0x44, 0x78, 0x9c }, 0, 6);
                        using (System.IO.Compression.DeflateStream df = new System.IO.Compression.DeflateStream(file, System.IO.Compression.CompressionMode.Compress, true)) df.Write(mem.ToArray(), 0, (int)mem.Length);
                        File.WriteAllBytes(Path.Combine(Binds.Folder, "x", "ClientSettings.Sav"), file.ToArray());
                        Is(Macros.Sync(stuck) && stuck.ConfirmOnRelease && stuck.Edit == 'I' && stuck.Select == 'O' && seq(Macros.Release(stuck, "edit")) == "O-", "ya no sobra un editar: " + seq(Macros.Release(stuck, "edit")));
                        stuck.Manual = true; stuck.ConfirmOnRelease = false; Is(!Macros.Sync(stuck) && !stuck.ConfirmOnRelease, "a mano se respeta");
                    }
                    finally { Binds.Folder = bf; }
                }
                Is(Macros.Clean(new MacroConfig { Edit = 69, Select = 1, Reset = 2, Keys = new Dictionary<string, int>(), On = new List<string>() }).Gap == 80, "una configuracion vieja coge la pausa normal");
                foreach (string id in Macros.Ids) Is(Macros.Press(mc, id).Concat(Macros.Release(mc, id)).Where(c => c.Vk > 0).GroupBy(a => a.Vk).All(g => g.Count(a => a.Down) == g.Count(a => !a.Down)), "ninguna tecla queda pulsada: " + id);
                mc.Reset = Macros.WheelDown; Macros.Save(dir, mc); Is(Macros.Load(dir).Reset == Macros.WheelDown, "rueda guardada");
                mc.Select = Macros.WheelUp; Macros.Save(dir, mc); Is(Macros.Load(dir).Select == 1, "seleccionar con rueda no vale");
                const string pre = "[2026.11.22-20.51.53:033][191]";
                Is(Macros.Mode(pre + "LogWorld: Bringing World /VKPlay/Maps/X.X up for play", false), "creativo");
                Is(!Macros.Mode(pre + "LogWorld: Bringing World /Delilah/Maps/Delilah_Terrain.Delilah_Terrain up for play", true) && !Macros.Mode(pre + "LogLoad: LoadMap: 1.2.3.4/VKPlay/Maps/X", true) && !Macros.Mode(pre + "LogWorld: Bringing World /Game/Maps/Frontend.Frontend up for play", true), "otros modos y cargas");
                Is(Macros.Mode(pre + "LogChat: Bringing World /Delilah/", true), "otras lineas no cambian el modo");
                string logs = FpsLog.Folder; Action<Act> send = Macros.Send; Func<int, bool> dn = Macros.IsDown; Func<bool> frontReal = Macros.Front;
                try
                {
                    FpsLog.Folder = Path.Combine(dir, "macro-logs"); Directory.CreateDirectory(FpsLog.Folder);
                    string lf = Path.Combine(FpsLog.Folder, "FortniteGame.log");
                    List<Act> sent = new List<Act>(); bool trig = false, front = true;
                    Macros.Send = a => { lock (sent) sent.Add(a); }; Macros.IsDown = vk => vk == 5 && trig; Macros.Front = () => front;
                    mc = Macros.Default(); mc.Keys["doble"] = 5; mc.On.Add("doble"); mc.Delay = 5; mc.Gap = 40;
                    File.WriteAllText(lf, pre + "LogWorld: Bringing World /Delilah/Maps/D.D up for play\n");
                    Macros.Use(mc); System.Threading.Thread.Sleep(300); Is(Macros.Status == "Apagadas", "sin aceptar no arranca");
                    mc.Accepted = true; Macros.Use(mc); trig = true; System.Threading.Thread.Sleep(900); Is(sent.Count == 0 && Macros.Status.StartsWith("En espera"), "en ranked no manda nada");
                    trig = false; File.AppendAllText(lf, pre + "LogWorld: Bringing World /VKPlay/Maps/X.X up for play\n"); System.Threading.Thread.Sleep(900);
                    front = false; System.Threading.Thread.Sleep(600); trig = true; System.Threading.Thread.Sleep(500); Is(sent.Count == 0, "sin Fortnite delante no manda nada"); trig = false; front = true; System.Threading.Thread.Sleep(900);
                    trig = true; System.Threading.Thread.Sleep(500); // without releasing
                    lock (sent) Is(Macros.Status == "Activas" && seq(sent) == "F+ F- L+ L- F+ F- L+ L-", "doble edicion en creativo sin soltar la tecla: " + seq(sent));
                    trig = false; System.Threading.Thread.Sleep(300); lock (sent) Is(sent.Count == 8, "soltar no repite"); mc.Hold = true; Macros.Use(mc); System.Threading.Thread.Sleep(100);
                    trig = true; System.Threading.Thread.Sleep(300); File.AppendAllText(lf, pre + "LogLoad: LoadMap: /Game/Maps/Frontend\n"); System.Threading.Thread.Sleep(900);
                    lock (sent) Is(!sent[sent.Count - 1].Down && sent[sent.Count - 1].Vk == 1 && sent.Count(a => a.Down) == sent.Count(a => !a.Down), "al salir de creativo suelta el clic");
                    File.WriteAllText(Path.Combine(dir, "macros.json"), "{roto"); Is(Macros.Load(dir).On.Count == 0, "macros ilegibles");
                }
                finally { Macros.Stop(); FpsLog.Folder = logs; Macros.Send = send; Macros.IsDown = dn; Macros.Front = frontReal; }

                Binds.Folder = realBinds;
                // speed: the ramp-up does not count and the verdict looks at ping under load
                {
                    Is(SpeedTest.Verdict(new SpeedResult { Down = 300, Up = 50, Idle = 20, Loaded = 90 }).Contains("sube 70 ms") && SpeedTest.Verdict(new SpeedResult { Down = 300, Up = 50, Idle = 20, Loaded = 25 }).Contains("solo 5 ms") && SpeedTest.Verdict(new SpeedResult { Down = 5, Up = 1 }).StartsWith("La velocidad es baja"), "veredicto de velocidad");
                    Func<bool, Action<double>, double> mea = SpeedTest.Measure; Func<string, int, System.Threading.Tasks.Task<int>> pg = NetTest.Ping;
                    try
                    {
                        bool load = false; NetTest.Ping = (h, t) => System.Threading.Tasks.Task.FromResult(load ? 80 : 20);
                        SpeedTest.Measure = (down, step) => { load = true; System.Threading.Thread.Sleep(400); step(1); load = false; return down ? 250 : 40; };
                        SpeedResult sr = System.Threading.Tasks.Task.Run(() => SpeedTest.Run((k, v) => { })).Result;
                        Is(sr.Down == 250 && sr.Up == 40 && sr.Idle == 20 && sr.Loaded == 80, "velocidad: " + sr.Idle + " " + sr.Loaded);
                        SpeedTest.Save(dir, sr); Is(SpeedTest.Load(dir).Down == 250, "velocidad guardada"); File.WriteAllText(Path.Combine(dir, "velocidad.json"), "{x"); Is(SpeedTest.Load(dir) == null, "velocidad ilegible");
                    }
                    finally { SpeedTest.Measure = mea; NetTest.Ping = pg; }
                }
                // request and release the timer
                Is(Precise.Set(true) && Precise.Set(true) && Precise.Set(false), "temporizador de 0,5 ms");

                // the saved scan comes back identical; if something is missing or unreadable, it is not valid
                ScanData sd = new ScanData { Hw = new Hw { Cpu = "c", Gpu = "g", Cores = 8, Ethernet = true, SecureBoot = true, DriverDate = new DateTime(2026, 5, 4, 1, 0, 0) }, Text = Scanner.Steps.ToDictionary(s => s.Key, s => s.Key + "\nx") };
                Is(Scanner.Load(dir) == null, "sin escaneo guardado");
                Scanner.Save(dir, sd);
                ScanData sl = Scanner.Load(dir);
                Is(sl != null && sl.Hw.Cpu == "c" && sl.Hw.Cores == 8 && sl.Hw.Ethernet && sl.Hw.SecureBoot == true && sl.Hw.Tpm2 == null && sl.Hw.DriverDate == sd.Hw.DriverDate && sl.Text["Gpu"] == "Gpu\nx", "escaneo guardado");
                sd.Hw.DriverDate = DateTime.MinValue; Scanner.Save(dir, sd); Is(Scanner.Load(dir).Hw.DriverDate == DateTime.MinValue, "driver sin fecha");
                sd.Text.Remove("Net"); Scanner.Save(dir, sd); Is(Scanner.Load(dir) == null, "escaneo a medias");
                File.WriteAllText(Path.Combine(dir, "scan.json"), "{no es json"); Is(Scanner.Load(dir) == null, "escaneo ilegible");

                // updates: only signed, newer, over https and with the correct hash
                string key = Updater.PublicKey;
                Func<string, int, byte[]> net = Updater.Download;
                try
                {
                    System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
                    using (System.Security.Cryptography.RSACryptoServiceProvider rsa = new System.Security.Cryptography.RSACryptoServiceProvider(2048, new System.Security.Cryptography.CspParameters(24)))
                    {
                        rsa.PersistKeyInCsp = false;
                        Updater.PublicKey = rsa.ToXmlString(false);
                        Func<Release, string> sign = x => { byte[] b = Encoding.UTF8.GetBytes(js.Serialize(x)); return js.Serialize(new Signed { Data = Convert.ToBase64String(b), Signature = Convert.ToBase64String(rsa.SignData(b, System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256"))) }); };
                        Func<string, bool> bad2 = x => { try { Updater.Parse(x, new Version(3, 0, 0, 0)); return false; } catch (InvalidDataException) { return true; } };
                        byte[] exe = Encoding.UTF8.GetBytes("version nueva");
                        string sha; using (System.Security.Cryptography.SHA256 h = System.Security.Cryptography.SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(exe)).Replace("-", "");
                        Release rel = new Release { Version = "3.1.0", Notes = new List<string> { "ñ" }, Url = "https://x/download/v3.1.0/y.exe", Sha256 = sha };
                        string good = sign(rel);
                        Is(Updater.Parse(sign(new Release { Version = "3.7.0", Url = "https://x/download/v0.37.0/y.exe", Sha256 = sha }), new Version(0, 40, 0, 0)) == null, "etiqueta distinta ignorada");
                        string old37 = sign(new Release { Version = "3.7.0", Url = "https://x/download/v3.7.0/y.exe", Sha256 = sha });
                        Is(Updater.Parse(old37, new Version(0, 40, 0, 0), "v0.37.0") == null, "manifiesto viejo en release renombrada ignorado");
                        Is(Updater.Parse(old37, new Version(0, 40, 0, 0), "v3.7.0") != null, "misma etiqueta sí avisa");
                        Is(Updater.Parse(good, new Version(3, 0, 0, 0)).Notes[0] == "ñ", "actualizacion firmada");
                        Is(Updater.Parse(good, new Version(3, 1, 0, 0)) == null && Updater.Parse(good, new Version(4, 0, 0, 0)) == null, "no baja de version");
                        Signed sg = js.Deserialize<Signed>(good); sg.Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(js.Serialize(new Release { Version = "9.0.0", Url = "https://malo", Sha256 = sha })));
                        Is(bad2(js.Serialize(sg)), "firma ajena rechazada");
                        Is(bad2(sign(new Release { Version = "3.1.0", Url = "http://x/y.exe", Sha256 = sha })), "sin https rechazada");
                        string target = Path.Combine(dir, "app.exe"); File.WriteAllText(target, "version vieja");
                        Updater.Download = (u, m) => Encoding.UTF8.GetBytes("otra cosa");
                        bool no = false; try { Updater.Apply(rel, target); } catch (InvalidDataException) { no = true; }
                        Is(no && File.ReadAllText(target) == "version vieja", "archivo cambiado rechazado");
                        Updater.Download = (u, m) => exe;
                        Updater.Apply(rel, target); Is(File.ReadAllText(target) == "version nueva" && File.ReadAllText(target + ".viejo") == "version vieja", "cambia el exe");
                        Updater.Undo(target); Is(File.ReadAllText(target) == "version vieja", "y sabe volver");
                    }
                }
                finally { Updater.PublicKey = key; Updater.Download = net; }
                Is(Updater.PublicKey.Contains("<Modulus>") && !Updater.PublicKey.Contains("<D>"), "solo viaja la clave publica");

                // install and uninstall in throwaway folders
                string d0 = Installer.Dir, m0 = Installer.Menu, k0 = Installer.Desktop;
                Microsoft.Win32.RegistryHive h0 = Installer.Hive;
                try
                {
                    Installer.Dir = Path.Combine(dir, "Programas"); Installer.Menu = Path.Combine(dir, "menu"); Installer.Desktop = Path.Combine(dir, "escritorio"); Installer.Hive = Microsoft.Win32.RegistryHive.CurrentUser;
                    Directory.CreateDirectory(Installer.Menu); Directory.CreateDirectory(Installer.Desktop);
                    Is(!Installer.InstalledIsEnough() && !Installer.TermsOk(dir), "sin instalar");
                    Action<bool> autoW = Autostart.Write; bool? auto = null; Autostart.Write = on => auto = on;
                    Installer.AcceptTerms(dir); Installer.Install(true); Installer.Install(true);
                    const string uk = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Tweakeroo";
                    Is(Installer.TermsOk(dir) && Installer.InstalledIsEnough() && File.Exists(Path.Combine(Installer.Menu, "Tweakeroo.lnk")) && File.Exists(Path.Combine(Installer.Desktop, "Tweakeroo.lnk")) && RegOp.ReadValue(uk, "DisplayVersion") == "s:" + Installer.Short, "instalar");
                    Installer.Remove(); Autostart.Write = autoW; Is(auto == false, "desinstalar quita abrir con Windows");
                    Is(!File.Exists(Path.Combine(Installer.Menu, "Tweakeroo.lnk")) && RegOp.ReadValue(uk, "DisplayName") == Op.None, "desinstalar");
                }
                finally { Installer.Dir = d0; Installer.Menu = m0; Installer.Desktop = k0; Installer.Hive = h0; }
                Is(L.Terms().Contains("# Privacidad"), "terminos incluidos");
                bool wasEn = L.En;
                string ldir = Path.Combine(dir, "idioma"); Directory.CreateDirectory(ldir);
                L.Load(ldir); bool def = L.En; L.Save(ldir, false); L.Load(ldir); bool es = !L.En; L.En = wasEn;
                Is(def && es, "ingles por defecto y español si se elige");
                Is(Custom.Invalid("1500", "1000", nat) == null && Custom.Invalid("1500", "x", nat) != null && Custom.Invalid("700", "600", nat) != null && Custom.Invalid("1920", "1200", nat) != null, "reglas de la personalizada");

                // purchase plan
                List<Check> pl = Review.Plan(my, new List<Match> { new Match { Median = 383, Cap = 0 } });
                Is(pl[0].Title.StartsWith("Gráfica: RTX") && pl[0].Text.StartsWith("Es el cuello de botella") && pl.Any(c => c.Title.StartsWith("Procesador: Ryzen 7 7800X3D")), "plan: " + string.Join("; ", pl.Select(c => c.Title)));
                my.Hz = my.MaxHz = 200; Is(Review.Plan(my, new List<Match> { new Match { Median = 383, Cap = 0 } }).Any(c => c.Title == "Monitor de 360 Hz"), "plan monitor");

                // English: nothing stays in Spanish
                L.En = true;
                try
                {
                    Is(L.Terms().Contains("# Privacy"), "terminos en ingles");
                    foreach (Tweak t in cat) foreach (string x in new[] { t.Name, t.Desc, t.Cat, "Efecto " + t.Effect.ToLowerInvariant(), t.Restart.Length > 0 ? "Reinicia " + t.Restart : "" }) L.T(x);
                    foreach (Hw h in new[] { desk, cable, lap, my, poor, new Hw { Cpu = "Intel Core i5-12400F", Gpu = "NVIDIA GeForce RTX 5090", Cores = 6, Socket = "LGA1700", ScreenW = 1920, ScreenH = 1080, Hz = 60, MaxHz = 60, RamGb = 16, DiskName = "x", DiskFreeGb = 90, Ethernet = true, SecureBoot = false, Tpm2 = true, Iommu = true, DriverDate = DateTime.Now.AddYears(-1) }, new Hw { Cpu = "AMD Ryzen 5 5600", Gpu = "NVIDIA GeForce RTX 5080", Socket = "AM4", ScreenW = 1920, ScreenH = 1080, Hz = 360, MaxHz = 360, SecureBoot = true, Tpm2 = true, Iommu = true, DriverDate = DateTime.Now }, new Hw { Cpu = "AMD Ryzen 7 5800H", Gpu = "NVIDIA GeForce RTX 3050", Laptop = true, ScreenW = 1920, ScreenH = 1080, Hz = 360, MaxHz = 360 } })
                    {
                        foreach (Tweak t in cat) { Advice ad = Advisor.For(t, h); L.T((ad.Yes ? "Recomendado. " : "No recomendado. ") + ad.Why); }
                        foreach (string x in Advisor.Notes(h)) L.T(x);
                        foreach (Check c in Review.For(h, new List<Match>()).Concat(Review.For(h, new List<Match> { new Match { Median = 238, Cap = 240 } })).Concat(Review.Plan(h, new List<Match>()))) { L.T(c.Title); L.T(c.Text); L.T(c.Tag); }
                    }
                    L.T(FpsLog.Compare(new List<Match>(), null)); L.T(FpsLog.Compare(new List<Match>(), "2026")); L.T(FpsLog.Compare(new List<Match> { mk("2026-01-03", 300), mk("2026-01-04", 320), mk("2026-01-01", 200), mk("2026-01-02", 220) }, "2026-01-02 12:00"));
                    string xaml; using (StreamReader rd = new StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Window.xaml"))) xaml = rd.ReadToEnd();
                    L.Xaml(xaml);
                    foreach (Check c in Wifi.Advice(true, 50, 6, new List<string> { "steam" }).Concat(Wifi.Advice(true, 90, 36, new List<string>())).Concat(Wifi.Advice(false, 0, 0, new List<string>()))) { L.T(c.Title); L.T(c.Text); }
                    for (int i = 0; i < NetTest.Regions.GetLength(0); i++) L.T("Servidores de Epic: " + NetTest.Regions[i, 1]);
                    { PingStats k = PingStats.Of(Enumerable.Repeat(5, 9).ToList()), f = PingStats.Of(Enumerable.Repeat(150, 9).ToList()), o = PingStats.Of(Enumerable.Repeat(-1, 9).ToList()), m = PingStats.Of(new[] { 5, -1, 5, 90 });
                      foreach (PingStats x1 in new[] { k, f, o, m }) foreach (PingStats x2 in new[] { k, o, m }) foreach (PingStats x3 in new[] { k, f, o, m }) foreach (bool wf in new[] { true, false }) L.T(NetTest.Verdict(new NetResult { Router = x1, Internet = x2, Epic = x3, Wifi = wf }));
                      L.T(NetTest.Compare(new NetResult { Date = "2026-11-22 20:00:00", Epic = m }, null)); NetTile tl = new NetTile(); tl.Show(m); L.T(tl.Sub); tl.Show(o); L.T(tl.Main); }
                    L.T(SpeedTest.Verdict(new SpeedResult { Down = 300, Up = 50, Idle = 20, Loaded = 90 })); L.T(SpeedTest.Verdict(new SpeedResult { Down = 5, Up = 1, Idle = 20, Loaded = 25 }));
                    foreach (string x in new[] { "Descarga", "Subida", "Ping con carga", "Salir de edición", "Manda solo la tecla de editar, por si te quedas dentro.", "Cierra Fortnite antes de medir la velocidad: la prueba llena la conexión.", "Prueba anterior (2026-11-22 20:00): descarga 300 Mbps, subida 50 Mbps.", "No se pudo medir: La prueba no recibió datos." }) L.T(x);
                    foreach (string x in new[] { "Conexión", "Router", "Internet", "Servidores de Epic", "Midiendo…", "Medir otra vez", "Midiendo durante 15 segundos. Lo más fiable es hacerlo sin descargas y, otra vez, mientras se juega.", "Activo ahora.", "Un toque", "Mantener", "No se encontraron las teclas de Fortnite. Elígelas a mano.", "Teclas leídas de Fortnite: editar E, resetear Clic derecho.", "Entra a editar, selecciona y confirma.", "Edita, confirma y edita otra vez donde apunta la mira.", "Edita, confirma y devuelve la pieza a su forma." }) L.T(x);
                    foreach (string x in new[] { "Todas", "Sin rebote", "Ninguna", "Noche", "lo-fi tranquilo", "8 bits", "pad y lluvia", "Jugar", "Borrar historial de FPS", "Borrar escaneo", "Volver al estado Original", "Restablecer Ajustes", "Pulsa otra vez para confirmar", "Escaneo borrado. Se pedirá otra vez al volver a abrir.", "El PC ya está en el estado Original.", "Ajustes restablecidos.", "Datos copiados. No incluyen nada personal.", "Historial de FPS borrado.", "Ajustes exportados. No incluyen los tweaks.", "No se pudo importar: El archivo no es una copia de ajustes de Tweakeroo.", "Es la versión más reciente.", "No hay dónde buscar actualizaciones.", "Historial de versiones", "Términos y privacidad", "Abrir", "Salir", "La primera vez que se usa una resolución se pide confirmación. Sin respuesta en 30 segundos, se restaura la anterior. Tweakeroo debe quedar abierto durante la partida. En torneos oficiales Epic puede forzar 16:9." }) L.T(x);
                    foreach (Boost x in Boost.All) { L.T(x.Name); L.T(x.Desc); }
                    { DriverInfo kn = Drivers.Known; Drivers.Known = new DriverInfo { Version = "621.24", Date = "12/11/2026", Url = "https://www.nvidia.com/x" }; foreach (string x in new[] { "32.0.16.2114", "32.0.16.2124" }) { Check c = Drivers.Advice(new Hw { Nvidia = true, Driver = x }); L.T(c.Title); L.T(c.Text); L.T(c.Act); } Drivers.Known = kn; L.T("Abrir web"); }
                    foreach (string x in new[] { "Al jugar", "Stretched", "Stretched Resolution", "Recomendada", "no cumple 1,45:1", "1,45:1 · 200 Hz", "Volviendo en 10 s", "Probar 10 segundos", "Sonido", "Música", "Cuándo suena", "Volumen de la música", "Efectos de sonido", "Volumen de los efectos", "Noche", "Arcade", "Lluvia", "Apagada", "Solo con la app delante", "Siempre menos al jugar", "Silenciada", "Juegos", "No encontrado", "Cambiar carpeta", "Mostrar en el selector", "Buscar el archivo", "Quitar del selector", "Tiene que quedar al menos un juego.", "Retraso", "Cierra sesión y vuelve a entrar para completar los cambios.", "Reinicia el juego para completar los cambios.", "x: activado. Reinicia Chrome para que haga efecto.", "Recomendada, 200 Hz", "Se crea al usarla, 200 Hz", "Valorant solo estira desde 1,45:1. Con 1080 de alto, el mínimo de ancho es 1566.", "Valorant solo estira con una relación de aspecto de 1,45:1 o más: las más cuadradas, como 4:3, no funcionan.", "La pantalla se estira al pulsar Jugar y vuelve al cerrar el juego. No hace falta crear nada en el panel de NVIDIA: se usan los Hz más altos de cada resolución.", "Abre Fortnite con la resolución elegida en Stretched Resolution. Al cerrarlo, la pantalla y la configuración se restauran.", "Se ponen al abrir Valorant y se quitan al cerrarlo, sin reiniciar. Cada juego guarda los suyos.", "Activos ahora: 3", "Ninguno elegido.", "1 elegido.", "4 elegidos.", "No se pudo aplicar: Índice de búsqueda en pausa", "Jugar", "Jugando", "Pantalla completa en la GPU.", "En el panel de NVIDIA, Ajustar tamaño y posición: Pantalla completa, escalar en la GPU.", "Anular el modo de los juegos", "En el panel de NVIDIA, Ajustar tamaño y posición: marca Anular el modo de escalado de juegos y programas.", "Abrir panel", "En AMD Software, Pantalla: activa Escalado de GPU y elige Panel completo.", "Abrir AMD Software", "Arreglar", "La gráfica no aceptó el cambio de escala.", "Cierra Valorant antes de cambiar sus ajustes.", "No toca la memoria del juego.", "El controlador NVIDIA devolvió el error 5.", "No se pudo cambiar: x" }) L.T(x);
                    foreach (ColorProfile x in Vivid.All) { L.T(x.Name); L.T(x.Note); }
                    foreach (Check c in Valorant.Checks(null, 2).Concat(Valorant.Checks(new Dictionary<string, string>(), 0)).Concat(Valorant.Checks(new Dictionary<string, string> { { "FullscreenMode", "1" }, { "bShouldLetterbox", "False" } }, 0))) { L.T(c.Title); L.T(c.Text); }
                    L.T(NetTest.Home(new NetResult { Router = new PingStats(), Internet = new PingStats(), Epic = new PingStats() }));
                    foreach (string x in FpsLog.ValMaps.Values.Where(x => x == "Campo de tiro")) L.T(x);
                    foreach (Game g in Game.All) foreach (string x in new[] { "Cierra {0} antes de cambiar de juego.", "Cierra {0} y vuelve a intentarlo.", "Cierra {0} primero: la resolución aún no se ha restaurado.", "Tweakeroo debe quedar abierto hasta cerrar {0} para restaurar la resolución.", "{0} cerrado: resolución restaurada.", "{0} no se abrió en 10 minutos: resolución restaurada.", "Abriendo {0}…", "Abrir {0}", "No se pudo abrir {0}: x", "No se pudieron leer los registros de {0}: x", "Se leen de los registros de {0}, segundo a segundo, sin modificar el juego.", "No hay partidas de más de un minuto en los registros de {0}.", "Se aplica al abrir {0}.", "Cierra {0} antes de medir la velocidad: la prueba llena la conexión.", "{0} no está instalado o no se encontró", "Si está en otra carpeta, elige su archivo {1}.", "Ese no es el archivo de {0}. Se llama {1}.", "{0} encontrado. Los tweaks del juego se actualizan al volver a abrir Tweakeroo." }) L.T(string.Format(x, g.Name, g.ExeName));
                    foreach (string x in new[] { "Colores", "Color: Carmesí", "Carmesí", "Estirar ahora", "Jugando", "Media de 10 partidas", "FPS estimados", "Abre Valorant y, si está activado, estira la pantalla al cargar un mapa. Al cerrarlo, todo se restaura.", "Abre Fortnite con la resolución elegida. Al cerrarlo, la pantalla y la configuración se restauran.", "Abriendo Riot Client: pulsa Jugar allí. La pantalla se estira al cargar un mapa; Tweakeroo debe quedar abierto.", "Abriendo Riot Client: pulsa Jugar allí.", "Algún monitor no se pudo desactivar: Windows no cambió el monitor (código 5).", "Pantalla estirada. Se restaura al cerrar Valorant.", "Pantalla restaurada.", "Tweakeroo debe quedar abierto durante la partida. Si la pantalla no se estira sola, el botón Estirar ahora lo hace a mano.", "No se encontró Riot Client. Abre Valorant una vez e inténtalo de nuevo.", "Windows no encontró el monitor (código 5).", "La gráfica no aceptó el color (código 5).", "La intensidad necesita una gráfica NVIDIA. ", "Windows no permite cambiar brillo, contraste y gamma en esta pantalla. Pasa con HDR o con el monitor desactivado en el Administrador de dispositivos.", "Ahora", "Ahora, intensidad 100 %", "Probando. Vuelve solo en unos segundos.", "Sin perfil: la pantalla no cambia.", "Windows no aceptó ese brillo. Prueba valores más cercanos a 50.", "No se pudo cambiar el color: x", "Se activa al abrir el juego.", "Activo ahora." }) L.T(x);
                    foreach (string x in new[] { "Tweaks", "Apagados", "Activados", "Nada activado en esta sección.", "Todo activado en esta sección." }) L.T(x);
                    foreach (Look lk in Theme.Looks) { L.T(lk.Name); L.T("Temática: " + lk.Name); }
                    string[] same = { "Tweakeroo", "Español", "English", "Fortnite", "Valorant", "FPS", "Stretched" };
                    List<string> miss = L.Missing.Where(x => !same.Contains(x)).ToList();
                    Is(miss.Count == 0, "sin traducir (" + miss.Count + "):\n" + string.Join("\n", miss));
                }
                finally { L.En = false; }

                // catalog
                List<Tweak> all = Catalog.Build();
                Is(all.Select(x => x.Id).Distinct().Count() == all.Count, "ids unicos");
                HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Tweak t in all)
                    foreach (Op o in t.Usable)
                        foreach (string k in o.AllKeys())
                        {
                            Is(keys.Add(k), "dos tweaks comparten " + k);
                            Is(o.DefaultFor(k) != o.Target, "por defecto igual al objetivo en " + t.Id);
                            o.Read(k);
                        }
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Fondos, ms por fotograma: " + sceneCost + ".");
                sb.AppendLine("Monitores activos: " + string.Join(", ", Monitors.On()) + ". Intensidad: " + Vivid.GetVib() + ". Curva: " + (Vivid.GetRamp() != null) + ". Valorant: " + string.Join(", ", Valorant.Checks(Valorant.Settings(), 0).Select(c => c.Title + " " + c.Ok)) + ". Exe: " + File.Exists(Valorant.Exe()));
                sb.AppendLine("Stretched aquí: " + string.Join(", ", Display.Stretched(Display.Modes(), Display.Saved()).Select(x => x.Name + "@" + x.Hz + (Display.Accepts(x) ? "" : " NO"))));
                List<Tweak> av = all.Where(x => x.Available).ToList();
                sb.AppendLine("OK. Catálogo " + all.Count + ", disponibles " + av.Count + ", ajustes " + keys.Count + ", prendidos " + av.Count(x => x.Read() == State.On) + ", parciales " + av.Count(x => x.Read() == State.Partial));
                sb.AppendLine("No disponibles: " + string.Join(", ", all.Where(x => !x.Available).Select(x => x.Id)));
                foreach (Tweak t in av) sb.AppendLine(t.Cat + "\t" + t.Id + "\t" + t.Read() + "\t" + string.Join(" ", t.Usable.SelectMany(o => o.CurrentKeys().Select(k => k + " = " + o.Read(k)))));
                return sb.ToString();
            }
            finally
            {
                IniOp.File = realIni;
                IniOp.GameRunning = realRunning;
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Root.Substring(5), false);
                foreach (string d in new[] { dir, dir + "2" }) if (Directory.Exists(d)) Directory.Delete(d, true);
            }
        }
    }
}
