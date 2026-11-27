using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Tablero
{
    // One moment when tweaks were turned on or off (Engine records them all).
    public class TweakChange
    {
        public string Time { get; set; } // local time, yyyy-MM-dd HH:mm:ss, like Match.Start
        public List<string> Ids { get; set; }
        public bool On { get; set; }
    }

    public static class Changes
    {
        internal const int Keep = 200;
        const string Format = "yyyy-MM-dd HH:mm:ss";
        static string FileOf(string dir) { return Path.Combine(dir, "cambios.json"); }

        public static List<TweakChange> Read(string dir)
        {
            try
            {
                List<TweakChange> r = File.Exists(FileOf(dir)) ? new JavaScriptSerializer().Deserialize<List<TweakChange>>(File.ReadAllText(FileOf(dir))) : null;
                return r == null ? new List<TweakChange>() : r.Where(c => c != null && c.Time != null && c.Time.Length == Format.Length && c.Ids != null).ToList();
            }
            catch (Exception) { return new List<TweakChange>(); }
        }

        // Bulk calls this once per tweak: the same kind of change within two minutes of the previous one joins that moment.
        public static void Add(string dir, IEnumerable<string> ids, bool on, DateTime at)
        {
            try
            {
                List<TweakChange> all = Read(dir);
                TweakChange last = all.LastOrDefault();
                DateTime prev;
                string now = at.ToString(Format, CultureInfo.InvariantCulture);
                if (last != null && last.On == on && DateTime.TryParseExact(last.Time, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out prev) && (at - prev).TotalSeconds >= 0 && (at - prev).TotalSeconds <= 120)
                { last.Ids = last.Ids.Union(ids).ToList(); last.Time = now; }
                else all.Add(new TweakChange { Time = now, Ids = ids.Distinct().ToList(), On = on });
                Engine.WriteFile(FileOf(dir), new JavaScriptSerializer().Serialize(all.Skip(Math.Max(0, all.Count - Keep)).ToList()));
            }
            catch (Exception) { } // the tweak is already applied: a history that cannot be written must not undo it or report a failure
        }

        // Tweaks of another game do not explain this game's FPS.
        public static List<TweakChange> For(List<TweakChange> all, string game)
        {
            return all.Select(c => new TweakChange { Time = c.Time, On = c.On, Ids = c.Ids.Where(id => { string g = Game.OfTweak(id); return g == null || g == game; }).ToList() })
                .Where(c => c.Ids.Count > 0).ToList();
        }
    }

    // Before and after the last change to the tweaks, from the matches already measured. Says what changed since then, not why.
    public class BeforeAfter
    {
        public const int Need = 3, Use = 10;
        public bool Ok;
        public string Text;              // without Ok: why it does not compare
        public TweakChange Change;       // null: no change recorded
        public string Until;             // the next change, which ends "after"; null = up to now
        public string Mode = "";
        public int Cap, Before, After;
        public double MedA, MedB, LowA, LowB; // A = before, B = after
        public int MedTrend, LowTrend;        // 1 better, 0 no clear change, -1 worse

        public string Title { get { return Change == null ? "Antes y después de los tweaks" : "Desde tus cambios del " + Change.Time.Substring(8, 2) + "/" + Change.Time.Substring(5, 2); } }
        public static string R(double v) { return ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture); }
        public static string Delta(double a, double b, int trend)
        {
            if (trend == 0) return "sin cambio claro";
            int d = (int)Math.Round(b - a), p = a > 0 ? (int)Math.Round(100 * (b - a) / a) : 0;
            return (d > 0 ? "+" : "") + d + " FPS (" + (p > 0 ? "+" : "") + p + " %)";
        }
        public string Counts { get { return "Partidas usadas: " + Before + " antes y " + After + " después." + (Mode != "" ? " Modo: " + Mode + "." : "") + (Cap > 0 ? " Con límite de " + Cap + " FPS." : " Sin límite de FPS."); } }
        public string Line { get { return Title + ": " + R(MedA) + " → " + R(MedB) + " FPS, " + Delta(MedA, MedB, MedTrend) + "."; } }

        // Noise is the spread of the "before" side: a difference smaller than its standard deviation (at least 1) is no clear change.
        internal static int Trend(List<double> before, double after)
        {
            double avg = before.Average(), sd = Math.Sqrt(before.Sum(x => (x - avg) * (x - avg)) / (before.Count - 1));
            return Math.Abs(after - avg) < Math.Max(1, sd) ? 0 : Math.Sign(after - avg);
        }

        // The last change followed by enough matches; "after" ends at the next change. Same mode (when the map tells it) and same cap on both sides.
        public static BeforeAfter Compare(List<Match> all, List<TweakChange> changes, bool modes)
        {
            if (changes.Count == 0) return new BeforeAfter { Text = "Al activar tweaks se comparan aquí los FPS de antes y de después." };
            Func<Match, string> mode = m => modes || m.Map == "Campo de tiro" ? m.Map ?? "" : "";
            List<TweakChange> ch = changes.OrderBy(c => c.Time, StringComparer.Ordinal).ToList();
            BeforeAfter latest = null;
            for (int i = ch.Count - 1; i >= 0; i--)
            {
                string at = ch[i].Time, until = i + 1 < ch.Count ? ch[i + 1].Time : null;
                List<Match> before = all.Where(m => string.CompareOrdinal(m.Start, at) < 0).OrderByDescending(m => m.Start, StringComparer.Ordinal).ToList();
                List<Match> after = all.Where(m => string.CompareOrdinal(m.Start, at) >= 0 && (until == null || string.CompareOrdinal(m.Start, until) < 0)).OrderByDescending(m => m.Start, StringComparer.Ordinal).ToList();
                var sides = before.Concat(after).Select(m => new { M = mode(m), m.Cap }).Distinct()
                    .Select(k => new { k.M, k.Cap, B = before.Where(m => mode(m) == k.M && m.Cap == k.Cap).Take(Use).ToList(), A = after.Where(m => mode(m) == k.M && m.Cap == k.Cap).Take(Use).ToList() }).ToList();
                var best = sides.Where(g => g.B.Count >= Need && g.A.Count >= Need).OrderByDescending(g => Math.Min(g.B.Count, g.A.Count)).FirstOrDefault();
                if (best != null)
                {
                    BeforeAfter s = new BeforeAfter
                    {
                        Ok = true, Change = ch[i], Until = until, Mode = best.M, Cap = best.Cap, Before = best.B.Count, After = best.A.Count,
                        MedA = best.B.Average(m => (double)m.Median), MedB = best.A.Average(m => (double)m.Median), LowA = best.B.Average(m => (double)m.Low), LowB = best.A.Average(m => (double)m.Low)
                    };
                    s.MedTrend = Trend(best.B.Select(m => (double)m.Median).ToList(), s.MedB);
                    s.LowTrend = Trend(best.B.Select(m => (double)m.Low).ToList(), s.LowB);
                    return s;
                }
                if (latest != null) continue;
                // Only the last change says what is missing: playing more only adds matches after it.
                latest = new BeforeAfter { Change = ch[i] };
                if (sides.Select(g => g.M).Distinct().Any(md => before.Count(m => mode(m) == md) >= Need && after.Count(m => mode(m) == md) >= Need))
                    latest.Text = "Con límite de FPS distinto no se compara: las partidas de antes y de después de los cambios tienen límites diferentes.";
                else
                {
                    int most = sides.Where(g => g.B.Count >= Need).Select(g => g.A.Count).DefaultIfEmpty(-1).Max();
                    latest.Text = most < 0 ? "No hay suficientes partidas de antes de los cambios para comparar." : "Faltan partidas para comparar: juega " + (Need - most) + " más.";
                }
            }
            return latest;
        }

        // The same for the Connection tests around the same change, when there are enough on both sides.
        public static string Ping(List<NetResult> tests, BeforeAfter s)
        {
            if (s.Change == null) return "";
            List<NetResult> ok = tests.Where(t => t.Epic != null && t.Epic.Sent > 0 && !t.Epic.Silent && t.Date != null).OrderByDescending(t => t.Date, StringComparer.Ordinal).ToList();
            List<NetResult> b = ok.Where(t => string.CompareOrdinal(t.Date, s.Change.Time) < 0).Take(Use).ToList();
            List<NetResult> a = ok.Where(t => string.CompareOrdinal(t.Date, s.Change.Time) >= 0 && (s.Until == null || string.CompareOrdinal(t.Date, s.Until) < 0)).Take(Use).ToList();
            if (b.Count < Need || a.Count < Need) return "Comparar el ping necesita al menos 3 pruebas de Conexión antes de los cambios y 3 después.";
            Func<NetResult, double>[] f = { t => t.Epic.Median, t => t.Epic.Jitter, t => t.Epic.Loss };
            double[] x = f.Select(g => b.Average(g)).ToArray(), y = f.Select(g => a.Average(g)).ToArray();
            bool clear = f.Select((g, i) => Trend(b.Select(g).ToList(), y[i])).Any(t => t != 0);
            return "Ping: " + NetTest.Num(x[0]) + " → " + NetTest.Num(y[0]) + " ms, variación " + NetTest.Num(x[1]) + " → " + NetTest.Num(y[1]) + " ms, pérdida " + NetTest.Num(x[2]) + " → " + NetTest.Num(y[2]) + " %." + (clear ? "" : " Sin cambio claro.");
        }
    }
}
