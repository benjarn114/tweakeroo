using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Tablero
{
    // The code is written in Spanish; en.txt holds the English (Spanish TAB English, with {0} for variable parts).
    public static class L
    {
        public static bool En;
        public static readonly HashSet<string> Missing = new HashSet<string>();
        static readonly Dictionary<string, string> map = new Dictionary<string, string>();
        static readonly List<KeyValuePair<Regex, string>> patterns = new List<KeyValuePair<Regex, string>>();

        static string Resource(string name)
        {
            using (StreamReader r = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(name))) return r.ReadToEnd();
        }
        static L()
        {
            List<string[]> tpl = new List<string[]>();
            foreach (string line in Resource("en.txt").Replace("\r", "").Split('\n'))
            {
                string[] p = line.Split('\t');
                if (p.Length != 2) continue;
                if (Regex.IsMatch(p[0], @"\{\d\}")) tpl.Add(p); else map[p[0]] = p[1];
            }
            // Phrases with more fixed text are tried first.
            foreach (string[] p in tpl.OrderByDescending(x => Regex.Replace(x[0], @"\{\d\}", "").Length))
                patterns.Add(new KeyValuePair<Regex, string>(new Regex("^" + Regex.Replace(Regex.Escape(p[0]), @"\\\{(\d)}", "(?<g$1>.+?)") + "$", RegexOptions.Singleline), p[1]));
        }

        static string FileOf(string dir) { return Path.Combine(dir, "idioma.txt"); }
        public static void Load(string dir)
        {
            string s = "";
            try { if (File.Exists(FileOf(dir))) s = File.ReadAllText(FileOf(dir)).Trim(); } catch (IOException) { }
            En = s != "es"; // English by default
        }
        public static void Save(string dir, bool en) { Directory.CreateDirectory(dir); File.WriteAllText(FileOf(dir), en ? "en" : "es"); }
        public static string Terms() { return Resource(En ? "terminos.en.txt" : "terminos.txt"); }

        static string Find(string s)
        {
            string r;
            if (map.TryGetValue(s, out r)) return r;
            foreach (KeyValuePair<Regex, string> p in patterns)
            {
                System.Text.RegularExpressions.Match m = p.Key.Match(s);
                if (m.Success) return Regex.Replace(p.Value, @"\{(\d)\}", x => Soft(m.Groups["g" + x.Groups[1].Value].Value));
            }
            return null;
        }
        // The captured text may be a proper name: if it has no translation it is left as is.
        static string Soft(string s) { return Parts(s, false); }
        static string Parts(string s, bool note)
        {
            if (!s.Any(char.IsLetter)) return s;
            string t = s.Trim(), r = Find(t);
            if (r != null) return s.Replace(t, r);
            if (t.Contains("\n")) return string.Join("\n", s.Split('\n').Select(x => Parts(x, note)));
            string[] parts = Regex.Split(s, @"(?<=[.?!])(?=\s)");
            if (parts.Length > 1) return string.Concat(parts.Select(x => Parts(x, note)));
            if (note) Missing.Add(t);
            return s;
        }
        public static string T(string s) { return !En || string.IsNullOrEmpty(s) ? s : Parts(s, true); }

        // Fixed window texts: translated before reading the XAML.
        public static string Xaml(string x)
        {
            if (!En) return x;
            x = Regex.Replace(x, "(Text|Content|ToolTip|Tag|AutomationProperties\\.Name)=\"([^\"{]+)\"", m => m.Groups[1].Value + "=\"" + System.Security.SecurityElement.Escape(T(System.Net.WebUtility.HtmlDecode(m.Groups[2].Value))) + "\"");
            return x.Replace("<TextBlock", "<t:Tb").Replace("</TextBlock", "</t:Tb");
        }
    }

    // Text that translates itself, wherever it comes from.
    public class Tb : TextBlock
    {
        static Tb() { TextProperty.OverrideMetadata(typeof(Tb), new FrameworkPropertyMetadata("", null, (d, v) => L.T((string)v ?? ""))); }
    }
}
