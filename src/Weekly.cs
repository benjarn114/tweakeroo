using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tablero
{
    // Weekly summary: one toast a week from the Mis FPS history (fps*.json). Tracker reports are not added:
    // the same matches are already in that history, and counting both would count them twice.
    public static class Weekly
    {
        static readonly string[] days = { "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado" };

        // Due when never shown or 7 days or more since the last one (yyyy-MM-dd).
        public static bool Due(string last, DateTime now)
        {
            DateTime d;
            return !DateTime.TryParseExact(last ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) || (now.Date - d).TotalDays >= 7;
        }

        static DateTime When(Match m)
        {
            DateTime d;
            return DateTime.TryParseExact(m.Start ?? "", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
        }

        // The sentence for the game with most matches in the last 7 days, or null with none.
        public static string Text(Dictionary<string, List<Match>> byGame, DateTime now)
        {
            string best = null; List<Match> week = null, before = null;
            foreach (KeyValuePair<string, List<Match>> g in byGame)
            {
                List<Match> w = (g.Value ?? new List<Match>()).Where(m => m.Median > 0 && When(m) > now.AddDays(-7) && When(m) <= now).ToList();
                if (w.Count == 0 || (week != null && w.Count <= week.Count)) continue;
                best = g.Key; week = w;
                before = g.Value.Where(m => m.Median > 0 && When(m) > now.AddDays(-14) && When(m) <= now.AddDays(-7)).ToList();
            }
            if (week == null) return null;
            int avg = (int)Math.Round(week.Average(m => m.Median));
            string s = best + " esta semana: " + (week.Count == 1 ? "1 partida" : week.Count + " partidas") + ", " + avg + " FPS de media";
            if (before.Count > 0)
            {
                int pct = (int)Math.Round((avg - before.Average(m => m.Median)) * 100 / before.Average(m => m.Median));
                s += " (" + (pct >= 0 ? "+" : "") + pct + " % frente a la anterior)";
            }
            Match top = week.OrderByDescending(m => m.Median).First();
            return s + ". Mejor partida: " + top.Median + " FPS, " + days[(int)When(top).DayOfWeek] + ".";
        }
    }

    partial class Ui
    {
        // Once at start, after the region notice: in the app as a toast that opens Mis FPS, hidden in the tray as a balloon.
        internal async Task WeeklyAtStart(Task region)
        {
#if !SHOT
            try
            {
                await region;
                if (Prefs.Now.RegionDay == DateTime.Now.ToString("yyyy-MM-dd")) await Task.Delay(9000); // that toast may be on screen now
                if (!Weekly.Due(Prefs.Now.WeekDay, DateTime.Now)) return;
                string msg = await Task.Run(() => Weekly.Text(Game.All.ToDictionary(g => g.Name, g => FpsLog.Update(dir, g)), DateTime.Now));
                if (msg == null) return;
                Prefs.Now.WeekDay = DateTime.Now.ToString("yyyy-MM-dd");
                SavePrefs();
                ShowWeekly(msg);
            }
            catch (Exception) { }
#else
            await Task.Yield();
#endif
        }

        void ShowWeekly(string msg)
        {
            if (!w.IsVisible && tray != null)
            {
                tray.BalloonTipClicked += (s, e) => { Reveal(); Open("Mis FPS"); };
                tray.ShowBalloonTip(10000, "Tweakeroo", L.T(msg), System.Windows.Forms.ToolTipIcon.None);
                return;
            }
            Border t = F<Border>("Toast");
            Say(msg, false);
            int mine = toastNo; // not the text: Status shows it translated
            MouseButtonEventHandler go = null;
            go = (s, e) => { if (toastNo != mine) return; Open("Mis FPS"); HideToast(); };
            t.MouseLeftButtonUp += go;
            t.IsHitTestVisible = true;
            t.Cursor = Cursors.Hand;
            EventHandler off = null;
            off = (s, e) => { hide.Tick -= off; t.MouseLeftButtonUp -= go; t.IsHitTestVisible = false; t.Cursor = null; };
            hide.Tick += off;
        }
#if SHOT
        // "semana": the weekly toast with sample data.
        internal void WeeklyShot()
        {
            DateTime now = DateTime.Now;
            List<Match> ms = new List<Match>();
            for (int i = 0; i < 14; i++) ms.Add(new Match { Start = now.AddHours(-10 * i - 1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Median = 210 + (i * 7) % 31 });
            for (int i = 0; i < 10; i++) ms.Add(new Match { Start = now.AddDays(-8).AddHours(-10 * i).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Median = 205 });
            ShowWeekly(Weekly.Text(new Dictionary<string, List<Match>> { { "Fortnite", ms } }, now));
        }
#endif
    }
}
