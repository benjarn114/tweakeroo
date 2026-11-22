using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tablero
{
    // Autoprueba: usa una clave de usar y tirar, un .ini temporal y una carpeta temporal. No toca ajustes reales.
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

        public static string Run()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tablero-" + Guid.NewGuid().ToString("N"));
            string realIni = IniOp.File;
            Func<bool> realRunning = IniOp.GameRunning;
            try
            {
                // tipos: ida y vuelta, incluido "no existe"
                foreach (string v in new[] { "d:0", "d:4294967295", "q:18446744073709551615", "s:", "s:hola; mundo", "x:%TEMP%", "m:a\nb", "m:", "b:00FF10", Op.None })
                { RegOp.WriteValue(Root, "t", v); Is(V("t") == v, "tipo " + v); }

                // existente y ausente
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

                // parcial
                RegOp.WriteValue(Root, "a", "d:1");
                Is(t1.Read() == State.Partial, "parcial");
                e.Enable(t1);
                e.Disable(t1, true);
                Is(V("a") == "d:1" && V("b") == Op.None, "apagar todo respeta lo que ya estaba");
                e.Disable(t1, false);
                Is(V("a") == "d:0", "apagar suelto usa el valor de Windows");

                // restaurar
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

                // fallo a medias
                Tweak bad = Mk("bad", new RegOp(Root, "a", "d:1", "d:0"), new BadOp("c"));
                Engine e2 = new Engine(dir + "2", new[] { bad });
                bool threw = false;
                try { e2.Enable(bad); } catch (InvalidOperationException) { threw = true; }
                Is(threw && V("a") == "d:7" && V("c") == Op.None && !e2.Mine(bad), "fallo a medias se deshace");

                // token compartido
                RegOp.WriteValue(Root, "tok", "s:AutoHDREnable=0;SwapEffectUpgradeEnable=1;");
                TokenOp tk = new TokenOp(Root, "tok", "VRROptimizeEnable", "s:1", "s:0");
                tk.Write("", "s:1");
                Is(V("tok") == "s:AutoHDREnable=0;SwapEffectUpgradeEnable=1;VRROptimizeEnable=1;", "token agrega");
                tk.Write("", Op.None);
                Is(V("tok") == "s:AutoHDREnable=0;SwapEffectUpgradeEnable=1;", "token quita");

                // ini: conserva BOM, orden y lo demas
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

                // bit suelto
                RegOp.WriteValue(Root, "bit", "s:510");
                BitOp bo = new BitOp(Root, "bit", 4);
                bo.Write("", "d:0"); Is(V("bit") == "s:506" && bo.Read("") == "d:0", "bit apaga");
                bo.Write("", "d:1"); Is(V("bit") == "s:510", "bit vuelve");

                // un plan que ya no existe no bloquea
                SchemeOp so = new SchemeOp(Power.Active().ToString(), Power.Active().ToString());
                Is(so.Took("", "s:" + Guid.NewGuid()), "plan borrado cae al de respaldo");

                // paletas: texto legible y controles visibles
                foreach (Palette p in Theme.All)
                    foreach (string x in "Ink/Bg Ink/Rail Ink/Card Ink/CardHover Ink2/Bg Ink2/Rail Ink2/Card Ink2/CardHover CtaInk/Cta BtnInk/Btn BtnInk/BtnHover Danger/Card Danger/CardHover OnInk/On On/Card Cta/Bg On/Off Btn/Rail Btn/Card".Split(new[] { (char)32 }))
                    {
                        string[] q = x.Split(new[] { (char)47 });
                        double need = x == "On/Off" ? 1.5 : q[0] == "Btn" ? 1.25 : q[0] == "On" || x == "Cta/Bg" ? 3 : 4.5;
                        Is(Theme.Contrast(p, q[0], q[1]) >= need, "contraste " + p.Name + " " + x + " = " + Theme.Contrast(p, q[0], q[1]).ToString("0.00"));
                    }

                // consejero
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

                // stretched: solo mas estrechas que la pantalla y con su mejor refresco
                Res nat = new Res { W = 1920, H = 1080, Hz = 144 };
                List<Res> st = Display.Stretched(new[] { nat, new Res { W = 1440, H = 1080, Hz = 60 }, new Res { W = 1440, H = 1080, Hz = 144 }, new Res { W = 1600, H = 900, Hz = 144 }, new Res { W = 1280, H = 960, Hz = 60 }, new Res { W = 800, H = 600, Hz = 60 }, new Res { W = 2560, H = 1600, Hz = 60 } }, nat);
                Is(st.Count == 2 && st[0].Name == "1440 × 1080" && st[0].Hz == 144 && st[1].W == 1280, "resoluciones estiradas");

                // FPS desde el registro: 240 estables con un bajon, lobby y huecos fuera
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

                // estimacion y revision
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

                // ajustes: lo raro vuelve a lo normal, y exportar e importar no cuela nada
                Prefs wasP = Prefs.Now; int wasL = Fx.Level; bool lang = L.En;
                try
                {
                    Prefs pr = Prefs.Parse("{\"Page\":\"Otra\",\"Confirm\":999,\"Motion\":7,\"Tray\":true}");
                    Is(pr.Page == "Jugar" && pr.Confirm == 15 && pr.Motion == 0 && pr.Tray && !pr.AutoCheck, "ajustes raros");
                    Is(Prefs.Parse("{no").AutoCheck && Prefs.Parse("null").Confirm == 15, "ajustes ilegibles");
                    string a1 = Path.Combine(dir, "aj1"), a2 = Path.Combine(dir, "aj2"); Directory.CreateDirectory(a1); Directory.CreateDirectory(a2);
                    Prefs.Now = Prefs.Default(); Prefs.Now.Page = "Macros"; Prefs.Now.Confirm = 30; Prefs.Save(a1);
                    Prefs.Now = Prefs.Default(); Prefs.Load(a1); Is(Prefs.Now.Page == "Macros" && Prefs.Now.Confirm == 30, "guardar ajustes");
                    MacroConfig mm = Macros.Default(); mm.Accepted = true; mm.Keys["doble"] = 5; mm.On.Add("doble"); Macros.Save(a1, mm);
                    string ex = Prefs.Export(a1, "Ciruela");
                    Prefs.Now = Prefs.Default(); Prefs.Import(a2, ex);
                    MacroConfig im = Macros.Load(a2);
                    Is(Prefs.Now.Page == "Macros" && im.Keys["doble"] == 5 && !im.Accepted && File.ReadAllText(Path.Combine(a2, "paleta.txt")) == "Ciruela", "importar ajustes sin aceptar el riesgo");
                    foreach (string mal in new[] { "{}", "x", "null", "{\"App\":\"Otra\",\"Prefs\":{}}" })
                    {
                        bool no = false; try { Prefs.Import(a2, mal); } catch (InvalidDataException) { no = true; }
                        Is(no, "no importa: " + mal);
                    }
                    Prefs.Import(a2, "{\"App\":\"Tweakeroo\",\"Prefs\":{},\"Palette\":\"..\\\\x\"}"); Is(File.ReadAllText(Path.Combine(a2, "paleta.txt")) == "Ciruela" && Macros.Load(a2).On.Count == 0, "importar sin macros ni paleta valida");
                }
                finally { Prefs.Now = wasP; Fx.Level = wasL; L.En = lang; }
                // historial de versiones: la instalada sale una vez y todo esta en los dos idiomas
                List<VersionRow> vr = VersionRow.All();
                Is(vr.Count(v => v.Current) == 1 && vr.All(v => v.Missing == 0 && v.Notes.Count > 0 && v.Date.Length == 10) && vr.Select(v => Version.Parse(v.Version)).SequenceEqual(vr.Select(v => Version.Parse(v.Version)).OrderByDescending(v => v)), "historial de versiones");
                // borrar los FPS no deja que vuelvan de los registros
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
                    Is(FpsLog.Update(fd).Count == 1, "partida leida antes de borrar");
                    FpsLog.Clear(fd);
                    Is(FpsLog.Update(fd).Count == 0 && FpsLog.Update(fd).Count == 0, "lo borrado no vuelve");
                }
                finally { FpsLog.Folder = fl; }

                // macros: que mandan, y que solo lo hacen en Creativo con Fortnite delante
                MacroConfig mc = Macros.Default();
                Func<List<Act>, string> seq = x => string.Join(" ", x.Select(a => Macros.KeyName(a.Vk)[0] == 'C' ? (a.Vk == 1 ? "L" : "R") + (a.Down ? "+" : "-") : Macros.KeyName(a.Vk) + (a.Down ? "+" : "-")));
                Is(seq(Macros.Press(mc, "doble")) == "F+ F- L+" && seq(Macros.Release(mc, "doble")) == "L- F+ F- L+ L-", "doble con confirmar al soltar: " + seq(Macros.Release(mc, "doble")));
                Is(seq(Macros.Release(mc, "editreset")) == "L- F+ F- R+ R-" && seq(Macros.Press(mc, "reset")) == "F+ F- R+ R-" && Macros.Release(mc, "reset").Count == 0, "reset");
                mc.ConfirmOnRelease = false;
                Is(seq(Macros.Release(mc, "edit")) == "L- F+ F-" && seq(Macros.Release(mc, "doble")) == "L- F+ F- F+ F- L+ L- F+ F-" && seq(Macros.Press(mc, "reset")) == "F+ F- R+ R- F+ F-", "sin confirmar al soltar");
                foreach (string id in Macros.Ids) Is(Macros.Press(mc, id).Concat(Macros.Release(mc, id)).GroupBy(a => a.Vk).All(g => g.Count(a => a.Down) == g.Count(a => !a.Down)), "ninguna tecla queda pulsada: " + id);
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
                    mc = Macros.Default(); mc.Keys["doble"] = 5; mc.On.Add("doble"); mc.Delay = 5;
                    File.WriteAllText(lf, pre + "LogWorld: Bringing World /Delilah/Maps/D.D up for play\n");
                    Macros.Use(mc); System.Threading.Thread.Sleep(300); Is(Macros.Status == "Apagadas", "sin aceptar no arranca");
                    mc.Accepted = true; Macros.Use(mc); trig = true; System.Threading.Thread.Sleep(900); Is(sent.Count == 0 && Macros.Status.StartsWith("En espera"), "en ranked no manda nada");
                    trig = false; File.AppendAllText(lf, pre + "LogWorld: Bringing World /VKPlay/Maps/X.X up for play\n"); System.Threading.Thread.Sleep(900);
                    front = false; System.Threading.Thread.Sleep(600); trig = true; System.Threading.Thread.Sleep(500); Is(sent.Count == 0, "sin Fortnite delante no manda nada"); trig = false; front = true; System.Threading.Thread.Sleep(900);
                    trig = true; System.Threading.Thread.Sleep(300); trig = false; System.Threading.Thread.Sleep(300);
                    lock (sent) Is(Macros.Status == "Activas" && seq(sent) == "F+ F- L+ L- F+ F- L+ L-", "doble edicion en creativo: " + seq(sent));
                    trig = true; System.Threading.Thread.Sleep(300); File.AppendAllText(lf, pre + "LogLoad: LoadMap: /Game/Maps/Frontend\n"); System.Threading.Thread.Sleep(900);
                    lock (sent) Is(!sent[sent.Count - 1].Down && sent[sent.Count - 1].Vk == 1 && sent.Count(a => a.Down) == sent.Count(a => !a.Down), "al salir de creativo suelta el clic");
                    File.WriteAllText(Path.Combine(dir, "macros.json"), "{roto"); Is(Macros.Load(dir).On.Count == 0, "macros ilegibles");
                }
                finally { Macros.Stop(); FpsLog.Folder = logs; Macros.Send = send; Macros.IsDown = dn; Macros.Front = frontReal; }

                // pedir y soltar el temporizador
                Is(Precise.Set(true) && Precise.Set(true) && Precise.Set(false), "temporizador de 0,5 ms");

                // el escaneo guardado vuelve igual; si falta algo o no se lee, no vale
                ScanData sd = new ScanData { Hw = new Hw { Cpu = "c", Gpu = "g", Cores = 8, Ethernet = true, SecureBoot = true, DriverDate = new DateTime(2026, 5, 4, 1, 0, 0) }, Text = Scanner.Steps.ToDictionary(s => s.Key, s => s.Key + "\nx") };
                Is(Scanner.Load(dir) == null, "sin escaneo guardado");
                Scanner.Save(dir, sd);
                ScanData sl = Scanner.Load(dir);
                Is(sl != null && sl.Hw.Cpu == "c" && sl.Hw.Cores == 8 && sl.Hw.Ethernet && sl.Hw.SecureBoot == true && sl.Hw.Tpm2 == null && sl.Hw.DriverDate == sd.Hw.DriverDate && sl.Text["Gpu"] == "Gpu\nx", "escaneo guardado");
                sd.Hw.DriverDate = DateTime.MinValue; Scanner.Save(dir, sd); Is(Scanner.Load(dir).Hw.DriverDate == DateTime.MinValue, "driver sin fecha");
                sd.Text.Remove("Net"); Scanner.Save(dir, sd); Is(Scanner.Load(dir) == null, "escaneo a medias");
                File.WriteAllText(Path.Combine(dir, "scan.json"), "{no es json"); Is(Scanner.Load(dir) == null, "escaneo ilegible");

                // actualizaciones: solo firmadas, mas nuevas, por https y con el hash correcto
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
                        Release rel = new Release { Version = "3.1.0", Notes = new List<string> { "ñ" }, Url = "https://x/y.exe", Sha256 = sha };
                        string good = sign(rel);
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

                // instalar y quitar en carpetas de usar y tirar
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

                // plan de compras
                List<Check> pl = Review.Plan(my, new List<Match> { new Match { Median = 383, Cap = 0 } });
                Is(pl[0].Title.StartsWith("Gráfica: RTX") && pl[0].Text.StartsWith("Es el cuello de botella") && pl.Any(c => c.Title.StartsWith("Procesador: Ryzen 7 7800X3D")), "plan: " + string.Join("; ", pl.Select(c => c.Title)));
                my.Hz = my.MaxHz = 200; Is(Review.Plan(my, new List<Match> { new Match { Median = 383, Cap = 0 } }).Any(c => c.Title == "Monitor de 360 Hz"), "plan monitor");

                // ingles: nada se queda en español
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
                    foreach (string x in new[] { "Todas", "Sin rebotes", "Apagadas", "Jugar", "Borrar historial de FPS", "Borrar escaneo", "Volver al estado Original", "Restablecer Ajustes", "Pulsa otra vez para confirmar", "Escaneo borrado. Se pedirá otra vez al volver a abrir.", "El PC ya está en el estado Original.", "Ajustes restablecidos.", "Datos copiados. No incluyen nada personal.", "Historial de FPS borrado.", "Ajustes exportados. No incluyen los tweaks.", "No se pudo importar: El archivo no es una copia de ajustes de Tweakeroo.", "Es la versión más reciente.", "No hay dónde buscar actualizaciones.", "Historial de versiones", "Términos y privacidad", "Abrir", "Salir", "La primera vez que se usa una resolución se pide confirmación. Sin respuesta en 30 segundos, se restaura la anterior. Tweakeroo debe quedar abierto durante la partida. En torneos oficiales Epic puede forzar 16:9." }) L.T(x);
                    string[] same = { "Tweakeroo", "Español", "English", "Fortnite", "FPS" };
                    List<string> miss = L.Missing.Where(x => !same.Contains(x)).ToList();
                    Is(miss.Count == 0, "sin traducir (" + miss.Count + "):\n" + string.Join("\n", miss));
                }
                finally { L.En = false; }

                // catalogo
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
