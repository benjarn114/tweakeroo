// CS2: finds Steam, its libraries, cs2.exe and the player's files. Only reads.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Tablero
{
    // Valve KeyValues text (.vdf, .acf, cs2_video.txt, .vcfg): quoted keys, quoted values and { } blocks.
    // A repeated key keeps the first one. Comments (//) are skipped. Anything malformed ends the read with what was parsed.
    public static class Kv
    {
        public static Dictionary<string, object> Parse(string text)
        {
            int i = 0;
            return Block(text, ref i);
        }
        static Dictionary<string, object> Block(string t, ref int i)
        {
            Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            string key = null;
            while (i < t.Length)
            {
                char c = t[i];
                if (c == '/' && i + 1 < t.Length && t[i + 1] == '/') { while (i < t.Length && t[i] != '\n') i++; continue; }
                if (c == '}') { i++; return d; }
                if (c == '{') { i++; Dictionary<string, object> sub = Block(t, ref i); if (key != null && !d.ContainsKey(key)) d[key] = sub; key = null; continue; }
                if (c != '"') { i++; continue; }
                StringBuilder s = new StringBuilder();
                for (i++; i < t.Length && t[i] != '"'; i++) { if (t[i] == '\\' && i + 1 < t.Length) i++; s.Append(t[i]); }
                i++;
                if (key == null) key = s.ToString();
                else { if (!d.ContainsKey(key)) d[key] = s.ToString(); key = null; }
            }
            return d;
        }
        // Path of keys, e.g. Get(d, "UserLocalConfigStore", "Software", "Valve"); null if any is missing.
        public static object Get(Dictionary<string, object> d, params string[] path)
        {
            object o = d;
            foreach (string k in path) { Dictionary<string, object> b = o as Dictionary<string, object>; if (b == null || !b.TryGetValue(k, out o)) return null; }
            return o;
        }
    }

    public static class Cs2
    {
        public const string App = "730";
        static string ReadShared(string file)
        {
            using (StreamReader r = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))) return r.ReadToEnd();
        }
        static Dictionary<string, object> Read(string file)
        {
            try { return file != null && File.Exists(file) ? Kv.Parse(ReadShared(file)) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // Steam's folder: the user key (written with forward slashes), the 32-bit machine key, or the default. Replaceable in tests.
        public static Func<string> Steam = () =>
        {
            string p = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                ?? Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            return (p ?? @"C:\Program Files (x86)\Steam").Replace('/', '\\');
        };

        // setting.aspectratiomode of cs2_video.txt: 0 = 4:3, 1 = 16:9 (and anything else), 2 = 16:10.
        public static int RatioMode(Res r) { return r.W * 3 == r.H * 4 ? 0 : r.W * 10 == r.H * 16 ? 2 : 1; }

        // Every Steam library, the main one first.
        public static List<string> Libraries()
        {
            string steam = Steam();
            List<string> r = new List<string> { steam };
            Dictionary<string, object> f = Read(Path.Combine(steam, @"steamapps\libraryfolders.vdf"));
            Dictionary<string, object> all = f == null ? null : Kv.Get(f, "libraryfolders") as Dictionary<string, object>;
            if (all != null)
                foreach (Dictionary<string, object> lib in all.Values.OfType<Dictionary<string, object>>())
                {
                    string p = Kv.Get(lib, "path") as string;
                    if (p != null && !r.Any(x => string.Equals(x.TrimEnd('\\'), p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) r.Add(p);
                }
            return r;
        }

        // The library whose manifest lists CS2 wins; without a manifest, where Steam would put it (the file may not exist).
        public static string Exe()
        {
            foreach (string lib in Libraries())
            {
                Dictionary<string, object> m = Read(Path.Combine(lib, @"steamapps\appmanifest_" + App + ".acf"));
                if (m == null) continue;
                string dir = Kv.Get(m, "AppState", "installdir") as string ?? "Counter-Strike Global Offensive";
                return Path.Combine(lib, "steamapps", "common", dir, @"game\bin\win64\cs2.exe");
            }
            return Path.Combine(Steam(), @"steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe");
        }

        // userdata\<account>\730\local\cfg of the account that played last on this PC; null if none.
        public static string Cfg()
        {
            string ud = Path.Combine(Steam(), "userdata");
            try
            {
                if (!Directory.Exists(ud)) return null;
                return new DirectoryInfo(ud).GetDirectories().Select(d => new FileInfo(Path.Combine(d.FullName, @"730\local\cfg\cs2_video.txt")))
                    .Where(f => f.Exists).OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.DirectoryName).FirstOrDefault();
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // cs2_video.txt as "setting.x" -> value; null if not read.
        public static Dictionary<string, string> Video()
        {
            string cfg = Cfg();
            Dictionary<string, object> v = cfg == null ? null : Read(Path.Combine(cfg, "cs2_video.txt"));
            Dictionary<string, object> b = v == null ? null : Kv.Get(v, "video.cfg") as Dictionary<string, object>;
            return b == null ? null : b.Where(x => x.Value is string).ToDictionary(x => x.Key, x => (string)x.Value, StringComparer.OrdinalIgnoreCase);
        }

        // fps_max of cs2_machine_convars.vcfg (0 = uncapped); null if not read.
        public static int? FpsMax()
        {
            string cfg = Cfg();
            Dictionary<string, object> v = cfg == null ? null : Read(Path.Combine(cfg, "cs2_machine_convars.vcfg"));
            string s = v == null ? null : Kv.Get(v, "config", "convars", "fps_max") as string;
            double d;
            return s != null && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? (int?)Math.Round(d) : null;
        }

        // Launch options typed in Steam (Properties of CS2); "" if none, null if not read.
        public static string LaunchOptions()
        {
            string cfg = Cfg();
            if (cfg == null) return null;
            // cfg = userdata\<account>\730\local\cfg -> userdata\<account>\config\localconfig.vdf
            Dictionary<string, object> v = Read(Path.Combine(Path.GetFullPath(Path.Combine(cfg, @"..\..\..")), @"config\localconfig.vdf"));
            if (v == null) return null;
            return Kv.Get(v, "UserLocalConfigStore", "Software", "Valve", "Steam", "apps", App, "LaunchOptions") as string ?? "";
        }

        // Before playing. Resolution and ratio are written by Play, and both fullscreen modes stretch: only what is not written.
        // video: Video(); launch: LaunchOptions(); gpu: "nvidia", "amd" or null; dir: app data folder.
        public static List<Check> Checks(Dictionary<string, string> video, string launch, string gpu, string dir)
        {
            List<Check> r = new List<Check>();
            string v;
            if (video == null) r.Add(new Check { Title = "Ajustes de CS2 sin leer", Text = "Abre el juego una vez.", Kind = Kind.Gratis });
            else if (video.TryGetValue("setting.monitor_index", out v) && v.Trim() != "0")
                r.Add(new Check { Title = "Monitor", Text = "CS2 está en otro monitor: solo se estira la pantalla principal.", Kind = Kind.Gratis });
            Func<string, bool> has = o => launch != null && Regex.IsMatch(launch, "(^|\\s)" + o + "(\\s|$)", RegexOptions.IgnoreCase);
            if (has("-vulkan")) r.Add(new Check { Title = "Opciones de lanzamiento", Text = "Con -vulkan no se miden los FPS y el 4:3 puede no estirarse.", Kind = Kind.Gratis });
            if (has("-allow_third_party_software")) r.Add(new Check { Title = "Opciones de lanzamiento", Text = "Con -allow_third_party_software CS2 juega sin el Modo de confianza. Conviene quitarlo.", Kind = Kind.Gratis });
            Valorant.Scale(r, gpu, dir);
            return r;
        }

        // Steam gets the address through Explorer, so neither Steam nor CS2 inherit administrator rights.
        // Without arguments in the address: Steam warns about those, and the player's own launch options apply.
        public const string Address = "steam://rungameid/" + App;
        public static void Launch()
        {
            if (!File.Exists(Path.Combine(Steam(), "steam.exe"))) throw new InvalidOperationException("No se encontró Steam. Instálalo o abre CS2 una vez.");
            StretchSession.Open(Address);
        }
    }
}
