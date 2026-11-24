using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Tablero
{
    // Each game has its palette, photo, resolution, FPS and tweaks. The rest is shared.
    public class Game
    {
        public string Id, Photo, Palette, PaletteFile;
        public string Name { get; set; }
        public Func<bool> Running;   // replaceable in tests
        public Func<bool> Installed; // replaceable in tests
        public string ExeName;
        Func<string> found;
        string slot;
        // Name in the top selector: a long one goes on two lines.
        public string SlotName { get { return slot ?? Name; } }

        public static readonly Game Fortnite = new Game { Id = "fortnite", Name = "Fortnite", Photo = "personaje.jpg", Palette = "Glaciar", PaletteFile = "paleta.txt", Running = () => IniOp.GameRunning(), ExeName = "FortniteClient-Win64-Shipping.exe", found = FortniteExe, Installed = () => File.Exists(Fortnite.Exe) };
        public static readonly Game Valorant = new Game { Id = "valorant", Name = "Valorant", Photo = "valorant.jpg", Palette = "Carmesí", PaletteFile = "paleta-valorant.txt", Running = () => Process.GetProcessesByName("VALORANT-Win64-Shipping").Length > 0, ExeName = "VALORANT-Win64-Shipping.exe", found = Tablero.Valorant.Exe, Installed = () => File.Exists(Valorant.Exe) };
        public static readonly Game Cs2 = new Game { Id = "cs2", Name = "Counter-Strike 2", slot = "Counter-Strike\n2", Photo = "cs2.jpg", Palette = "CS2", PaletteFile = "paleta-cs2.txt", Running = () => Process.GetProcessesByName("cs2").Length > 0, ExeName = "cs2.exe", found = Tablero.Cs2.Exe, Installed = () => File.Exists(Cs2.Exe) };
        public static readonly List<Game> All = new List<Game> { Fortnite, Valorant, Cs2 };
        // The ones shown in the selector.
        public static List<Game> Visible { get { return All.Where(g => !Prefs.Now.Hidden.Contains(g.Id)).ToList(); } }
        // The 2 slots at the top: the pinned ones that are visible, in their order, filled up with the rest in the order of All.
        public static List<Game> Slots(List<string> pinned, List<Game> visible)
        {
            List<Game> s = (pinned ?? new List<string>()).Select(id => visible.FirstOrDefault(g => g.Id == id)).Where(g => g != null).Distinct().Take(2).ToList();
            foreach (Game g in visible) if (s.Count < 2 && !s.Contains(g)) s.Add(g);
            return s;
        }
        // The one picked by hand wins over the one found automatically.
        public string Exe { get { string p; return Prefs.Now.Paths.TryGetValue(Id, out p) ? p : found(); } }
        public bool IsExe(string path) { return path != null && path.EndsWith("\\" + ExeName, StringComparison.OrdinalIgnoreCase); }
        static string FortniteExe()
        {
            string dat = Environment.ExpandEnvironmentVariables(@"%ProgramData%\Epic\UnrealEngineLauncher\LauncherInstalled.dat");
            string dir = @"C:\Program Files\Epic Games\Fortnite";
            try
            {
                System.Text.RegularExpressions.Match m = File.Exists(dat) ? Regex.Match(File.ReadAllText(dat), "\"InstallLocation\":\\s*\"([^\"]*\\\\Fortnite)\"") : null;
                if (m != null && m.Success) dir = m.Groups[1].Value.Replace("\\\\", "\\");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Path.Combine(dir, @"FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe");
        }
        public static Game Of(string id) { return All.FirstOrDefault(g => g.Id == id) ?? Fortnite; }
        public static Game Open { get { return All.FirstOrDefault(g => g.Running()); } }
        public StretchSession Session(string dir) { return this == Valorant ? new ValorantSession(dir) : this == Cs2 ? new Cs2Session(dir) : new StretchSession(dir); }

        // null = for all games.
        public static string OfTweak(string id)
        {
            if (id.StartsWith("val-")) return "valorant";
            if (id.StartsWith("cs2-")) return "cs2";
            return id.StartsWith("fn-") || id == "nv-power" || id == "nv-filter" || id == "net-qos" ? "fortnite" : null;
        }
    }

    // Monitors from Device Manager. Without CM_DISABLE_PERSIST: restarting the PC re-enables them.
    public static class Monitors
    {
        const string Class = "{4d36e96e-e325-11ce-bfc1-08002be10318}";
        const int FilterClass = 0x200, FilterPresent = 0x100, HasProblem = 0x400, Disabled = 22, NoUi = 4;
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_List_SizeW(out int len, string filter, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, int len, int flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out uint node, string id, int flags);
        [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Status(out int status, out int problem, uint node, int flags);
        [DllImport("cfgmgr32.dll")] static extern int CM_Disable_DevNode(uint node, int flags);
        [DllImport("cfgmgr32.dll")] static extern int CM_Enable_DevNode(uint node, int flags);

        static uint Node(string id)
        {
            uint n;
            int e = CM_Locate_DevNodeW(out n, id, 0);
            if (e != 0) throw new InvalidOperationException("Windows no encontró el monitor (código " + e + ").");
            return n;
        }
        static List<string> Active()
        {
            int len;
            if (CM_Get_Device_ID_List_SizeW(out len, Class, FilterClass | FilterPresent) != 0 || len < 2) return new List<string>();
            char[] b = new char[len];
            if (CM_Get_Device_ID_ListW(Class, b, len, FilterClass | FilterPresent) != 0) return new List<string>();
            return new string(b).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries).Where(id =>
            {
                int status, problem;
                return CM_Get_DevNode_Status(out status, out problem, Node(id), 0) == 0 && !((status & HasProblem) != 0 && problem == Disabled);
            }).ToList();
        }
        static void Change(string id, bool on)
        {
            int e = on ? CM_Enable_DevNode(Node(id), 0) : CM_Disable_DevNode(Node(id), NoUi);
            if (e != 0) throw new InvalidOperationException("Windows no cambió el monitor (código " + e + ").");
        }
        // Replaceable in tests.
        public static Func<List<string>> On = Active;
        public static Action<string, bool> Set = Change;
    }

    // NVIDIA scaling of the main display (panel: Adjust desktop size and position).
    // 2 = Full screen + scale on the GPU (NV_SCALING_GPU_SCALING_TO_NATIVE). Saved per display, not per resolution.
    public static class NvScale
    {
        public const int GpuFull = 2;
        const int PathSize = 48, TargetSize = 24, SourceSize = 32, DetailSize = 128, Persist = 2;
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Query(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FGet(ref uint count, IntPtr paths);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FSet(uint count, IntPtr paths, uint flags);
        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = Query(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("El controlador NVIDIA no ofrece esta función.");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        static void Check(int e) { if (e != 0) throw new InvalidOperationException("El controlador NVIDIA devolvió el error " + e + "."); }
        static IntPtr Zeroed(int size, List<IntPtr> owned) { IntPtr p = Marshal.AllocHGlobal(size); owned.Add(p); for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0); return p; }

        // NvAPI_DISP_GetDisplayConfig in three passes; body receives the table and the path of the Windows main display.
        static TR With<TR>(Func<IntPtr, uint, IntPtr, TR> body)
        {
            Check(Fn<FInit>(0x0150E828)());
            FGet get = Fn<FGet>(0x11ABCCF8);
            List<IntPtr> owned = new List<IntPtr>();
            try
            {
                uint n = 0;
                Check(get(ref n, IntPtr.Zero));
                IntPtr paths = Zeroed(PathSize * (int)n, owned);
                for (int i = 0; i < n; i++) Marshal.WriteInt32(paths, i * PathSize, PathSize | 2 << 16);
                Check(get(ref n, paths));
                for (int i = 0; i < n; i++)
                {
                    IntPtr path = paths + i * PathSize;
                    int count = Marshal.ReadInt32(path, 8);
                    IntPtr targets = Zeroed(TargetSize * count, owned);
                    for (int j = 0; j < count; j++)
                    {
                        IntPtr d = Zeroed(DetailSize, owned);
                        Marshal.WriteInt32(d, 0, DetailSize | 1 << 16);
                        Marshal.WriteIntPtr(targets, j * TargetSize + 8, d);
                    }
                    Marshal.WriteIntPtr(path, 16, targets);
                    Marshal.WriteIntPtr(path, 24, Zeroed(SourceSize, owned));
                }
                Check(get(ref n, paths));
                for (int i = 0; i < n; i++)
                {
                    IntPtr path = paths + i * PathSize;
                    if ((Marshal.ReadInt32(Marshal.ReadIntPtr(path, 24), 28) & 1) != 0) return body(paths, n, path); // bGDIPrimary
                }
                throw new InvalidOperationException("No se encontró la pantalla principal.");
            }
            finally { foreach (IntPtr p in owned) Marshal.FreeHGlobal(p); }
        }
        static IntPtr Details(IntPtr path) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(path, 16), 8); }

        static int Read() { return With((paths, n, primary) => Marshal.ReadInt32(Details(primary), 8)); }
        // Only changes the scaling; the rest is passed back as is. The display flickers for a moment.
        static void Write(int scaling)
        {
            With((paths, n, primary) =>
            {
                for (int j = 0; j < Marshal.ReadInt32(primary, 8); j++)
                    Marshal.WriteInt32(Marshal.ReadIntPtr(Marshal.ReadIntPtr(primary, 16), j * TargetSize + 8), 8, scaling);
                Check(Fn<FSet>(0x5D8CF8DE)(n, paths, Persist));
                return true;
            });
        }
        // Replaceable in tests.
        public static Func<int> Get = Read;
        public static Action<int> Set = Write;

        // The previous value is recorded only once, so the original can be restored by hand.
        public static void Fix(string dir)
        {
            int old = Get();
            string f = Path.Combine(dir, "escalado.json");
            if (!File.Exists(f)) { Directory.CreateDirectory(dir); File.WriteAllText(f, "{ \"Old\": " + old + " }"); }
            Set(GpuFull);
            if (Get() != GpuFull) throw new InvalidOperationException("La gráfica no aceptó el cambio de escala.");
        }
    }

    // Valorant: its files are read and only its video setting is written when pressing Fix, with the game closed. The game itself is never touched.
    public static class Valorant
    {
        public static string Saved = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\VALORANT\Saved");
        public static string Logs { get { return Path.Combine(Saved, "Logs"); } }
        static readonly string Riot = Environment.ExpandEnvironmentVariables(@"%ProgramData%\Riot Games");

        static string ReadShared(string file)
        {
            using (StreamReader r = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))) return r.ReadToEnd();
        }

        public static string Exe()
        {
            string root = @"C:\Riot Games\VALORANT\live", f = Path.Combine(Riot, @"Metadata\valorant.live\valorant.live.product_settings.yaml");
            try
            {
                System.Text.RegularExpressions.Match m = File.Exists(f) ? Regex.Match(File.ReadAllText(f), "product_install_full_path:\\s*\"([^\"]+)\"") : null;
                if (m != null && m.Success) root = m.Groups[1].Value.Replace('/', '\\');
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Path.Combine(root, @"ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe");
        }
        static string Client()
        {
            string f = Path.Combine(Riot, "RiotClientInstalls.json"), exe = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
            try
            {
                if (File.Exists(f))
                {
                    Dictionary<string, object> d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    object v;
                    if (d != null && (d.TryGetValue("rc_live", out v) || d.TryGetValue("rc_default", out v)) && v is string) exe = ((string)v).Replace('/', '\\');
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            return exe;
        }
        // A shortcut opened through Explorer: Riot Client does not inherit administrator rights.
        // Same target and arguments as Riot's own VALORANT shortcut. Riot Client only selects the game (its remote flag
        // autoLaunch.disabled): the player presses Play there, and the session joins when the game opens.
        public static void Launch(string dir)
        {
            string exe = Client(), lnk = Path.Combine(dir, "Valorant.lnk");
            if (!File.Exists(exe)) throw new InvalidOperationException("No se encontró Riot Client. Abre Valorant una vez e inténtalo de nuevo.");
            Directory.CreateDirectory(dir);
            dynamic l = ((dynamic)Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"))).CreateShortcut(lnk);
            l.TargetPath = exe; l.Arguments = "--launch-product=valorant --launch-patchline=live"; l.WorkingDirectory = Path.GetDirectoryName(exe);
            l.Save();
            StretchSession.Open(lnk);
        }

        // GameUserSettings.ini of the last account used on this PC; null if none.
        public static string SettingsFile()
        {
            string config = Path.Combine(Saved, "Config");
            if (!Directory.Exists(config)) return null;
            string last = Path.Combine(config, @"WindowsClient\RiotLocalMachine.ini"), user = "";
            System.Text.RegularExpressions.Match m = File.Exists(last) ? Regex.Match(ReadShared(last), @"LastKnownUser=([0-9a-fA-F\-]{36})") : null;
            if (m != null && m.Success) user = m.Groups[1].Value;
            return new DirectoryInfo(config).GetDirectories().Where(d => user.Length == 0 || d.Name.StartsWith(user, StringComparison.OrdinalIgnoreCase)).Select(d => new FileInfo(Path.Combine(d.FullName, @"WindowsClient\GameUserSettings.ini"))).Where(f => f.Exists).OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName).FirstOrDefault();
        }
        public static Dictionary<string, string> Settings()
        {
            try
            {
                string file = SettingsFile();
                if (file == null) return null;
                Dictionary<string, string> r = new Dictionary<string, string>();
                foreach (string l in ReadShared(file).Split('\n')) { string[] p = l.Trim().Split(new[] { '=' }, 2); if (p.Length == 2 && !r.ContainsKey(p[0])) r[p[0]] = p[1]; }
                return r;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // Windowed fullscreen and Fill, in the order they are added if missing.
        static readonly string[][] Video = { new[] { "FullscreenMode", "1" }, new[] { "LastConfirmedFullscreenMode", "1" }, new[] { "PreferredFullscreenMode", "1" }, new[] { "bShouldLetterbox", "False" }, new[] { "bLastConfirmedShouldLetterbox", "False" } };
        const string VideoSection = "[/Script/ShooterGame.ShooterGameUserSettings]";

        // Only changes those keys; the rest of the file stays byte for byte (BOM, line breaks and order).
        public static void Fix(string file)
        {
            if (Game.Valorant.Running()) throw new InvalidOperationException("Cierra Valorant antes de cambiar sus ajustes.");
            byte[] b = File.ReadAllBytes(file);
            // Unreal saves in ANSI or in UTF-16 with BOM; Latin-1 returns each byte as is.
            bool wide = b.Length > 1 && b[0] == 0xFF && b[1] == 0xFE;
            Encoding e = wide ? Encoding.Unicode : Encoding.GetEncoding(28591);
            int skip = wide ? 2 : 0;
            string t = e.GetString(b, skip, b.Length - skip), nl = t.Contains("\r\n") ? "\r\n" : "\n", add = "";
            foreach (string[] kv in Video)
            {
                Regex k = new Regex(@"(?m)^([ \t]*" + kv[0] + "=)[^\r\n]*");
                if (k.IsMatch(t)) t = k.Replace(t, "${1}" + kv[1]); else add += kv[0] + "=" + kv[1] + nl;
            }
            if (add.Length > 0)
            {
                // Next to FullscreenMode; if absent, at the start of its section; if that is missing too, in a new section at the end.
                System.Text.RegularExpressions.Match a = Regex.Match(t, @"(?m)^[ \t]*FullscreenMode=[^\r\n]*");
                Regex head = new Regex(@"(?m)^(ï»¿)?[ \t]*" + Regex.Escape(VideoSection) + @"[^\r\n]*");
                if (!a.Success && !head.IsMatch(t)) t += (t.Length == 0 || t.EndsWith("\n") ? "" : nl) + VideoSection + nl;
                if (!a.Success) a = head.Match(t);
                int at = a.Index + a.Length;
                if (string.CompareOrdinal(t, at, "\r\n", 0, 2) == 0) at += 2;
                else if (at < t.Length && t[at] == '\n') at++;
                else add = nl + add.Substring(0, add.Length - nl.Length); // last line without a line break: kept as is
                t = t.Insert(at, add);
            }
            string copy = file + ".tweakeroo";
            if (!File.Exists(copy)) File.Copy(file, copy);
            File.WriteAllBytes(file, (wide ? new byte[] { 0xFF, 0xFE } : new byte[0]).Concat(e.GetBytes(t)).ToArray());
        }

        // AMD Software from the Microsoft Store, if installed.
        static bool AmdApp()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                    return k != null && k.GetSubKeyNames().Any(n => n.StartsWith("AdvancedMicroDevicesInc-RSXCM_", StringComparison.OrdinalIgnoreCase) && n.EndsWith("_fhmx3h6dzfmvj", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return false; }
        }

        // gpu: "nvidia", "amd" or null. dir: app data folder, to record the previous scaling.
        public static List<Check> Checks(Dictionary<string, string> ini, int monitors, string gpu = null, string dir = null)
        {
            List<Check> r = new List<Check>();
            string v;
            if (ini == null) r.Add(new Check { Title = "Ajustes de Valorant sin leer", Text = "Abre el juego una vez y entra en una partida.", Kind = Kind.Gratis });
            else
            {
                bool full = ini.TryGetValue("FullscreenMode", out v) && v == "1", fill = ini.TryGetValue("bShouldLetterbox", out v) && v == "False";
                Action fix = () => { string f = SettingsFile(); if (f == null) throw new InvalidOperationException("Ajustes de Valorant sin leer"); Fix(f); };
                r.Add(new Check { Title = "Modo de pantalla", Text = full ? "Ventana a pantalla completa." : "En Valorant, Vídeo, General: elige Ventana a pantalla completa.", Kind = full ? Kind.Bien : Kind.Gratis, Fix = full ? null : fix });
                r.Add(new Check { Title = "Relación de aspecto", Text = fill ? "Rellenar." : "En Valorant, Vídeo, General: elige Rellenar en vez de Bandas negras.", Kind = fill ? Kind.Bien : Kind.Gratis, Fix = fill ? null : fix });
            }
            r.Add(new Check { Title = "Monitores", Text = monitors == 0 ? "Ya están desactivados en el Administrador de dispositivos; se dejan como están." : "Se desactivan en el Administrador de dispositivos al pulsar Jugar y se reactivan al cerrar el juego.", Kind = Kind.Bien });
            Scale(r, gpu, dir);
            return r;
        }

        // How the GPU scales: the same for every game that stretches.
        public static void Scale(List<Check> r, string gpu, string dir)
        {
            if (gpu == "nvidia")
            {
                int s = -1;
                try { s = NvScale.Get(); } catch (Exception) { }
                bool gpuFull = s == NvScale.GpuFull;
                r.Add(gpuFull ? new Check { Title = "Escala de la gráfica", Text = "Pantalla completa en la GPU.", Kind = Kind.Bien }
                    : new Check { Title = "Escala de la gráfica", Text = "En el panel de NVIDIA, Ajustar tamaño y posición: Pantalla completa, escalar en la GPU.", Kind = Kind.Gratis, Fix = () => NvScale.Fix(dir) });
                r.Add(new Check { Title = "Anular el modo de los juegos", Text = "En el panel de NVIDIA, Ajustar tamaño y posición: marca Anular el modo de escalado de juegos y programas.", Kind = Kind.Gratis, Url = @"shell:AppsFolder\NVIDIACorp.NVIDIAControlPanel_56jybvy8sckqj!NVIDIACorp.NVIDIAControlPanel", Button = "Abrir panel" });
            }
            else if (gpu == "amd")
                r.Add(new Check { Title = "Escala de la gráfica", Text = "En AMD Software, Pantalla: activa Escalado de GPU y elige Panel completo.", Kind = Kind.Gratis, Url = AmdApp() ? @"shell:AppsFolder\AdvancedMicroDevicesInc-RSXCM_fhmx3h6dzfmvj!App" : "https://www.amd.com/en/support/download/drivers.html", Button = "Abrir AMD Software" });
            else r.Add(new Check { Title = "Escala de la gráfica", Text = "En el panel de NVIDIA, Ajustar tamaño y posición: Pantalla completa, escalar en la GPU y anular el modo del juego.", Kind = Kind.Gratis });
        }
    }

    // Says whether a map is loaded, reading only what is new in the log.
    public class MapWatch
    {
        const string Mark = "LogMapLoadModel: Update: [Map Name: ";
        readonly string file = Path.Combine(Valorant.Logs, "ShooterGame.log"), since;
        long at;
        string rest = "";
        public bool InMap { get; private set; }
        // Why the last read failed, or null. It is retried on the next tick.
        public string Error { get; private set; }
        public MapWatch(DateTime utc)
        {
            since = utc.ToString("yyyy.MM.dd-HH.mm.ss", System.Globalization.CultureInfo.InvariantCulture);
            // What is already in the log is from before the session: reading starts at its end, so the old log (up to 20 MB) is never loaded whole.
            // shortcut: if the game rotates its log and the new one outgrows this before the first read, its first lines (startup only) are skipped.
            try { at = new FileInfo(file).Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // null = the line is not about maps.
        public static bool? Loaded(string line)
        {
            int i = line.IndexOf(Mark, StringComparison.Ordinal);
            if (i != 30) return null;
            return line.IndexOf("[Local World: TRUE", i, StringComparison.Ordinal) > 0 && line.IndexOf("[Map Ready: TRUE", i, StringComparison.Ordinal) > 0;
        }
        public bool Read()
        {
            try
            {
                if (!File.Exists(file)) return InMap;
                using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (f.Length < at) { at = 0; rest = ""; InMap = false; }
                    f.Seek(at, SeekOrigin.Begin);
                    byte[] b = new byte[f.Length - at];
                    int n = f.Read(b, 0, b.Length);
                    string[] lines = (rest + Encoding.UTF8.GetString(b, 0, n)).Split('\n');
                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        bool? on = Loaded(lines[i]);
                        // Anything from a previous session does not count.
                        if (on != null && string.CompareOrdinal(lines[i], 1, since, 0, since.Length) >= 0) InMap = on.Value;
                    }
                    // Only once read: after a failure the same lines are read again.
                    at += n;
                    rest = lines[lines.Length - 1];
                }
                Error = null;
            }
            // Any failure, even memory running out while the game loads, only skips this tick: it must never end the session.
            catch (Exception ex) { Error = ex.GetType().Name + ": " + ex.Message; }
            return InMap;
        }
    }

    // Valorant stretch: monitors disabled before playing and desktop resolution when the map loads.
    public class ValorantSession : StretchSession
    {
        MapWatch maps;
        bool held;
        string noted; // last log state written to the trace
        readonly string trace;
        public ValorantSession(string dir) : base(dir, "valorant.json") { trace = Path.Combine(dir, "valorant-registro.txt"); }
        // Short trace so a session that did not stretch can be explained afterwards. Starts again with each session; no game data.
        void Note(string what, bool fresh = false)
        {
            try
            {
                if (!fresh && File.Exists(trace) && new FileInfo(trace).Length > 64 * 1024) return;
                string l = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " " + what + Environment.NewLine;
                if (fresh) File.WriteAllText(trace, l); else File.AppendAllText(trace, l);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public override Game Game { get { return Game.Valorant; } }
        public bool Stretch { get { return data.Stretch; } set { data.Stretch = value; Save(); } }
        public bool Applied { get { return data.Applied; } }
        bool Wide { get { return data.Stretch && data.Res != null && !data.Res.Same(Display.Saved()); } }
        // Still needs stretching: the game is open and the display is still normal.
        public bool CanApply { get { return Pending && Wide && !data.Applied; } }
        public override bool Asks(Res r) { return data.Stretch && !Known(r); }
        public string Warning { get; private set; }

        public override void Begin(Res r)
        {
            if (Pending) End();
            Warning = null;
            held = false;
            noted = null;
            maps = new MapWatch(DateTime.UtcNow);
            data.Trial = data.Applied = false;
            data.Res = r;
            // Windows' saved mode, not the current one: the desktop may still be stretched after a crash.
            data.Native = Display.Saved();
            data.Off = new List<string>();
            data.Old = new Dictionary<string, string>();
            Save();
            bool wide = Wide;
            Note("inicio: " + r.Name + " sobre " + data.Native.Name + (wide ? ", se estira al cargar un mapa" : ", sin estirar"), true);
            if (!wide) return;
            try
            {
                // Recorded before touching them: if the app closes halfway, they are re-enabled on return.
                data.Off = Monitors.On();
                Save();
                foreach (string id in data.Off)
                    try { Monitors.Set(id, false); } catch (InvalidOperationException ex) { Warning = ex.Message; Note("monitor sin desactivar: " + ex.Message); }
                // A new resolution is shown to confirm it; then the normal one comes back until the map loads.
                if (Asks(r)) Show();
            }
            catch { End(); throw; }
        }
        void Show()
        {
            if (!Display.Modes().Any(m => m.Same(data.Res))) { Custom.Try(data.Res); data.Trial = true; Save(); }
            else if (!Display.Current().Same(data.Res)) Display.Set(data.Res);
            if (!Display.Current().Same(data.Res)) throw new InvalidOperationException("Windows no aplicó " + data.Res.Name + ".");
        }
        public override void Confirm(Res r)
        {
            base.Confirm(r);
            Back();
        }
        public override void Launch(string dir) { if (!Game.Running()) Valorant.Launch(dir); }
        // When a map loads, or by hand with the button.
        public bool Arrive(bool byHand)
        {
            if (!CanApply || maps == null && !byHand || !byHand && (held || !MapNow())) return false;
            try { Show(); }
            catch (Exception ex) { Note("no se estiró: " + ex.Message); throw; }
            data.Applied = true;
            Save();
            Note(byHand ? "estirada a mano" : "estirada al cargar el mapa");
            return true;
        }
        // Notes when reading the log starts working or failing, once per change.
        bool MapNow()
        {
            bool on = maps.Read();
            string now = maps.Error ?? "";
            if (now != noted) Note(now == "" ? "registro de Valorant leído" : "registro de Valorant sin leer: " + now);
            noted = now;
            return on;
        }
        void Back()
        {
            if (data.Trial) { Custom.Revert(); data.Trial = false; }
            // Saved again here: sessions left by 1.0.0 may hold a stretched Native.
            Res normal = Display.Saved();
            if (!Display.Current().Same(normal)) Display.Reset();
            if (!Display.Current().Same(normal)) Display.Set(normal);
            data.Applied = false;
            Save();
        }
        public override void End()
        {
            if (!Pending) return;
            Exception bad = null;
            held = true;
            if (!Game.Running())
                foreach (string id in (data.Off ?? new List<string>()).ToList())
                    try { Monitors.Set(id, true); data.Off.Remove(id); }
                    catch (Exception ex) { bad = ex; }
            if (data.Native != null && (data.Trial || data.Applied || Display.Current().Same(data.Res) || !Game.Running()))
                try { Back(); } catch (Exception ex) { bad = ex; }
            if (!Game.Running() && bad == null) data.Old = null;
            Save();
            if (bad != null)
            {
                string now;
                try { now = Display.Current().Name; } catch (InvalidOperationException) { now = "?"; }
                Note("no se pudo volver a la normal (pantalla " + now + "): " + bad.GetType().Name + ": " + bad.Message);
                throw bad;
            }
            Note(Pending ? "pantalla normal; Valorant sigue abierto" : "fin");
        }
    }
}
