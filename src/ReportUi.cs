// "Mis FPS": the report of each match (engine in Tracker.cs), the notice when one ends and the sound of the lag mark.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Tablero
{
    // The texts of a report, pure: the self-test feeds them made-up reports. Each template goes through L.T, then gets its values.
    static class ReportText
    {
        static string F(string template, params object[] a) { return string.Format(CultureInfo.InvariantCulture, L.T(template), a); }
        public static string Exe(string name) { return name + ".exe"; }
        public static string N(double? v) { return v == null ? "—" : Math.Round(v.Value).ToString(CultureInfo.InvariantCulture); }

        // "hoy 17:20", "ayer 17:20" or "12/11 17:20" (with the year if it is another), local time.
        public static string When(DateTime utc, DateTime now)
        {
            DateTime t = utc.ToLocalTime();
            string h = t.ToString("HH:mm", CultureInfo.InvariantCulture);
            int days = (now.Date - t.Date).Days;
            return days == 0 ? F("hoy {0}", h) : days == 1 ? F("ayer {0}", h) : t.ToString(t.Year == now.Year ? "dd/MM" : "dd/MM/yyyy", CultureInfo.InvariantCulture) + " " + h;
        }

        // "Fortnite · Battle Royale · hoy 17:20 · 24 min"
        public static string Head(MatchReport r, DateTime now)
        {
            List<string> p = new List<string> { Game.Of(r.Game).Name };
            if (!string.IsNullOrEmpty(r.Map)) p.Add(r.Map);
            p.Add(When(r.Start, now));
            p.Add(Math.Max(1, (int)Math.Round((r.End - r.Start).TotalMinutes)) + " min"); // never "24" and "min" on two lines
            return string.Join(" · ", p);
        }

        // The program on top in the stutters; it has to be in at least half of them to be named.
        public static string Culprit(MatchReport r)
        {
            if (r.Fps == null) return L.T("Sin datos de FPS.");
            List<Moment> s = r.Stutters ?? new List<Moment>();
            if (s.Count == 0) return L.T("Sin tirones.");
            IGrouping<string, Moment> top = s.Where(m => m.Culprit != null).GroupBy(m => m.Culprit).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (top == null || top.Count() * 2 < s.Count) return L.T("Sin un programa culpable claro.");
            return s.Count == 1 ? F("{0} era el programa que más consumía en el tirón.", Exe(top.Key))
                : F("{0} era el programa que más consumía en {1} de {2} tirones.", Exe(top.Key), top.Count(), s.Count);
        }

        // "Marca de 8:32: 142 FPS, ping 41 ms, chrome.exe 38 % de CPU."
        public static string Mark(LagMark m)
        {
            Sample s = m.Sample;
            List<string> p = new List<string>();
            if (s != null)
            {
                if (s.Fps != null) p.Add(N(s.Fps) + " FPS");
                if (s.Ping >= 0) p.Add("ping " + s.Ping + " ms"); else if (s.Ping < 0) p.Add(L.T("ping perdido"));
                if (s.CpuHog != null && s.CpuHogPct >= 1) p.Add(F("{0} {1} % de CPU", Exe(s.CpuHog), N(s.CpuHogPct)));
                if (s.DiskHog != null && s.DiskHogMbs >= 5) p.Add(F("{0} {1} MB/s de disco", Exe(s.DiskHog), N(s.DiskHogMbs)));
            }
            return p.Count == 0 ? F("Marca de {0}: sin datos.", m.At) : F("Marca de {0}: {1}.", m.At, string.Join(", ", p));
        }

        // The notice, in Spanish: the toast translates itself (Tb), the tray balloon goes through L.T.
        public static string Done(MatchReport r)
        {
            int n = r.Stutters == null ? 0 : r.Stutters.Count;
            if (r.Fps == null) return "Partida terminada. El informe está en Mis FPS.";
            return "Partida terminada: " + N(r.Fps) + " FPS de media, " + (n == 1 ? "1 tirón." : n + " tirones.");
        }

        static void Is(bool ok, string what) { if (!ok) throw new Exception("FALLA: " + what); }
        static Moment M(int ms, string who) { return new Moment { At = "1:00", Ms = ms, Culprit = who }; }
        public static void Test()
        {
            bool en = L.En;
            L.En = false;
            try
            {
                List<Moment> seven = new[] { "chrome", "chrome", null, "chrome", "Discord", "chrome", "chrome" }.Select(x => M(40, x)).ToList();
                Is(Culprit(new MatchReport { Fps = 200, Stutters = seven }) == "chrome.exe era el programa que más consumía en 5 de 7 tirones.", "informe: culpable");
                Is(Culprit(new MatchReport { Fps = 200, Stutters = new List<Moment> { M(40, "chrome"), M(40, "Discord"), M(40, null) } }) == "Sin un programa culpable claro.", "informe: sin culpable claro");
                Is(Culprit(new MatchReport { Fps = 200, Stutters = new List<Moment> { M(40, null) } }) == "Sin un programa culpable claro." && Culprit(new MatchReport { Fps = 200, Stutters = new List<Moment> { M(40, "MsMpEng") } }) == "MsMpEng.exe era el programa que más consumía en el tirón.", "informe: un tirón");
                Is(Culprit(new MatchReport { Fps = 200, Stutters = new List<Moment>() }) == "Sin tirones." && Culprit(new MatchReport { Fps = 200 }) == "Sin tirones." && Culprit(new MatchReport()) == "Sin datos de FPS.", "informe: sin tirones");
                Is(Mark(new LagMark { At = "8:32", Sample = new Sample { Fps = 142.4, Ping = 41, CpuHog = "chrome", CpuHogPct = 38.2 } }) == "Marca de 8:32: 142 FPS, ping 41 ms, chrome.exe 38 % de CPU.", "informe: marca");
                Is(Mark(new LagMark { At = "0:10" }) == "Marca de 0:10: sin datos." && Mark(new LagMark { At = "2:00", Sample = new Sample { Ping = -1, CpuHog = "x", CpuHogPct = 0.2, DiskHog = "MsMpEng", DiskHogMbs = 40 } }) == "Marca de 2:00: ping perdido, MsMpEng.exe 40 MB/s de disco.", "informe: marca sin datos");
                DateTime now = new DateTime(2026, 11, 27, 20, 0, 0, DateTimeKind.Local);
                Func<DateTime, DateTime> u = x => x.ToUniversalTime();
                Is(When(u(new DateTime(2026, 11, 27, 17, 20, 0, DateTimeKind.Local)), now) == "hoy 17:20" && When(u(new DateTime(2026, 11, 26, 23, 5, 0, DateTimeKind.Local)), now) == "ayer 23:05"
                    && When(u(new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Local)), now) == "12/11 09:00" && When(u(new DateTime(2025, 12, 31, 9, 0, 0, DateTimeKind.Local)), now) == "31/12/2025 09:00", "informe: fecha relativa");
                MatchReport r = new MatchReport { Game = "fortnite", Map = "Battle Royale", Start = u(new DateTime(2026, 11, 27, 17, 20, 0, DateTimeKind.Local)), Fps = 212.4, Stutters = seven };
                r.End = r.Start.AddMinutes(24);
                Is(Head(r, now) == "Fortnite · Battle Royale · hoy 17:20 · 24 min" && Done(r) == "Partida terminada: 212 FPS de media, 7 tirones." && Done(new MatchReport()) == "Partida terminada. El informe está en Mis FPS.", "informe: cabecera y aviso");
                // Everything in English, nothing left without translation.
                L.En = true;
                int missing = L.Missing.Count;
                Is(Culprit(new MatchReport { Fps = 200, Stutters = seven }) == "chrome.exe was the program using the most in 5 of 7 stutters." && When(u(new DateTime(2026, 11, 26, 23, 5, 0, DateTimeKind.Local)), now) == "yesterday 23:05", "informe: en inglés");
                Is(Mark(new LagMark { At = "8:32", Sample = new Sample { Fps = 142, Ping = 41, CpuHog = "chrome", CpuHogPct = 38 } }) == "Mark at 8:32: 142 FPS, ping 41 ms, chrome.exe 38% CPU.", "informe: marca en inglés");
                foreach (string t in new[] { "Sin datos de FPS.", "Sin tirones.", "Sin un programa culpable claro.", "ping perdido", Done(r), Done(new MatchReport { Fps = 90, Stutters = new List<Moment> { M(40, null) } }), Done(new MatchReport()),
                    "Última partida", "Partida anterior", "Partidas anteriores", "FPS medio", "1 % bajo", "Tirones", "Ping", "picos: 3", "Probable culpable", "Ver detalle", "Ocultar detalle", "Tirón de 40 ms", "Pico de ping de 90 ms", "Sin culpable claro", "Se muestran 10 de 12." })
                    L.T(t);
                // Templates filled in English: a missing one is noted.
                Mark(new LagMark { At = "0:10" }); Mark(new LagMark { At = "2:00", Sample = new Sample { DiskHog = "a", DiskHogMbs = 9 } }); Culprit(new MatchReport { Fps = 1, Stutters = new List<Moment> { M(40, "a") } });
                Is(L.Missing.Count == missing, "informe: textos sin traducir: " + string.Join(" | ", L.Missing.Skip(missing)));
            }
            finally { L.En = en; }
        }
    }

    partial class Ui
    {
#if SHOT
        internal static List<MatchReport> ReportSample; // "informe": sample reports instead of the folder
#endif
        List<MatchReport> reports = new List<MatchReport>();
        MatchReport shownReport;
        bool reportOpen;                                   // "Ver detalle" open
        int reportToast = -1;                              // toastNo of the end-of-match notice
        System.Windows.Forms.NotifyIcon balloonOn;         // the tray icon whose balloon click is already wired

        void StartReport()
        {
            Border t = F<Border>("Toast");
            // Hit-testable only while a notice that opens My FPS shows (HideToast turns it off); another message in between does not open it.
            t.MouseLeftButtonUp += (s, e) => { if (toastNo != reportToast) return; HideToast(); Open("Mis FPS"); };
            Tracker.Finished += r => w.Dispatcher.BeginInvoke(new Action(() => Finished(r)));
            Tracker.Marked += () => Sound.Effect("done");
        }

        void Finished(MatchReport r)
        {
            try
            {
                if (!w.IsLoaded) return;
                if (F<FrameworkElement>("FpsPage").IsVisible) ShowReport();
                string msg = ReportText.Done(r);
                if (tray != null && (!w.IsVisible || w.WindowState == WindowState.Minimized))
                {
                    if (balloonOn != tray) { balloonOn = tray; tray.BalloonTipClicked += (s, e) => { Reveal(); Open("Mis FPS"); }; }
                    tray.ShowBalloonTip(8000, "Tweakeroo", L.T(msg), System.Windows.Forms.ToolTipIcon.None);
                    return;
                }
                Say(msg, false);
                reportToast = toastNo;
                Border t = F<Border>("Toast");
                t.IsHitTestVisible = true;
                t.Cursor = Cursors.Hand;
            }
            catch (Exception) { } // a notice must never break the app
        }

        // From ShowFps: the reports are read off the UI thread.
        async void ShowReport()
        {
            List<MatchReport> got = null;
#if SHOT
            got = ReportSample;
#endif
            if (got == null) got = await Task.Run(() => Tracker.Reports());
            reports = got;
            shownReport = got.FirstOrDefault();
            DrawReport();
        }
        internal void ShowReportDetail() { reportOpen = true; DrawReport(); }

        Label HeadLabel(string text, double top)
        {
            return new Label { Style = (Style)w.FindResource("Head"), Content = L.T(text), Margin = new Thickness(0, top, 0, 0), Padding = new Thickness(0) };
        }
        static TextBlock Line(string text, double size, FontWeight weight, bool soft)
        {
            TextBlock t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
            if (soft) t.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
            return t;
        }
        FrameworkElement Tile(string label, string main, string sub)
        {
            StackPanel p = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            p.Children.Add(HeadLabel(label, 0));
            TextBlock v = Line(main, 24, FontWeights.Bold, false);
            System.Windows.Documents.Typography.SetNumeralAlignment(v, FontNumeralAlignment.Tabular);
            p.Children.Add(v);
            if (sub != null) p.Children.Add(Line(sub, 13, FontWeights.Normal, true));
            return p;
        }

        void DrawReport()
        {
            Border card = F<Border>("ReportCard");
            MatchReport r = shownReport;
            card.Visibility = r == null ? Visibility.Collapsed : Visibility.Visible;
            if (r == null) { card.Child = null; return; }
            DateTime now = DateTime.Now;
            StackPanel p = new StackPanel();

            // The dropdown goes in its own row: beside it, the head line wrapped at 1280 px.
            p.Children.Add(HeadLabel(r == reports.FirstOrDefault() ? "Última partida" : "Partida anterior", 0));
            p.Children.Add(Line(ReportText.Head(r, now), 15, FontWeights.SemiBold, false));
            if (reports.Count > 1) { Drop d = PastDrop(now); d.HorizontalAlignment = HorizontalAlignment.Left; d.Margin = new Thickness(0, 12, 0, 0); p.Children.Add(d); }

            int stutters = r.Stutters == null ? 0 : r.Stutters.Count, spikes = r.Spikes == null ? 0 : r.Spikes.Count;
            UniformGrid tiles = new UniformGrid { Rows = 1, Margin = new Thickness(0, 16, 0, 0) };
            tiles.Children.Add(Tile("FPS medio", ReportText.N(r.Fps), null));
            tiles.Children.Add(Tile("1 % bajo", ReportText.N(r.Low1), null));
            tiles.Children.Add(Tile("Tirones", r.Fps == null ? "—" : stutters.ToString(CultureInfo.InvariantCulture), null));
            tiles.Children.Add(Tile("Ping", r.Ping == null ? "—" : r.Ping + " ms", r.Ping == null ? null : string.Format(L.T("picos: {0}"), spikes)));
            p.Children.Add(tiles);

            p.Children.Add(HeadLabel("Probable culpable", 16));
            p.Children.Add(Line(ReportText.Culprit(r), 15, FontWeights.SemiBold, false));
            foreach (LagMark m in r.Marks ?? new List<LagMark>())
            {
                TextBlock t = Line(ReportText.Mark(m), 14, FontWeights.Normal, false);
                t.Margin = new Thickness(0, 6, 0, 0);
                p.Children.Add(t);
            }

            List<KeyValuePair<Moment, bool>> all = (r.Stutters ?? new List<Moment>()).Select(x => new KeyValuePair<Moment, bool>(x, true))
                .Concat((r.Spikes ?? new List<Moment>()).Select(x => new KeyValuePair<Moment, bool>(x, false))).OrderBy(x => x.Key.Time).ToList();
            if (all.Count > 0)
            {
                StackPanel detail = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Visibility = reportOpen ? Visibility.Visible : Visibility.Collapsed };
                foreach (KeyValuePair<Moment, bool> x in all.Take(10))
                {
                    Grid g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    TextBlock at = Line(x.Key.At, 14, FontWeights.SemiBold, false);
                    System.Windows.Documents.Typography.SetNumeralAlignment(at, FontNumeralAlignment.Tabular);
                    TextBlock what = Line(string.Format(L.T(x.Value ? "Tirón de {0} ms" : "Pico de ping de {0} ms"), x.Key.Ms), 14, FontWeights.Normal, false);
                    TextBlock who = Line(x.Key.Culprit == null ? L.T("Sin culpable claro") : ReportText.Exe(x.Key.Culprit), 14, FontWeights.Normal, true);
                    Grid.SetColumn(what, 1);
                    Grid.SetColumn(who, 2);
                    g.Children.Add(at); g.Children.Add(what); g.Children.Add(who);
                    detail.Children.Add(g);
                }
                if (all.Count > 10) { TextBlock more = Line(string.Format(L.T("Se muestran 10 de {0}."), all.Count), 13, FontWeights.Normal, true); more.Margin = new Thickness(0, 6, 0, 0); detail.Children.Add(more); }
                Btn more2 = new Btn { Style = (Style)w.FindResource("LinkBtn"), Content = L.T(reportOpen ? "Ocultar detalle" : "Ver detalle"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                more2.Click += (s, e) =>
                {
                    reportOpen = !reportOpen;
                    detail.Visibility = reportOpen ? Visibility.Visible : Visibility.Collapsed;
                    more2.Content = L.T(reportOpen ? "Ocultar detalle" : "Ver detalle");
                    if (reportOpen) for (int i = 0; i < detail.Children.Count; i++) Fx.Enter((FrameworkElement)detail.Children[i], i, 10);
                };
                p.Children.Add(more2);
                p.Children.Add(detail);
            }
            card.Child = p;
        }

        // The last reports (up to 50), the one on screen ticked, as the crosshair list does.
        Drop PastDrop(DateTime now)
        {
            Drop d = new Drop { Style = (Style)w.FindResource("Drop"), Height = 38, Width = 300, MinWidth = 220, FontSize = 14.5, VerticalAlignment = VerticalAlignment.Top, Content = new TextBlock { Text = L.T("Partidas anteriores") } };
            AutomationProperties.SetName(d, L.T("Partidas anteriores"));
            Style row = (Style)w.FindResource("PopRow");
            StackPanel list = new StackPanel();
            foreach (MatchReport x in reports.Take(Tracker.Keep))
            {
                MatchReport pick = x;
                string name = ReportText.Head(x, now);
                Button b = new Button { Style = row };
                AutomationProperties.SetName(b, name);
                DockPanel dp = new DockPanel();
                System.Windows.Shapes.Path tick = new System.Windows.Shapes.Path { Data = (System.Windows.Media.Geometry)w.FindResource("IcoCheck"), StrokeThickness = 3, StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round, StrokeLineJoin = System.Windows.Media.PenLineJoin.Round, Width = 24, Height = 24 };
                tick.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "On");
                dp.Children.Add(new Viewbox { Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0), Child = tick, Visibility = x == shownReport ? Visibility.Visible : Visibility.Hidden });
                dp.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                b.Content = dp;
                b.Click += (s, e) => { d.Close(); shownReport = pick; reportOpen = false; DrawReport(); };
                list.Children.Add(b);
            }
            d.Panel = new ScrollViewer { Content = list, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
            return d;
        }

#if SHOT
        // A realistic match for the screenshots: 7 stutters (5 with Chrome), 3 ping spikes and a mark; and two older ones for the list.
        internal static List<MatchReport> SampleReports()
        {
            DateTime end = DateTime.UtcNow.AddMinutes(-12), start = end.AddMinutes(-24);
            Func<DateTime, int, int, string, Moment> at = (t0, sec, ms, who) => new Moment { Time = t0.AddSeconds(sec), At = Tracker.Clock(TimeSpan.FromSeconds(sec)), Ms = ms, Culprit = who };
            MatchReport a = new MatchReport
            {
                Game = "fortnite", Map = "Battle Royale", Start = start, End = end, Fps = 212.4, Low1 = 118.6, Ping = 38, Loss = 0.4,
                Stutters = new List<Moment> { at(start, 95, 41, "chrome"), at(start, 212, 48, "chrome"), at(start, 388, 37, null), at(start, 512, 52, "chrome"), at(start, 731, 39, "Discord"), at(start, 905, 44, "chrome"), at(start, 1240, 46, "chrome") },
                Spikes = new List<Moment> { at(start, 260, 96, null), at(start, 514, 121, "chrome"), at(start, 1012, 88, "OneDrive") },
                Culprit = "chrome", CulpritTimes = 6,
                Marks = new List<LagMark> { new LagMark { Time = start.AddSeconds(512), At = "8:32", Sample = new Sample { Time = start.AddSeconds(512), Fps = 142, Ping = 41, CpuHog = "chrome", CpuHogPct = 38.2 } } }
            };
            DateTime y = DateTime.UtcNow.AddDays(-1).AddHours(-2), o = DateTime.UtcNow.AddDays(-4);
            MatchReport b = new MatchReport { Game = "valorant", Map = "Ascent", Start = y, End = y.AddMinutes(38), Fps = 301, Low1 = 164, Ping = 22, Stutters = new List<Moment>(), Spikes = new List<Moment> { at(y, 600, 71, null) }, Marks = new List<LagMark>() };
            MatchReport c = new MatchReport { Game = "cs2", Start = o, End = o.AddMinutes(51), Fps = 287, Low1 = 140, Ping = 31, Stutters = new List<Moment> { at(o, 300, 33, null), at(o, 900, 36, "MsMpEng") }, Spikes = new List<Moment>(), Marks = new List<LagMark>() };
            return new List<MatchReport> { a, b, c };
        }
#endif
    }
}
