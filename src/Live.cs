using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Web.Script.Serialization;

namespace Tablero
{
    // Applies right away what Windows only reads at sign-in.
    public static class Live
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)] static extern bool Spi(uint action, uint p, IntPtr v, uint ini);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)] static extern bool Spi(uint action, uint p, [In, Out] int[] v, uint ini);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeoutW(IntPtr w, uint msg, IntPtr a, string b, uint flags, uint ms, out IntPtr r);

        public static void Tell(string area) { IntPtr r; SendMessageTimeoutW((IntPtr)0xffff, 0x1A, IntPtr.Zero, area, 2, 300, out r); }
        static int Reg(string path, string name) { string s = RegOp.ReadValue(path, name); int v; return s != Op.None && int.TryParse(s.Substring(2), out v) ? v : -1; }

        public static bool GetFlag(uint get) { int[] v = { 0 }; Spi(get, 0, v, 0); return v[0] != 0; }
        public static void SetFlag(uint set, bool on) { Spi(set, 0, on ? (IntPtr)1 : IntPtr.Zero, 0); }
        public static bool Drag { get { return GetFlag(0x26); } set { Spi(0x25, value ? 1u : 0u, IntPtr.Zero, 0); } }
        public static bool WindowAnim { get { int[] v = { 8, 0 }; Spi(0x48, 8, v, 0); return v[1] != 0; } set { Spi(0x49, 8, new[] { 8, value ? 1 : 0 }, 0); } }
        public static int[] Mouse { get { int[] v = new int[3]; Spi(3, 0, v, 0); return v; } set { Spi(4, 0, value, 0); } }
        // FILTERKEYS has more fields than the others: read whole and change only what is needed.
        static int Size(uint get) { return get == 0x32 ? 24 : get == 0x36 ? 28 : 8; }
        public static int Keys(uint get) { int[] v = new int[Size(get) / 4]; v[0] = Size(get); Spi(get, (uint)v[0], v, 0); return v[1]; }
        public static void Keys(uint get, int flags) { int[] v = new int[Size(get) / 4]; v[0] = Size(get); if (!Spi(get, (uint)v[0], v, 0) || v[1] == flags) return; v[1] = flags; Spi(get + 1, (uint)v[0], v, 0); }
        public const uint Sticky = 0x3A, Filter = 0x32, Toggle = 0x34, MouseKeys = 0x36;
        // Bit 4: the keyboard shortcut that opens the prompt.
        static void Shortcut(uint get, string key)
        {
            int flags = Reg(@"HKCU\Control Panel\Accessibility\" + key, "Flags");
            if (flags >= 0) Keys(get, flags & 4 | Keys(get) & ~4);
        }

        public static readonly Dictionary<string, Action> Apply = new Dictionary<string, Action> {
            { "in-accel", () => { int a = Reg(@"HKCU\Control Panel\Mouse", "MouseThreshold1"), b = Reg(@"HKCU\Control Panel\Mouse", "MouseThreshold2"), c = Reg(@"HKCU\Control Panel\Mouse", "MouseSpeed"); if (a >= 0 && b >= 0 && c >= 0) Mouse = new[] { a, b, c }; } },
            { "in-sticky", () => Shortcut(Sticky, "StickyKeys") }, { "in-filter", () => Shortcut(Filter, "Keyboard Response") }, { "in-toggle", () => Shortcut(Toggle, "ToggleKeys") }, { "in-mousekeys", () => Shortcut(MouseKeys, "MouseKeys") },
            { "in-delay", () => { int v = Reg(@"HKCU\Control Panel\Keyboard", "KeyboardDelay"); if (v >= 0) Spi(0x17, (uint)v, IntPtr.Zero, 0); } },
            { "in-speed", () => { int v = Reg(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed"); if (v >= 0) Spi(0x0B, (uint)v, IntPtr.Zero, 0); } },
            { "ui-anim", () => { int v = Reg(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate"); if (v >= 0) WindowAnim = v != 0; } },
            { "ui-menu", () => { int v = Reg(@"HKCU\Control Panel\Desktop", "MenuShowDelay"); if (v >= 0) Spi(0x6B, (uint)v, IntPtr.Zero, 0); } },
            { "ui-transp", () => Tell("ImmersiveColorSet") } };
        public static void After(Tweak t)
        {
            Action a;
            if (!Apply.TryGetValue(t.Id, out a)) return;
            try { a(); }
            catch (Exception) { } // already saved: applied on next sign-in
        }
    }

    // What really needs a restart: only what changed after the last boot.
    public class Pending
    {
        readonly string file;
        Dictionary<string, long> at = new Dictionary<string, long>();
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        // Replaceable in tests.
        public static Func<DateTime> Boot = () => DateTime.UtcNow.AddMilliseconds(-(double)GetTickCount64());
        public static Func<string, DateTime?> Started = name =>
        {
            DateTime? r = null;
            int session = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcessesByName(name))
                try { if (p.SessionId == session && (r == null || p.StartTime.ToUniversalTime() < r)) r = p.StartTime.ToUniversalTime(); }
                catch (Exception) { }
            return r;
        };

        public Pending(string dir)
        {
            file = Path.Combine(dir, "pendiente.json");
            try { if (File.Exists(file)) at = new JavaScriptSerializer().Deserialize<Dictionary<string, long>>(File.ReadAllText(file)) ?? at; }
            catch (Exception) { }
        }
        void Save() { try { Engine.WriteFile(file, new JavaScriptSerializer().Serialize(at)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        static bool After(DateTime when, string process) { DateTime? s = Started(process); return s != null && s < when; }
        static bool Waiting(Tweak t, DateTime when)
        {
            switch (t.Restart)
            {
                case "": return false;
                case "la sesión": return After(when, "explorer");
                case "el juego": return Game.All.Any(g => (t.Game == null || t.Game == g.Id) && g.Running());
                case "Edge": return After(when, "msedge");
                case "Chrome": return After(when, "chrome");
                case "Brave": return After(when, "brave");
                default: return Boot() < when;
            }
        }
        public bool Need(Tweak t)
        {
            long v;
            if (!at.TryGetValue(t.Id, out v)) return false;
            if (Waiting(t, new DateTime(v, DateTimeKind.Utc))) return true;
            at.Remove(t.Id);
            Save();
            return false;
        }
        // Changing and reverting before restarting leaves everything as it was.
        public void Change(Tweak t)
        {
            if (Need(t)) at.Remove(t.Id);
            else if (Waiting(t, DateTime.UtcNow)) at[t.Id] = DateTime.UtcNow.Ticks;
            Save();
        }
        // The most important of the ones needed, for a single warning.
        public string Ask(IEnumerable<Tweak> all)
        {
            List<string> k = all.Where(Need).Select(t => t.Restart).Distinct().ToList();
            return k.Contains("el PC") ? " Reinicia el PC para completar los cambios." : k.Contains("la sesión") ? " Cierra sesión y vuelve a entrar para completar los cambios." : k.Contains("el juego") ? " Reinicia el juego para completar los cambios." : k.Count > 0 ? " Reinicia " + k[0] + " para que haga efecto." : "";
        }
    }

    // Changes that only last while playing. All are applied and removed without restarting.
    public class Boost
    {
        public string Id, Name, Desc;
        public Func<string> On;        // returns what is needed to revert
        public Action<string> Off;

        static Boost Flag(string id, string name, string desc, uint get, uint set)
        {
            return new Boost { Id = id, Name = name, Desc = desc, On = () => { string old = Live.GetFlag(get) ? "1" : "0"; Live.SetFlag(set, false); return old; }, Off = old => Live.SetFlag(set, old == "1") };
        }
        static Boost Service(string id, string name, string desc, params string[] services)
        {
            return new Boost { Id = id, Name = name, Desc = desc,
                On = () =>
                {
                    List<string> stopped = new List<string>();
                    foreach (string s in services)
                        try { using (ServiceController c = new ServiceController(s)) if (c.Status == ServiceControllerStatus.Running && c.CanStop) { c.Stop(); stopped.Add(s); } }
                        catch (InvalidOperationException) { }
                    return string.Join(",", stopped);
                },
                Off = old =>
                {
                    foreach (string s in old.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        try { using (ServiceController c = new ServiceController(s)) if (c.Status == ServiceControllerStatus.Stopped) c.Start(); }
                        catch (InvalidOperationException) { }
                } };
        }
        const string Personalize = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", HighPlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        public static List<Boost> All = new List<Boost> {
            new Boost { Id = "plan", Name = "Plan Alto rendimiento", Desc = "La CPU sube de frecuencia sin esperar; al cerrar vuelve el plan de antes.",
                On = () => { Guid now = Power.Active(), high = new Guid(HighPlan); if (now == high || !Power.Schemes().Contains(high)) return ""; Power.Activate(high); return now.ToString(); },
                Off = old => { if (old.Length > 0 && Power.Schemes().Contains(new Guid(old)) && Power.Active() == new Guid(HighPlan)) Power.Activate(new Guid(old)); } },
            new Boost { Id = "fondo", Name = "Navegadores y lanzadores en modo eficiencia", Desc = "Chrome, Edge, Steam, Epic, Spotify y similares ceden la CPU al juego. No se cierran.", On = () => { Eco.Set(true); return ""; }, Off = old => Eco.Set(false) },
            Service("windowsupdate", "Windows Update en pausa", "Sin descargas ni instalaciones a mitad de partida.", "wuauserv", "BITS", "DoSvc"),
            Service("indice", "Índice de búsqueda en pausa", "Sin lecturas de disco de la búsqueda de Windows.", "WSearch"),
            Service("sysmain", "SysMain en pausa", "Sin precarga de programas en memoria.", "SysMain"),
            Service("diag", "Diagnóstico en pausa", "Sin recogida de datos de Windows.", "DiagTrack", "DPS", "PcaSvc"),
            new Boost { Id = "raton", Name = "Ratón sin aceleración", Desc = "Solo mientras se juega; en el escritorio queda como esté.", On = () => { int[] m = Live.Mouse; Live.Mouse = new[] { 0, 0, 0 }; return string.Join(",", m); }, Off = old => Live.Mouse = old.Split(',').Select(int.Parse).ToArray() },
            new Boost { Id = "teclas", Name = "Sin avisos de Shift", Desc = "Pulsar o mantener Shift no abre las Teclas especiales ni las de filtro.",
                On = () => { int a = Live.Keys(Live.Sticky), b = Live.Keys(Live.Filter), c = Live.Keys(Live.Toggle); Live.Keys(Live.Sticky, a & ~4); Live.Keys(Live.Filter, b & ~4); Live.Keys(Live.Toggle, c & ~4); return a + "," + b + "," + c; },
                Off = old => { int[] v = old.Split(',').Select(int.Parse).ToArray(); Live.Keys(Live.Sticky, v[0]); Live.Keys(Live.Filter, v[1]); Live.Keys(Live.Toggle, v[2]); } },
            new Boost { Id = "anim", Name = "Sin animaciones de ventanas", Desc = "Cambiar de ventana es instantáneo.", On = () => { string o = Live.WindowAnim ? "1" : "0"; Live.WindowAnim = false; return o; }, Off = old => Live.WindowAnim = old == "1" },
            Flag("controles", "Sin animaciones de controles", "Menús, listas y botones sin animación.", 0x1042, 0x1043),
            new Boost { Id = "transp", Name = "Sin transparencias", Desc = "Barra y menús opacos: menos trabajo para la gráfica.",
                On = () => { string o = RegOp.ReadValue(Personalize, "EnableTransparency"); RegOp.WriteValue(Personalize, "EnableTransparency", "d:0"); Live.Tell("ImmersiveColorSet"); return o; },
                Off = old => { RegOp.WriteValue(Personalize, "EnableTransparency", old); Live.Tell("ImmersiveColorSet"); } },
            Flag("sombras", "Sin sombras de ventanas", "Escritorio más ligero detrás del juego.", 0x1024, 0x1025),
            new Boost { Id = "arrastre", Name = "Arrastrar ventanas sin contenido", Desc = "Solo se ve el borde al moverlas.", On = () => { string o = Live.Drag ? "1" : "0"; Live.Drag = false; return o; }, Off = old => Live.Drag = old == "1" } };
    }

    // Windows efficiency mode for background programs.
    public static class Eco
    {
        static readonly string[] Names = { "chrome", "msedge", "brave", "firefox", "opera", "vivaldi", "steamwebhelper", "steam", "EpicGamesLauncher", "EpicWebHelper", "Spotify", "OneDrive", "Teams", "ms-teams", "WhatsApp", "GalaxyClient", "EADesktop", "Battle.net" };
        [StructLayout(LayoutKind.Sequential)] struct Throttle { public uint Version, Control, State; }
        [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process, int kind, ref Throttle t, int size);
        static readonly Dictionary<int, ProcessPriorityClass> changed = new Dictionary<int, ProcessPriorityClass>();
        static bool on;
        static void One(Process p, bool eco)
        {
            Throttle t = new Throttle { Version = 1, Control = eco ? 1u : 0u, State = eco ? 1u : 0u };
            SetProcessInformation(p.Handle, 4, ref t, Marshal.SizeOf(t));
        }
        public static void Set(bool want)
        {
            on = want;
            if (want) { Keep(); return; }
            foreach (KeyValuePair<int, ProcessPriorityClass> x in changed.ToList())
                try { using (Process p = Process.GetProcessById(x.Key)) if (Names.Contains(p.ProcessName)) { One(p, false); p.PriorityClass = x.Value; } }
                catch (Exception) { } // already closed
            changed.Clear();
        }
        // Those opened mid-match too.
        public static void Keep()
        {
            if (!on) return;
            foreach (Process p in Names.SelectMany(Process.GetProcessesByName))
                try
                {
                    if (changed.ContainsKey(p.Id)) continue;
                    ProcessPriorityClass was = p.PriorityClass;
                    One(p, true);
                    if (was == ProcessPriorityClass.Normal) p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    changed[p.Id] = was;
                }
                catch (Exception) { } // protected or already closed
        }
    }

    public class BoostSession
    {
        readonly string file;
        Dictionary<string, string> old = new Dictionary<string, string>();
        public BoostSession(string dir)
        {
            file = Path.Combine(dir, "aljugar.json");
            try { if (File.Exists(file)) old = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? old; }
            catch (Exception) { }
        }
        public int Count { get { return old.Count; } }
        void Save() { Directory.CreateDirectory(Path.GetDirectoryName(file)); Engine.WriteFile(file, new JavaScriptSerializer().Serialize(old)); }
        // Leaves exactly the requested ones on. Returns those that failed.
        public List<string> Use(ICollection<string> ids)
        {
            List<string> bad = new List<string>();
            foreach (Boost b in Boost.All)
            {
                bool want = ids.Contains(b.Id), has = old.ContainsKey(b.Id);
                if (want == has) continue;
                try
                {
                    if (want) { old[b.Id] = b.On(); Save(); }
                    else { b.Off(old[b.Id]); old.Remove(b.Id); Save(); }
                }
                catch (Exception) { bad.Add(b.Name); if (!want) { old.Remove(b.Id); Save(); } }
            }
            return bad;
        }
    }

    public class BoostRow : Vm
    {
        public string Id;
        public string Name { get; set; }
        public string Desc { get; set; }
        public bool On { get; set; }
        public void Set(bool on) { On = on; Raise("On"); }
    }

    public class DriverInfo
    {
        public string Version { get; set; }
        public string Date { get; set; }
        public string Url { get; set; }
        public string Seen { get; set; }   // when it was asked
    }

    // Latest NVIDIA driver. Only the GPU family is sent.
    public static class Drivers
    {
        public static DriverInfo Known;
        public static Func<string, string> Download = url =>
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            System.Net.HttpWebRequest r = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            r.UserAgent = "Tweakeroo"; r.Timeout = r.ReadWriteTimeout = 8000;
            using (System.Net.WebResponse a = r.GetResponse())
            using (StreamReader s = new StreamReader(a.GetResponseStream()))
            {
                char[] b = new char[1 << 18];
                return new string(b, 0, s.ReadBlock(b, 0, b.Length));
            }
        };
        static string FileOf(string dir) { return Path.Combine(dir, "driver.json"); }
        public static void Load(string dir)
        {
            try { Known = File.Exists(FileOf(dir)) ? Clean(new JavaScriptSerializer().Deserialize<DriverInfo>(File.ReadAllText(FileOf(dir)))) : null; }
            catch (Exception) { Known = null; }
        }
        static DriverInfo Clean(DriverInfo d)
        {
            Version v;
            return d == null || d.Version == null || !Version.TryParse(d.Version, out v) || d.Url == null || !d.Url.StartsWith("https://www.nvidia.com/", StringComparison.Ordinal) ? null : d;
        }
        // 32.0.16.2114 is 621.14
        public static string Installed(string raw)
        {
            string d = new string((raw ?? "").Where(char.IsDigit).ToArray());
            return d.Length < 5 || (raw ?? "").Count(c => c == '.') != 3 ? "" : d.Substring(d.Length - 5, 3) + "." + d.Substring(d.Length - 2);
        }
        static string Family(string gpu)
        {
            // Series and product from the NVIDIA lookup: GTX 900 and 1000 have their own branch.
            if (System.Text.RegularExpressions.Regex.IsMatch(gpu, @"\b(GTX (9|10)\d\d|GT 10\d0)\b")) return "psfid=101&pfid=845";
            return System.Text.RegularExpressions.Regex.IsMatch(gpu, @"\b(RTX \d{4}|GTX 16\d\d)\b") ? "psfid=120&pfid=929" : null;
        }
        public static DriverInfo Parse(string json)
        {
            Func<string, string> get = k => { System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(json, "\"" + k + "\"\\s*:\\s*\"([^\"]*)\""); return m.Success ? m.Groups[1].Value : null; };
            DateTime d;
            DriverInfo r = Clean(new DriverInfo { Version = get("Version"), Url = get("DetailsURL"), Seen = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
            if (r != null && DateTime.TryParse(get("ReleaseDateTime"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) r.Date = d.ToString("dd/MM/yyyy");
            return r;
        }
        public static bool Fresh { get { DateTime d; return Known != null && DateTime.TryParse(Known.Seen, out d) && (DateTime.Now - d).TotalHours < 24; } }
        public static void Check(string dir, Hw h)
        {
            string f = h == null || !h.Nvidia ? null : Family(h.Gpu ?? "");
            if (f == null) return;
            try
            {
                DriverInfo d = Parse(Download("https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&" + f + "&osID=" + (Environment.OSVersion.Version.Build >= 22000 ? 135 : 57) + "&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=0&qnf=0&sort1=0&numberOfResults=1"));
                if (d == null) return;
                Known = d;
                Engine.WriteFile(FileOf(dir), new JavaScriptSerializer().Serialize(d));
            }
            catch (Exception) { } // without internet the last known value is kept
        }
        // null = unknown.
        public static Check Advice(Hw h)
        {
            string now = Installed(h.Driver);
            if (!h.Nvidia || now.Length == 0 || Known == null) return null;
            bool old = Version.Parse(now) < Version.Parse(Known.Version);
            string date = Known.Date == null ? "" : ", del " + Known.Date;
            return old ? new Check { Kind = Kind.Gratis, Title = "Driver de NVIDIA: hay una versión nueva", Text = "Instalada: " + now + ". Última: " + Known.Version + date + ".", Url = Known.Url }
                : new Check { Kind = Kind.Bien, Title = "Driver de NVIDIA al día", Text = "Versión " + now + "." };
        }
    }
}
