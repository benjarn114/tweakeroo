using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Tablero
{
    // Fortnite server notice: only when another region clearly has a better ping than the one chosen in the game. Read only.
    public static class Region
    {
        // A better region counts only when its ping is at least MinMs lower AND at least MinShare lower than the chosen one:
        // under that, the difference is noise between tests and not worth changing servers (and queues) for.
        public const int MinMs = 15;
        public const double MinShare = 0.25;
        public const int Pings = 3; // per region; a region with fewer answers is not compared

        // SelectedRegionId of Fortnite's GameUserSettings.ini as a NetTest.Regions id; null if automatic, unknown or unreadable.
        public static string Chosen(string ini)
        {
            try
            {
                string t;
                using (StreamReader r = new StreamReader(new FileStream(ini, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))) t = r.ReadToEnd();
                System.Text.RegularExpressions.Match m = Regex.Match(t, @"(?m)^\s*SelectedRegionId\s*=\s*""?(\w*)");
                // Without that line, the region of the last match ("Matchmaking:Region":"BR"); it may be the automatic pick.
                if (!m.Success) m = Regex.Match(t, @"""Matchmaking:Region""\s*:\s*""(\w*)""");
                string id = m.Groups[1].Value.ToLowerInvariant();
                return NetTest.Name(id) != "" ? id : null; // "", "auto", "none"…: automatic
            }
            catch (Exception) { return null; }
        }

        // Pings to every region at once, Pings each.
        public static async Task<Dictionary<string, PingStats>> Measure()
        {
            List<string> ids = Enumerable.Range(0, NetTest.Regions.GetLength(0)).Select(i => NetTest.Regions[i, 0]).ToList();
            PingStats[] s = await Task.WhenAll(ids.Select(async id =>
            {
                List<int> ms = new List<int>();
                for (int i = 0; i < Pings; i++) ms.Add(await NetTest.Ping(NetTest.Host(id), 1000));
                return PingStats.Of(ms);
            }));
            Dictionary<string, PingStats> r = new Dictionary<string, PingStats>();
            for (int i = 0; i < ids.Count; i++) r[ids[i]] = s[i];
            return r;
        }

        // Lowest median among those that answer; null if none.
        public static string Lowest(Dictionary<string, PingStats> ms)
        {
            return ms.Where(x => !x.Value.Silent && x.Value.Sent > 0).OrderBy(x => x.Value.Median).Select(x => x.Key).FirstOrDefault();
        }

        // The clearly better region, or null (automatic, already the best, small difference or not enough answers).
        public static string Better(string chosen, Dictionary<string, PingStats> ms)
        {
            Func<PingStats, bool> full = p => p.Sent - p.Lost >= Pings;
            PingStats now;
            if (chosen == null || ms == null || !ms.TryGetValue(chosen, out now) || !full(now)) return null;
            KeyValuePair<string, PingStats> best = ms.Where(x => x.Key != chosen && full(x.Value)).OrderBy(x => x.Value.Median).FirstOrDefault();
            if (best.Key == null) return null;
            int gain = now.Median - best.Value.Median;
            return gain >= MinMs && best.Value.Median <= now.Median * (1 - MinShare) ? best.Key : null;
        }

        public static string Notice(string chosen, Dictionary<string, PingStats> ms)
        {
            string b = Better(chosen, ms);
            if (b == null) return null;
            return NetTest.Name(b) + " da " + ms[b].Median + " ms; la región elegida en Fortnite (" + NetTest.Name(chosen) + ") da " + ms[chosen].Median + " ms. Se cambia en Fortnite: Ajustes > Juego > Región de emparejamiento.";
        }
    }
}
