using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace Tablero
{
    // Self-test of the match engine (Tracker.cs) and the shortcuts (Hotkeys.cs) with made-up samples:
    // no game, no ETW, nothing registered; files only in the temporary folder.
    static class TrackerTest
    {
        static void Is(bool ok, string what) { if (!ok) throw new Exception("FALLA: " + what); }
        static readonly DateTime T0 = new DateTime(2026, 11, 27, 20, 0, 0, DateTimeKind.Utc);
        static Sample S(int sec, double fps = 200, double worst = 6, int ping = 40, string hog = "svchost", double pct = 1, string disk = null, double mbs = 0)
        {
            return new Sample { Time = T0.AddSeconds(sec), Fps = fps, Low1 = fps * 0.8, WorstMs = worst, Ping = ping, Cpu = 30, CpuHog = hog, CpuHogPct = pct, DiskHog = disk, DiskHogMbs = mbs };
        }

        public static void Run(string dir)
        {
            // Stutters: a frame 3 times the usual one and at least 25 ms; without FPS there is no "usual".
            Is(!Tracker.Stutter(S(0, 200, 6)) && Tracker.Stutter(S(0, 200, 30)) && !Tracker.Stutter(S(0, 60, 40)) && Tracker.Stutter(S(0, 60, 60)) && !Tracker.Stutter(new Sample { WorstMs = 90 }), "partidas: tirones");
            Is(S(0, hog: "chrome", pct: 20).Culprit == "chrome" && S(0, hog: "x", pct: 1, disk: "MsMpEng", mbs: 50).Culprit == "MsMpEng" && S(0).Culprit == null, "partidas: culpable del momento");
            Is(Tracker.Clock(TimeSpan.FromSeconds(512)) == "8:32" && Tracker.Clock(TimeSpan.FromSeconds(-3)) == "0:00" && Tracker.Clock(TimeSpan.FromMinutes(75)) == "75:00", "partidas: minuto");

            // One Fortnite session: starting up (not known), lobby, a match of almost 10 minutes, lobby, a match too short.
            Play p = new Play(Game.Fortnite);
            List<MatchReport> got = new List<MatchReport>();
            List<double> none = new List<double>(), frames = Enumerable.Repeat(5.0, 99).Concat(new[] { 20.0, 5000.0 }).ToList(); // the 5 s pause is not a frame
            got.AddRange(p.Add(S(0), null, none));
            got.AddRange(p.Add(S(2), "", none));
            Is(!p.Mark(T0.AddSeconds(3)), "partidas: en el lobby no hay marca");
            for (int t = 10; t <= 600; t += 2)
            {
                Sample s = t == 100 ? S(t, worst: 40, hog: "chrome", pct: 20) : t == 150 ? S(t, worst: 50, hog: "chrome", pct: 30) : t == 200 ? S(t, worst: 45, hog: "Discord", pct: 10)
                    : t == 250 ? S(t, ping: 120, hog: "x", pct: 1, disk: "MsMpEng", mbs: 50) : t == 300 ? S(t, ping: -1) : S(t);
                got.AddRange(p.Add(s, "Battle Royale", frames));
            }
            Is(p.Mark(T0.AddSeconds(10 + 512)), "partidas: marca en partida");
            got.AddRange(p.Add(S(602), "", none));
            got.AddRange(p.Add(S(610), "Creativo", frames));
            got.AddRange(p.Add(S(640), "", none));
            got.AddRange(p.End(T0.AddSeconds(650)));
            Is(got.Count == 1, "partidas: una sola partida (el arranque, el lobby y la de 30 s no cuentan): " + got.Count);
            MatchReport r = got[0];
            Is(r.Game == "fortnite" && r.Map == "Battle Royale" && r.Start == T0.AddSeconds(10) && r.End == T0.AddSeconds(602), "partidas: datos de la partida");
            Is(r.Stutters.Count == 3 && r.Stutters[0].At == "1:30" && r.Stutters[0].Ms == 40 && r.Stutters[0].Culprit == "chrome" && r.Stutters[2].Culprit == "Discord", "partidas: lista de tirones");
            Is(r.Spikes.Count == 1 && r.Spikes[0].Ms == 120 && r.Spikes[0].At == "4:00" && r.Spikes[0].Culprit == "MsMpEng" && r.Ping == 40 && r.Loss > 0 && r.Loss < 1, "partidas: ping, picos y pérdida");
            Is(r.Culprit == "chrome" && r.CulpritTimes == 2, "partidas: culpable más repetido: " + r.Culprit + " " + r.CulpritTimes);
            Is(r.Marks.Count == 1 && r.Marks[0].At == "8:32" && r.Marks[0].Sample.Time == T0.AddSeconds(522), "partidas: marca situada por minuto");
            // 99 frames of 5 ms and one of 20 ms per sample: 194.2 FPS on average, and the slowest 1 % is the 20 ms frame.
            Is(Math.Abs(r.Fps.Value - 194.2) < 0.1 && Math.Abs(r.Low1.Value - 50) < 0.2, "partidas: FPS y 1 % bajo: " + r.Fps + " " + r.Low1);

            // A game that never says where the player is: the whole session is one report, without a map.
            Play q = new Play(Game.Cs2);
            for (int t = 0; t <= 100; t += 2) q.Add(S(t), null, none);
            List<MatchReport> all = q.End(T0.AddSeconds(102));
            Is(all.Count == 1 && all[0].Map == null && all[0].Fps == null && all[0].Stutters.Count == 0, "partidas: sin mapa, una por sesión");

            // Saved: the last 50 kept, newest first, and a report reads back whole.
            string d = Path.Combine(dir, "partidas-prueba");
            for (int i = 0; i < 52; i++) Tracker.Save(d, new MatchReport { Game = "cs2", Start = T0.AddMinutes(i), End = T0.AddMinutes(i + 1), Stutters = new List<Moment>(), Spikes = new List<Moment>(), Marks = new List<LagMark>() });
            List<MatchReport> kept = Tracker.Reports(d);
            Is(Directory.GetFiles(Tracker.Folder(d)).Length == 50 && kept.Count == 50 && kept[0].Start == T0.AddMinutes(51) && kept[49].Start == T0.AddMinutes(2) && Tracker.Latest(d).Start == T0.AddMinutes(51), "partidas: recorte a 50");
            string d2 = Path.Combine(dir, "partidas-prueba2");
            Is(Tracker.Latest(d2) == null && Tracker.Reports(d2).Count == 0, "partidas: sin informes");
            Tracker.Save(d2, r);
            MatchReport back = Tracker.Latest(d2);
            Is(back.Start == r.Start && back.Map == r.Map && back.Stutters.Count == 3 && back.Marks[0].At == "8:32" && back.Marks[0].Sample.CpuHog == "svchost" && back.Low1 == r.Low1 && back.Culprit == "chrome", "partidas: guardar y leer");

            // Shortcut texts
            ModifierKeys m;
            Key k;
            Is(Hotkeys.Parse("Ctrl+Shift+O", out m, out k) && m == (ModifierKeys.Control | ModifierKeys.Shift) && k == Key.O && Hotkeys.Text(m, k) == "Ctrl+Shift+O", "atajo: Ctrl+Shift+O");
            Is(Hotkeys.Parse(" ctrl + alt + 5 ", out m, out k) && k == Key.D5 && Hotkeys.Text(m, k) == "Ctrl+Alt+5", "atajo: número");
            Is(Hotkeys.Parse("Win+Shift+F9", out m, out k) && Hotkeys.Text(m, k) == "Shift+Win+F9" && Hotkeys.Parse("F9", out m, out k) && m == ModifierKeys.None, "atajo: orden y sin modificador");
            foreach (string bad in new[] { "Ctrl+", "Ctrl+Shift", "Foo+O", "Ctrl+LeftShift", "Ctrl+12", "Ctrl+-1", "", null })
                Is(!Hotkeys.Parse(bad, out m, out k), "atajo: no vale " + bad);
            Is(Hotkeys.Clean(null, "Ctrl+Shift+M") == "Ctrl+Shift+M" && Hotkeys.Clean("", "x") == "" && Hotkeys.Clean("shift+ctrl+m", "x") == "Ctrl+Shift+M" && Hotkeys.Clean("basura", "Ctrl+Shift+M") == "Ctrl+Shift+M", "atajo: limpiar");
            Prefs pr = Prefs.Parse("{\"MarkKey\":\"\",\"OverlayKey\":\"nada\"}");
            Is(Prefs.Default().MarkKey == "Ctrl+Shift+M" && Prefs.Default().OverlayKey == "Ctrl+Shift+O" && pr.MarkKey == "" && pr.OverlayKey == "Ctrl+Shift+O" && Prefs.Parse("{}").MarkKey == "Ctrl+Shift+M", "atajo: ajustes");

            bool en = L.En;
            L.En = true;
            try { Is(L.T(Tracker.NoTemp) != Tracker.NoTemp, "sin traducir: " + Tracker.NoTemp); }
            finally { L.En = en; }
        }
    }
}
