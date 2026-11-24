using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Tablero
{
    public class Match
    {
        public string Start { get; set; } // local time, yyyy-MM-dd HH:mm:ss
        public string Map { get; set; }
        public int Seconds { get; set; }
        public int Median { get; set; }
        public int Low { get; set; }  // the slowest 5 % of seconds fall below this
        public int Max { get; set; }
        public int Cap { get; set; }  // detected FPS cap; 0 = uncapped
    }

    // Each Fortnite log line has the time and the frame number (modulo 1000): the FPS come from that.
    public static class FpsLog
    {
        public static string Folder = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\FortniteGame\Saved\Logs");
        static string FileOf(string dir, Game g) { return Path.Combine(dir, g == Game.Fortnite ? "fps.json" : "fps-" + g.Id + ".json"); }
        internal const int Skip = 20, MinSeconds = 60;

        static int Num(string s, int at, int len)
        {
            int v = 0;
            for (int i = at; i < at + len; i++)
            {
                if (s[i] == ' ' && v == 0) continue;
                if (s[i] < '0' || s[i] > '9') return -1;
                v = v * 10 + s[i] - '0';
            }
            return v;
        }

        static string MapName(string line)
        {
            foreach (string[] m in new[] { new[] { "VKPlay", "Creativo" }, new[] { "Figment", "Fortnite OG" }, new[] { "BlastBerry", "Recarga" }, new[] { "Juno", "LEGO" }, new[] { "DelMar", "Rocket Racing" }, new[] { "Pilgrim", "Festival" } })
                if (line.IndexOf(m[0], StringComparison.OrdinalIgnoreCase) >= 0) return m[1];
            return "Otro modo";
        }

        const string ValMark = "LogMapLoadModel: Update: [Map Name: ";
        internal static readonly Dictionary<string, string> ValMaps = new Dictionary<string, string> { { "RangeV2", "Campo de tiro" }, { "Range", "Campo de tiro" }, { "Ascent", "Ascent" }, { "Bonsai", "Split" }, { "Canyon", "Fracture" }, { "Duality", "Bind" }, { "Foxtrot", "Breeze" }, { "Infinity", "Abyss" }, { "Jam", "Lotus" }, { "Juliett", "Sunset" }, { "Pitt", "Pearl" }, { "Port", "Icebox" }, { "Rook", "Corrode" }, { "Triad", "Haven" }, { "HURM_Alley", "District" }, { "HURM_Bowl", "Kasbah" }, { "HURM_Helix", "Drift" }, { "HURM_HighTide", "Glitch" }, { "HURM_Yard", "Piazza" } };

        public static List<Match> Read(TextReader r, bool valorant = false)
        {
            List<Match> res = new List<Match>();
            string map = null, code = null, l;
            DateTime start = DateTime.MinValue, prev = DateTime.MinValue, t = DateTime.MinValue;
            int frame = 0, frames = 0;
            double secs = 0;
            List<double> w = new List<double>();
            Action close = () => { if (map != null && w.Count >= MinSeconds) res.Add(Sum(map, start, w)); map = code = null; w = new List<double>(); };
            while ((l = r.ReadLine()) != null)
            {
                if (l.Length < 31 || l[0] != '[' || l[24] != ']' || l[25] != '[' || l[29] != ']') continue;
                int n = Num(l, 26, 3), y = Num(l, 1, 4), mo = Num(l, 6, 2), d = Num(l, 9, 2), h = Num(l, 12, 2), mi = Num(l, 15, 2), s = Num(l, 18, 2), ms = Num(l, 21, 3);
                if (n < 0 || ms < 0 || y < 2000 || mo < 1 || mo > 12 || d < 1 || d > 31 || h < 0 || h > 23 || mi < 0 || mi > 59 || s < 0 || s > 59) continue;
                t = new DateTime(y, mo, 1, h, mi, s, ms, DateTimeKind.Utc).AddDays(d - 1);
                if (valorant)
                {
                    if (l.IndexOf(ValMark, 30, StringComparison.Ordinal) == 30)
                    {
                        int from = 30 + ValMark.Length, to = l.IndexOf(' ', from);
                        string name = to < 0 ? "" : l.Substring(from, to - from);
                        if (code != null && name != code) close();
                        if (code == null && l.IndexOf("[Local World: TRUE", StringComparison.Ordinal) > 0)
                        {
                            code = name; start = t; prev = DateTime.MinValue;
                            if (!ValMaps.TryGetValue(name, out map)) map = name;
                        }
                    }
                }
                else if (l.IndexOf("LogLoad: LoadMap:", 30, StringComparison.Ordinal) == 30) close();
                else if (l.IndexOf("LogWorld: Bringing World ", 30, StringComparison.Ordinal) == 30)
                {
                    close();
                    if (l.IndexOf("/Frontend", StringComparison.Ordinal) < 0 && l.IndexOf("/Login", StringComparison.Ordinal) < 0) { map = MapName(l); start = t; }
                    prev = DateTime.MinValue;
                }
                if (map == null) continue;
                double dt = (t - prev).TotalSeconds;
                // A long gap does not tell how many times the counter wrapped past a thousand: it is discarded.
                if (prev == DateTime.MinValue || dt < 0 || dt > 1.2) { secs = 0; frames = 0; }
                else
                {
                    secs += dt;
                    frames += (n - frame + 1000) % 1000;
                    if (secs >= 1) { if ((t - start).TotalSeconds > Skip) w.Add(frames / secs); secs = 0; frames = 0; }
                }
                prev = t; frame = n;
            }
            close();
            return res;
        }

        internal static Match Sum(string map, DateTime start, List<double> w)
        {
            w.Sort();
            double p95 = w[w.Count * 95 / 100];
            bool capped = w.Count(x => x >= p95 - 3 && x <= p95 + 2) > w.Count * 0.2;
            return new Match
            {
                Map = map, Start = start.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Seconds = w.Count,
                Median = (int)Math.Round(w[w.Count / 2]), Low = (int)Math.Round(w[w.Count / 20]), Max = (int)Math.Round(w[w.Count - 1]),
                Cap = capped ? (int)Math.Round(p95 / 5) * 5 : 0
            };
        }

        public class Store
        {
            public Dictionary<string, string> Files { get; set; }
            public List<Match> Matches { get; set; }
            public string Since { get; set; } // the earlier data was deleted and does not come back
        }

        public static void Clear(string dir, Game g)
        {
            Store st = new Store { Files = new Dictionary<string, string>(), Matches = new List<Match>(), Since = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
            Engine.WriteFile(FileOf(dir, g), new JavaScriptSerializer().Serialize(st));
        }

        // An unreadable history is rebuilt from the logs.
        static Store Load(string file, JavaScriptSerializer json)
        {
            Store st = null;
            if (File.Exists(file))
            {
                try { st = json.Deserialize<Store>(File.ReadAllText(file)); }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            return st == null || st.Files == null || st.Matches == null ? new Store { Files = new Dictionary<string, string>(), Matches = new List<Match>() } : st;
        }

        // The game deletes old logs: what was already read is kept in the app folder.
        // CS2 leaves no FPS in any log: only its history is loaded (FpsRecorder adds to it with Add).
        public static List<Match> Update(string dir, Game g)
        {
            bool val = g == Game.Valorant;
            string file = FileOf(dir, g), logsIn = val ? Valorant.Logs : g == Game.Fortnite ? Folder : null;
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Store st = Load(file, json);
            if (logsIn != null && Directory.Exists(logsIn))
            {
                FileInfo[] logs = new DirectoryInfo(logsIn).GetFiles(val ? "ShooterGame*.log" : "FortniteGame*.log");
                bool changed = false;
                foreach (FileInfo f in logs)
                {
                    string stamp = f.Length + "|" + f.LastWriteTimeUtc.Ticks, old;
                    if (st.Files.TryGetValue(f.Name, out old) && old == stamp) continue;
                    try
                    {
                        using (StreamReader r = new StreamReader(new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16), Encoding.UTF8))
                            foreach (Match m in Read(r, val).Where(x => string.CompareOrdinal(x.Start, st.Since ?? "") >= 0)) { st.Matches.RemoveAll(x => x.Start == m.Start); st.Matches.Add(m); }
                        st.Files[f.Name] = stamp;
                        changed = true;
                    }
                    catch (IOException) { }
                }
                foreach (string gone in st.Files.Keys.Where(k => !logs.Any(f => f.Name == k)).ToList()) st.Files.Remove(gone);
                // shortcut: only the last 300 matches, extend if more history is ever wanted
                st.Matches = st.Matches.OrderByDescending(m => m.Start, StringComparer.Ordinal).Take(300).ToList();
                if (changed) Engine.WriteFile(file, json.Serialize(st));
            }
            return st.Matches;
        }

        // A match measured while playing (CS2), kept with the rest, newest first. Returns the whole history.
        public static List<Match> Add(string dir, Game g, Match m)
        {
            string file = FileOf(dir, g);
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Store st = Load(file, json);
            // Started before the history was cleared: it stays cleared.
            if (string.CompareOrdinal(m.Start, st.Since ?? "") >= 0) { st.Matches.RemoveAll(x => x.Start == m.Start); st.Matches.Add(m); }
            st.Matches = st.Matches.OrderByDescending(x => x.Start, StringComparer.Ordinal).Take(300).ToList();
            Engine.WriteFile(file, json.Serialize(st));
            return st.Matches;
        }

        static string FpsOf(List<Match> m) { return (int)Math.Round(m.Average(x => (double)x.Median)) + " FPS en " + m.Count + " partidas"; }
        // Only compares matches of the same mode and the same cap: otherwise the difference would not be due to the change.
        public static string Compare(List<Match> all, string change)
        {
            if (change == null) return "Al activar tweaks se comparan aquí los FPS de antes y de después.";
            var best = all.GroupBy(m => new { m.Map, m.Cap })
                .Select(g => new { g.Key, Before = g.Where(m => string.CompareOrdinal(m.Start, change) < 0).ToList(), After = g.Where(m => string.CompareOrdinal(m.Start, change) >= 0).ToList() })
                .Where(g => g.Before.Count >= 2 && g.After.Count >= 2).OrderByDescending(g => Math.Min(g.Before.Count, g.After.Count)).FirstOrDefault();
            if (best == null) return "La comparación requiere dos partidas antes y dos después del último cambio grande, del mismo modo y con el mismo límite de FPS.";
            string s = "Antes del último cambio grande: " + FpsOf(best.Before) + ". Después: " + FpsOf(best.After) + ". Modo: " + best.Key.Map + (best.Key.Cap > 0 ? ", con límite de " + best.Key.Cap + " FPS." : ", sin límite.");
            return best.Key.Cap > 0 ? s + " Con límite, una mejora puede no verse: conviene comparar también los peores momentos." : s;
        }
    }

    public class Estimate { public int Low, High; public bool GpuLimits; }

    // shortcut: rough estimate for Performance mode, anchored to ~550 FPS on an RTX 5060 at 1080p (ProSettings estimate); the range is wide on purpose
    public static class Estimator
    {
        // Relative power, RTX 3060 = 100. The most specific comes first.
        const string Gpus = "5090=450 5080=310 5070 Ti=270 5070=220 5060 Ti=165 5060=140 5050=105 4090=370 4080=290 4070 Ti=230 4070 SUPER=210 4070=180 4060 Ti=140 4060=118 4050=85 "
            + "3090=205 3080=190 3070 Ti=155 3070=145 3060 Ti=130 3060=100 3050=70 2080 Ti=150 2080=125 2070=105 2060=85 1660 Ti=70 1660 SUPER=68 1660=60 1650 SUPER=55 1650=45 1080 Ti=110 1080=85 1070=70 1060=50 1050 Ti=33 1050=26 1030=13 "
            + "9070 XT=265 9070=240 9060 XT=165 7900 XTX=290 7900 XT=240 7900 GRE=215 7800 XT=200 7700 XT=165 7600 XT=118 7600=112 6950 XT=215 6900 XT=205 6800 XT=195 6800=170 6750 XT=140 6700 XT=135 6700=120 6650 XT=112 6600 XT=108 6600=96 6500 XT=45 6400=35 "
            + "5700 XT=100 5700=90 5600 XT=82 5500 XT=55 RX 590=58 RX 580=52 RX 570=46 RX 560=24 B580=125 B570=108 A770=100 A750=92 A580=85 A380=40 780M=28 760M=22 680M=22 Iris=12 Vega=12 UHD=6 HD Graphics=5 Radeon Graphics=14";
        static readonly string[,] Cpus = {
            { @"Ryzen \d [79]\d{3}X3D", "650" }, { @"Ryzen \d 5\d{3}X3D", "480" }, { @"Ryzen \d [789]\d{3}", "480" }, { @"Ryzen AI", "400" }, { @"Ryzen \d 6\d{3}", "370" }, { @"Ryzen \d 5\d{3}", "370" },
            { @"Ryzen \d [34]\d{3}", "290" }, { @"Ryzen \d [12]\d{3}", "200" }, { @"Core Ultra", "450" }, { @"i3-1[234]\d{3}", "360" }, { @"i\d-1[234]\d{3}", "440" }, { @"i\d-1[01]\d{3}", "320" }, { @"i\d-1[0-4]\d\d[A-Z]", "280" },
            { @"i\d-[89]\d{3}", "260" }, { @"i\d-[67]\d{3}", "190" }, { @"i\d-[2-5]\d{3}", "140" } };

        static int Gpu(string name)
        {
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(Gpus, @"([^=]+)=(\d+)\s?"))
                if (Regex.IsMatch(name.Replace("(TM)", "").Replace("(R)", ""), @"\b" + Regex.Escape(m.Groups[1].Value).Replace(@"\ ", @"\s+") + @"\b", RegexOptions.IgnoreCase)) return int.Parse(m.Groups[2].Value);
            return 0;
        }
        static int Cpu(string name)
        {
            for (int i = 0; i < Cpus.GetLength(0); i++) if (Regex.IsMatch(name, Cpus[i, 0])) return int.Parse(Cpus[i, 1]);
            return 0;
        }
        public static Estimate For(Hw h, int w, int hgt)
        {
            double g = Gpu(h.Gpu), c = Cpu(h.Cpu);
            if (g <= 0 || c <= 0 || w <= 0 || hgt <= 0) return null;
            if (h.Laptop) { g *= 0.75; c *= 0.85; }
            g = 550 * Math.Pow(g / 140, 0.7) * Math.Pow(1920.0 * 1080 / (w * (double)hgt), 0.5);
            double f = Math.Min(g, c);
            return new Estimate { Low = (int)(f * 0.7 / 10) * 10, High = (int)(f * 1.15 / 10) * 10, GpuLimits = g < c };
        }
    }
}
