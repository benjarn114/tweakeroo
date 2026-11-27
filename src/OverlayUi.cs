// Settings of the performance overlay (folded card at the end of "Al jugar"), the lag mark shortcut in Settings, and when
// the overlay window exists: only while a game is open (Tracker samples it) and the overlay is on. Its shortcut is held only
// while a game is open, like Tracker's mark shortcut: a global shortcut takes the combination from every other program.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tablero
{
    partial class Ui
    {
        PerfOverlay perf;
        readonly OverlayView perfPreview = new OverlayView();
        Viewbox perfFit; // the preview, smaller only when it does not fit (a long line with every datum)
        readonly List<double> perfGraph = new List<double>();
        readonly DispatcherTimer perfGone = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        readonly Dictionary<string, CheckBox> perfChecks = new Dictionary<string, CheckBox>();
        Tb perfWhy;
        Sample perfLast;
        bool perfGame, perfOpen, perfTemp, perfTempKnown;
        const string PerfId = "overlay", MarkId = "marca"; // MarkId: the id Tracker holds the mark shortcut with

        // Texts made in code, for the translation check (SelfTest.cs).
        internal static IEnumerable<string> OverlayWords()
        {
            return OverlayModel.Names.Concat(OverlayModel.Labels.Where(x => x != null)).Concat(OverlayModel.Styles).Concat(OverlayModel.Corners).Concat(new[] { HotkeyBox.Waiting, HotkeyBox.None, HotkeyBox.Taken, Tracker.NoTemp });
        }

        void StartOverlay()
        {
#if SHOT
            // "overlay A|B|C [todo] [esquina N]": the card open with that style (and every datum); "atajo": Settings at the mark shortcut.
            string[] a = Environment.GetCommandLineArgs();
            int sa = Array.IndexOf(a, "overlay");
            if (sa >= 0 && sa + 1 < a.Length) Prefs.Now.OverlayStyle = Math.Max(0, "ABC".IndexOf(a[sa + 1], StringComparison.Ordinal));
            if (Array.IndexOf(a, "todo") >= 0) Prefs.Now.OverlayShow = OverlayModel.Ids.ToList();
            int sc = Array.IndexOf(a, "esquina");
            if (sc >= 0) Prefs.Now.OverlayCorner = int.Parse(a[sc + 1]);
#endif
            F<Button>("PerfHead").Click += (s, e) => PerfFold(!perfOpen);
            Toggle("PerfOn", () => Prefs.Now.OverlayOn, v => { Prefs.Now.OverlayOn = v; PerfShow(); });

            HotkeyBox key = F<HotkeyBox>("PerfKey"), mark = F<HotkeyBox>("SetMarkKey");
            key.Note = F<TextBlock>("PerfKeyNote");
            mark.Note = F<TextBlock>("SetMarkKeyNote");
            key.Pick = t => KeyTake(PerfId, t, Prefs.Now.OverlayKey, Prefs.Now.MarkKey, PerfToggle, v => Prefs.Now.OverlayKey = v);
            // A new mark shortcut applies at once with a game open (Tracker would only read it at the next game).
            mark.Pick = t => KeyTake(MarkId, t, Prefs.Now.MarkKey, Prefs.Now.OverlayKey, () => Tracker.Mark(), v => Prefs.Now.MarkKey = v);
            Action keys = () => { key.Value = Prefs.Now.OverlayKey; mark.Value = Prefs.Now.MarkKey; };
            keys();
            refresh.Add(() =>
            {
                keys();
                if (perfGame) { Hotkeys.Bind(PerfId, Prefs.Now.OverlayKey, PerfToggle); Hotkeys.Bind(MarkId, Prefs.Now.MarkKey, () => Tracker.Mark()); }
                PerfAll();
            });

            PerfDrop("PerfStyle", "PerfStyles", OverlayModel.Styles, () => Prefs.Now.OverlayStyle, v => Prefs.Now.OverlayStyle = v);
            PerfDrop("PerfCorner", "PerfCorners", OverlayModel.Corners, () => Prefs.Now.OverlayCorner, v => Prefs.Now.OverlayCorner = v);

            Style check = (Style)w.FindResource("Check");
            StackPanel rows = F<StackPanel>("PerfShow");
            for (int i = 0; i < OverlayModel.Ids.Length; i++)
            {
                string id = OverlayModel.Ids[i];
                CheckBox c = new CheckBox { Style = check, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Automation.AutomationProperties.SetName(c, L.T(OverlayModel.Names[i]));
                c.Click += (s, e) =>
                {
                    Prefs.Now.OverlayShow.Remove(id);
                    if (c.IsChecked == true) Prefs.Now.OverlayShow.Add(id);
                    SavePrefs();
                    PerfAll();
                };
                perfChecks[id] = c;
                DockPanel d = new DockPanel();
                DockPanel.SetDock(c, Dock.Right);
                d.Children.Add(c);
                StackPanel t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(new Tb { Text = OverlayModel.Names[i], FontSize = 16, FontWeight = FontWeights.Bold });
                if (id == OverlayModel.CpuTemp)
                {
                    perfWhy = new Tb { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
                    perfWhy.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
                    t.Children.Add(perfWhy);
                }
                d.Children.Add(t);
                Border row = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(0, 0, 0, 8), Child = d };
                row.SetResourceReference(Border.BackgroundProperty, "Card");
                rows.Children.Add(row);
            }

            perfFit = new Viewbox { Child = perfPreview, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(16) };
            F<Grid>("PerfStageGrid").Children.Add(perfFit);
            // The game under the preview when the build has the screenshot; otherwise the gradient in the XAML stays.
            using (System.IO.Stream bg = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("crosshair-fondo.jpg"))
                if (bg != null)
                {
                    BitmapImage b = new BitmapImage();
                    b.BeginInit(); b.StreamSource = bg; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
                    F<Border>("PerfStage").Background = new ImageBrush(b) { Stretch = Stretch.UniformToFill };
                }

            // The first read of the CPU temperature takes tens of ms: off the UI thread.
            Task.Run(() => Tracker.CpuTempAvailable).ContinueWith(t => w.Dispatcher.BeginInvoke(new Action(() => { perfTemp = !t.IsFaulted && t.Result; perfTempKnown = true; PerfAll(); })));
            Tracker.Sampled += s => w.Dispatcher.BeginInvoke(new Action(() => PerfSample(s)));
            perfGone.Tick += (s, e) => { if (Tracker.Last == null) PerfEnd(); };
            w.Closed += (s, e) => { perfGone.Stop(); PerfClose(); };
            PerfAll();
#if SHOT
            if (sa >= 0) PerfFold(true);
            int at = Array.IndexOf(a, "atajo");
            if ((sa >= 0 || at >= 0) && Array.IndexOf(a, "fin") < 0) // "fin": the page end, as App does
                w.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    ScrollViewer sv = F<ScrollViewer>(sa >= 0 ? "BoostPage" : "SettingsPage");
                    FrameworkElement to = F<FrameworkElement>(sa >= 0 ? "PerfHead" : "SetMarkRow");
                    for (int i = 0; i < 50 && !sv.IsVisible; i++) await Task.Delay(100);
                    await Task.Delay(500);
                    sv.ScrollToVerticalOffset(to.TransformToAncestor((Visual)sv.Content).Transform(new Point(0, 0)).Y - (sa >= 0 ? 12 : 160));
                }));
#endif
        }

        void PerfDrop(string drop, string list, string[] names, Func<int> get, Action<int> set)
        {
            Drop d = F<Drop>(drop);
            ListBox l = F<ListBox>(list);
            Choice(l, names.Select((x, i) => new LimitOption { Name = x, Value = i.ToString() }).ToList(), () => get().ToString(), v => set(int.Parse(v)));
            d.Content = l.SelectedItem;
            l.SelectionChanged += (s, e) => { d.Content = l.SelectedItem; PerfAll(); };
            // A click or Enter on a row closes, as in the music dropdown.
            l.PreviewMouseLeftButtonUp += (s, e) => { if (ItemsControl.ContainerFromElement(l, e.OriginalSource as DependencyObject) != null) d.Close(); };
            l.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { d.Close(); e.Handled = true; } };
        }

        // Whether t can be the shortcut id: not the app's other shortcut, and free in Windows. With a game open it is held at once;
        // if Windows refuses it, the previous one is held again.
        bool KeyTake(string id, string t, string old, string other, Action act, Action<string> save)
        {
            bool ok;
            if (t != "" && t == other) ok = false;
            else if (perfGame) { ok = Hotkeys.Bind(id, t, act); if (!ok) Hotkeys.Bind(id, old, act); }
            else { ok = t == "" || Hotkeys.Bind("prueba", t, () => { }); Hotkeys.Clear("prueba"); }
            if (ok) { save(t); SavePrefs(); }
            return ok;
        }

        // The rows, the preview and the window, after any change.
        void PerfAll()
        {
            bool temp = perfTemp || !perfTempKnown; // until it is known the option is not taken away
            foreach (KeyValuePair<string, CheckBox> c in perfChecks)
            {
                c.Value.IsEnabled = OverlayModel.Enabled(c.Key, temp);
                c.Value.IsChecked = c.Value.IsEnabled && Prefs.Now.OverlayShow.Contains(c.Key);
                c.Value.Opacity = c.Value.IsEnabled ? 1 : 0.45;
            }
            if (perfWhy != null) { perfWhy.Text = temp ? "" : Tracker.CpuTempWhy; perfWhy.Visibility = temp ? Visibility.Collapsed : Visibility.Visible; }
            int corner = Prefs.Now.OverlayCorner;
            perfFit.HorizontalAlignment = corner % 2 == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            perfFit.VerticalAlignment = corner < 2 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            perfPreview.Show(Prefs.Now.OverlayStyle, Prefs.Now.OverlayShow, temp, OverlayModel.Example(), OverlayModel.ExampleGraph(), DateTime.Today.AddHours(17).AddMinutes(24));
            PerfShow();
        }

        // Opens inside the page, pushing the rest down, as "Todos los juegos" in Play; without animations at once.
        void PerfFold(bool open)
        {
            perfOpen = open;
            Border box = F<Border>("PerfBox");
            FrameworkElement chev = F<FrameworkElement>("PerfChev");
            RotateTransform r = chev.RenderTransform as RotateTransform;
            if (r == null || r.IsFrozen) { r = new RotateTransform(); chev.RenderTransform = r; }
            Fx.To(r, RotateTransform.AngleProperty, open ? 180 : 0, open ? 480 : 180, open ? Fx.Spring : Fx.Out);
            if (!Fx.On)
            {
                box.BeginAnimation(FrameworkElement.HeightProperty, null);
                box.Height = double.NaN;
                box.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            box.Height = box.Visibility == Visibility.Visible ? box.ActualHeight : 0;
            box.Visibility = Visibility.Visible;
            FrameworkElement inner = (FrameworkElement)box.Child;
            inner.Measure(new Size(((FrameworkElement)box.Parent).ActualWidth, double.PositiveInfinity));
            Fx.To(box, FrameworkElement.HeightProperty, open ? inner.DesiredSize.Height : 0, open ? 420 : 220, open ? Fx.Out : Fx.In, 0, () =>
            {
                if (perfOpen != open) return;
                box.BeginAnimation(FrameworkElement.HeightProperty, null);
                box.Height = double.NaN;
                if (!open) box.Visibility = Visibility.Collapsed;
            });
        }

        // ---- The window over the game ----
        void PerfSample(Sample s)
        {
            if (Tracker.Last == null) return; // came after the game closed
            if (!perfGame)
            {
                perfGame = true;
                perfGraph.Clear();
                Hotkeys.Bind(PerfId, Prefs.Now.OverlayKey, PerfToggle);
                perfGone.Start();
            }
            double? ms = OverlayModel.FrameMs(s);
            if (ms != null) { perfGraph.Add(ms.Value); if (perfGraph.Count > OverlayModel.Points) perfGraph.RemoveAt(0); }
            perfLast = s;
            PerfShow();
        }
        void PerfEnd()
        {
            perfGame = false;
            perfLast = null;
            perfGone.Stop();
            Hotkeys.Clear(PerfId);
            PerfClose();
        }
        void PerfToggle()
        {
            Prefs.Now.OverlayOn = !Prefs.Now.OverlayOn;
            F<CheckBox>("PerfOn").IsChecked = Prefs.Now.OverlayOn;
            SavePrefs();
            PerfShow();
        }
        void PerfShow()
        {
            bool want = perfGame && perfLast != null && Prefs.Now.OverlayOn;
#if SHOT
            want = false; // tests never draw over the real screen
#endif
            if (!want) { PerfClose(); return; }
            bool fresh = perf == null;
            if (fresh) perf = new PerfOverlay { FontFamily = w.FontFamily };
            perf.Resources["On"] = w.TryFindResource("On"); // the window has no palette of its own: the graph line takes the app's
            perf.Corner = Prefs.Now.OverlayCorner;
            perf.View.Show(Prefs.Now.OverlayStyle, Prefs.Now.OverlayShow, perfTemp, perfLast, perfGraph, DateTime.Now);
            if (fresh) perf.Show(); else perf.Place();
        }
        void PerfClose() { if (perf != null) { perf.Close(); perf = null; } }
    }
}
