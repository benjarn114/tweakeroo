using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Tablero
{
    // Crosshair geometry in device pixels around (0,0), the screen centre, which on an even-sized screen is a pixel corner.
    // An element of thickness t is centred on (0,0) when t is even and on the middle of the centre pixel (0.5,0.5) when t is odd,
    // so its edges always land on whole pixels and opposite arms mirror each other exactly around that centre.
    // Arms start Offset px from that centre (for odd t that is rounded half a pixel inward, so Offset 0 leaves no hole).
    public static class CrossShape
    {
        static int N(double v) { return (int)Math.Round(Math.Max(0, v), MidpointRounding.AwayFromZero); }
        static void Arms(List<KeyValuePair<Rect, double>> r, XLines l, bool noTop)
        {
            if (l == null || !l.Show || l.Opacity <= 0) return;
            int t = N(l.Thickness), len = N(l.Length), off = N(l.Offset);
            if (t == 0 || len == 0) return;
            double q = t % 2 == 1 ? 0.5 : 0, lo = q - t / 2.0, back = 2 * q - off - len;
            // right, left, bottom, top
            foreach (Rect x in new[] { new Rect(off, lo, len, t), new Rect(back, lo, len, t), new Rect(lo, off, t, len), new Rect(lo, back, t, len) }.Take(noTop ? 3 : 4))
                r.Add(new KeyValuePair<Rect, double>(x, l.Opacity));
        }
        // What is drawn in the crosshair's color, each with its opacity.
        public static List<KeyValuePair<Rect, double>> Fills(Crosshair c)
        {
            List<KeyValuePair<Rect, double>> r = new List<KeyValuePair<Rect, double>>();
            int d = N(c.DotThickness);
            if (c.Dot && d > 0 && c.DotOpacity > 0) { double q = d % 2 == 1 ? 0.5 : 0; r.Add(new KeyValuePair<Rect, double>(new Rect(q - d / 2.0, q - d / 2.0, d, d), c.DotOpacity)); }
            Arms(r, c.Inner, c.TStyle); // CS2's T style has no top line
            Arms(r, c.Outer, c.TStyle);
            return r;
        }
        // The black border: every fill grown by OutlineThickness on each side.
        public static List<Rect> Outlines(Crosshair c)
        {
            int o = N(c.OutlineThickness);
            if (!c.Outline || o == 0 || c.OutlineOpacity <= 0) return new List<Rect>();
            return Fills(c).Select(f => { Rect x = f.Key; x.Inflate(o, o); return x; }).ToList();
        }
        public static void Draw(DrawingContext dc, Crosshair c)
        {
            List<KeyValuePair<Rect, double>> f = Fills(c);
            if (f.Count == 0) return;
            List<Rect> o = Outlines(c);
            // Unions, so overlapping pieces are not darker; the border never shows under the color.
            if (o.Count > 0) dc.DrawGeometry(Paint(Colors.Black, c.OutlineOpacity), null, new CombinedGeometry(GeometryCombineMode.Exclude, Union(o), Union(f.Select(x => x.Key))));
            Color col = Color.FromRgb((byte)(c.Color >> 16), (byte)(c.Color >> 8), (byte)c.Color);
            foreach (IGrouping<double, KeyValuePair<Rect, double>> g in f.GroupBy(x => x.Value)) dc.DrawGeometry(Paint(col, g.Key), null, Union(g.Select(x => x.Key)));
        }
        static Geometry Union(IEnumerable<Rect> rs) { GeometryGroup g = new GeometryGroup { FillRule = FillRule.Nonzero }; foreach (Rect r in rs) g.Children.Add(new RectangleGeometry(r)); return g; }
        static Brush Paint(Color c, double a) { SolidColorBrush b = new SolidColorBrush(c) { Opacity = Math.Max(0, Math.Min(1, a)) }; b.Freeze(); return b; }
    }

    // Draws the crosshair at its real pixel size, centred on a whole device pixel. The same in the preview and over the game.
    public class CrossView : FrameworkElement
    {
        Crosshair x; double wide = 1;
        public Crosshair X { get { return x; } set { x = value; InvalidateVisual(); } }
        // Only the preview: how much the stretched resolution widens it.
        public double Wide { get { return wide; } set { wide = value; InvalidateVisual(); } }
        public CrossView() { IsHitTestVisible = false; SnapsToDevicePixels = true; RenderOptions.SetEdgeMode(this, EdgeMode.Aliased); }
        protected override void OnRender(DrawingContext dc)
        {
            if (x == null) return;
            PresentationSource ps = PresentationSource.FromVisual(this);
            Matrix m = ps != null && ps.CompositionTarget != null ? ps.CompositionTarget.TransformFromDevice : Matrix.Identity;
            double cx = Math.Floor(Math.Round(ActualWidth / m.M11) / 2) * m.M11, cy = Math.Floor(Math.Round(ActualHeight / m.M22) / 2) * m.M22;
            dc.PushTransform(new TranslateTransform(cx, cy));
            dc.PushTransform(new ScaleTransform(m.M11 * wide, m.M22));
            CrossShape.Draw(dc, x);
            dc.Pop();
            dc.Pop();
        }
    }

    // Only a window over the screen centre: it does not touch the game, its memory or its input. Clicks go through and it never takes focus.
    public class CrossOverlay : Window
    {
        const int Px = 256;
        const int ExStyle = -20, Transparent = 0x20, ToolWindow = 0x80, Layered = 0x80000, NoActivate = 0x8000000;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        public readonly CrossView View = new CrossView();
        public CrossOverlay(Crosshair x)
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual; UseLayoutRounding = true; SnapsToDevicePixels = true; Title = "Tweakeroo crosshair";
            View.X = x;
            Content = View;
            SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, ExStyle, GetWindowLong(h, ExStyle) | Transparent | Layered | ToolWindow | NoActivate);
                Place();
            };
            EventHandler moved = (s, e) => Dispatcher.BeginInvoke(new Action(Place));
            SystemEvents.DisplaySettingsChanged += moved;
            Closed += (s, e) => SystemEvents.DisplaySettingsChanged -= moved;
        }
        // Centred on the main screen in device pixels. With stretched the desktop is the low resolution, so the GPU stretches it like the game.
        public void Place()
        {
            PresentationSource ps = PresentationSource.FromVisual(this);
            if (ps == null || ps.CompositionTarget == null) return;
            Res r;
            try { r = Display.Current(); }
            catch (InvalidOperationException) { return; }
            Matrix m = ps.CompositionTarget.TransformFromDevice;
            Point at = m.Transform(new Point(r.W / 2 - Px / 2, r.H / 2 - Px / 2)), size = m.Transform(new Point(Px, Px));
            Left = at.X; Top = at.Y; Width = size.X; Height = size.Y;
            View.InvalidateVisual();
        }
    }

    partial class Ui
    {
        Circuit crossCircuit;   // Fortnite only, like Macros (GameUi.cs)
        Crosshair cross;        // the chosen saved crosshair: every edit goes to it (Prefs.CrossFix)
        CrossView crossView;
        CrossOverlay overlay;
        double crossRatio = 1;  // native aspect / Fortnite stretched aspect
        bool crossLoading;
        int crossEditing = -2;  // row whose name is being typed in the list: -1 the new one, -2 none
        readonly StackPanel crossList = new StackPanel();
        readonly TextBlock crossListErr = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(8, 4, 8, 4), Visibility = Visibility.Collapsed };
        readonly List<Action> crossShow = new List<Action>();
        readonly DispatcherTimer crossPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        readonly DispatcherTimer crossSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        // A click on a name loads it at once; the list closes once a double click (rename) can no longer come.
        readonly DispatcherTimer crossClose = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime) };
        // Valorant's colors, in its order.
        static readonly uint[] CrossColors = { 0xFFFFFF, 0x00FF00, 0x7FFF00, 0xDFFF00, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0xFF0000 };
        static readonly string[] CrossColorNames = { "Blanco", "Verde", "Amarillo verdoso", "Verde amarillento", "Amarillo", "Cian", "Rosa", "Rojo" };
        // Texts made in code, for the translation check (SelfTest.cs).
        internal static readonly string[] CrossWords = { "Mostrar", "Opacidad", "Grosor", "Longitud", "Desplazamiento", "Borrar", "Crosshair importado.", "Código copiado.", "No se pudo copiar: x", "+ Nuevo crosshair", "Nombre del crosshair", "Falta el nombre.", "Ya hay un crosshair con ese nombre." };

        static Crosshair CrossLoad(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return Crosshair.Deserialize(s); }
            catch (Exception) { return null; } // a damaged saved value is simply skipped
        }

        void StartCross()
        {
            cross = CrossLoad(Prefs.Now.CrossNow) ?? new Crosshair();
            crossView = new CrossView { X = cross };
            F<Grid>("CrossStage").Children.Add(crossView);
            // The game under the preview when the build has the screenshot; otherwise the gradient in the XAML stays.
            using (System.IO.Stream bg = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("crosshair-fondo.jpg"))
                if (bg != null)
                {
                    BitmapImage b = new BitmapImage();
                    b.BeginInit(); b.StreamSource = bg; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
                    F<Border>("CrossBox").Background = new ImageBrush(b) { Stretch = Stretch.UniformToFill }; // centred: its centre is the screen centre
                }
            Switch on = F<Switch>("CrossOn");
            on.State = Prefs.Now.CrossOn;
            on.Click += (s, e) => { Prefs.Now.CrossOn = on.State != true; on.State = Prefs.Now.CrossOn; SavePrefs(); CrossWatch(); };
            crossPoll.Tick += (s, e) => CrossTick();
            crossSave.Tick += (s, e) => CrossStore();
            w.Closed += (s, e) => { crossPoll.Stop(); if (overlay != null) overlay.Close(); CrossFlush(); };
            CheckBox wide = F<CheckBox>("CrossWide");
            wide.Click += (s, e) => crossView.Wide = wide.IsChecked == true ? crossRatio : 1;
            F<Btn>("CrossImport").Click += (s, e) => CrossImport();
            F<TextBox>("CrossCode").KeyDown += (s, e) => { if (e.Key == Key.Enter) CrossImport(); };
            F<Btn>("CrossCopy").Click += (s, e) =>
            {
                try { Clipboard.SetText(cross.ToValorantCode()); Say("Código copiado.", false); }
                catch (COMException ex) { Say("No se pudo copiar: " + ex.Message, false); }
            };

            Drop pick = F<Drop>("CrossPick");
            pick.ApplyTemplate();
            foreach (string n in new[] { "b", "card" }) ((Border)pick.Template.FindName(n, pick)).CornerRadius = new CornerRadius(10);
            ((Border)pick.Template.FindName("card", pick)).MinWidth = 220;
            crossListErr.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            pick.Panel = crossList;
            pick.Click += (s, e) => { if (e.OriginalSource != pick) return; // clicks inside the list bubble up to the Drop
            crossEditing = -2; crossClose.Stop(); crossListErr.Visibility = Visibility.Collapsed; ShowCrossList(); }; // runs before it opens or closes
            crossClose.Tick += (s, e) => { crossClose.Stop(); pick.Close(); };

            Style plain = F<Btn>("Rescan").Style; // the theme reuses the key "Btn" for a brush
            WrapPanel sw = F<WrapPanel>("CrossColors");
            for (int i = 0; i < CrossColors.Length; i++)
            {
                uint c = CrossColors[i];
                Btn b = new Btn { Style = plain, Width = 40, Height = 40, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 8), Tag = c, ToolTip = L.T(CrossColorNames[i]), Background = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)) };
                AutomationProperties.SetName(b, L.T(CrossColorNames[i]));
                b.Click += (s, e) => CrossEdit(() => cross.Color = c);
                sw.Children.Add(b);
            }
            TextBox hex = F<TextBox>("CrossHex");
            hex.TextChanged += (s, e) =>
            {
                string t = hex.Text.Trim().TrimStart('#');
                uint v;
                if (!crossLoading && t.Length == 6 && uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out v)) CrossEdit(() => cross.Color = v);
            };

            Panel o = F<Panel>("CrossOutline");
            CrossToggle(o, () => cross.Outline, v => cross.Outline = v);
            CrossSlider(o, "Opacidad", 0, 1, 0.05, () => cross.OutlineOpacity, v => cross.OutlineOpacity = v);
            CrossSlider(o, "Grosor", 1, 6, 1, () => cross.OutlineThickness, v => cross.OutlineThickness = v);
            Panel d = F<Panel>("CrossDot");
            CrossToggle(d, () => cross.Dot, v => cross.Dot = v);
            CrossSlider(d, "Opacidad", 0, 1, 0.05, () => cross.DotOpacity, v => cross.DotOpacity = v);
            CrossSlider(d, "Grosor", 1, 6, 1, () => cross.DotThickness, v => cross.DotThickness = v);
            CrossLines(F<Panel>("CrossInner"), () => cross.Inner);
            CrossLines(F<Panel>("CrossOuter"), () => cross.Outer);
            // One tab, one panel; the row always keeps one tab chosen.
            ListBox tabs = F<ListBox>("CrossTabs");
            Panel[] parts = { o, d, F<Panel>("CrossInner"), F<Panel>("CrossOuter") };
            SelectionChangedEventHandler tab = (s, e) =>
            {
                if (tabs.SelectedIndex < 0) { if (e != null && e.RemovedItems.Count > 0) tabs.SelectedItem = e.RemovedItems[0]; return; }
                for (int i = 0; i < parts.Length; i++) parts[i].Visibility = i == tabs.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
            };
            tabs.SelectionChanged += tab;
            tab(null, null);
            ShowCrossValues();
            ShowCrossList();
            CrossWatch();
        }

        void CrossLines(Panel p, Func<XLines> l)
        {
            CrossToggle(p, () => l().Show, v => l().Show = v);
            CrossSlider(p, "Opacidad", 0, 1, 0.05, () => l().Opacity, v => l().Opacity = v);
            CrossSlider(p, "Longitud", 0, 20, 1, () => l().Length, v => l().Length = v);
            CrossSlider(p, "Grosor", 0, 10, 1, () => l().Thickness, v => l().Thickness = v);
            CrossSlider(p, "Desplazamiento", 0, 20, 1, () => l().Offset, v => l().Offset = v);
        }
        void CrossToggle(Panel p, Func<bool> get, Action<bool> set)
        {
            CheckBox c = new CheckBox { Style = F<CheckBox>("NetSteady").Style, Content = L.T("Mostrar"), FontSize = 15 };
            c.Click += (s, e) => CrossEdit(() => set(c.IsChecked == true));
            crossShow.Add(() => c.IsChecked = get());
            p.Children.Add(c);
        }
        void CrossSlider(Panel p, string text, double min, double max, double step, Func<double> get, Action<double> set)
        {
            DockPanel row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            TextBlock name = new TextBlock { Text = L.T(text), Width = 140, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            TextBlock val = new TextBlock { Width = 64, TextAlignment = TextAlignment.Right, FontSize = 15, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Documents.Typography.SetNumeralAlignment(val, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(val, Dock.Right);
            Slider sl = new Slider { Style = F<Slider>("ColVib").Style, Minimum = min, Maximum = max, SmallChange = step, LargeChange = step < 1 ? 0.1 : 1, TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(sl, L.T(text));
            string fmt = step < 1 ? "0.00" : "0";
            sl.ValueChanged += (s, e) => { val.Text = sl.Value.ToString(fmt); CrossEdit(() => set(sl.Value)); };
            crossShow.Add(() => { sl.Value = Math.Max(min, Math.Min(max, get())); val.Text = sl.Value.ToString(fmt); });
            row.Children.Add(name);
            row.Children.Add(val);
            row.Children.Add(sl);
            p.Children.Add(row);
        }

        // Every change: preview and overlay at once, saved to the chosen crosshair shortly after.
        void CrossEdit(Action change)
        {
            if (crossLoading) return;
            change();
            CrossMarks();
            crossView.InvalidateVisual();
            if (overlay != null) overlay.View.InvalidateVisual();
            crossSave.Stop();
            crossSave.Start();
        }
        void SetCross(Crosshair c)
        {
            if (c == null) return;
            cross = c;
            crossView.X = c;
            if (overlay != null) overlay.View.X = c;
            ShowCrossValues();
            crossSave.Stop();
            crossSave.Start();
        }
        // Names are unique, so the name finds the chosen one in the list.
        int CrossIndex() { return Prefs.Now.CrossSaved.FindIndex(x => Crosshair.Deserialize(x).Name == cross.Name); }
        void CrossStore()
        {
            crossSave.Stop();
            string s = cross.Serialize();
            int at = CrossIndex();
            if (at < 0) Prefs.Now.CrossSaved.Add(s); else Prefs.Now.CrossSaved[at] = s;
            Prefs.Now.CrossNow = s;
            SavePrefs();
        }
        // A pending edit is saved before switching, so it never lands on another crosshair.
        void CrossFlush() { if (crossSave.IsEnabled) CrossStore(); }
        void ShowCrossValues()
        {
            crossLoading = true;
            foreach (Action a in crossShow) a();
            crossLoading = false;
            CrossMarks();
        }
        // The chosen swatch is outlined and the hex box shows the color, unless it is being typed in.
        void CrossMarks()
        {
            uint rgb = cross.Color & 0xFFFFFF;
            foreach (Btn b in F<WrapPanel>("CrossColors").Children)
            {
                bool on = (uint)b.Tag == rgb;
                b.SetResourceReference(Control.BorderBrushProperty, on ? "Ink" : "Line");
                b.BorderThickness = new Thickness(on ? 3 : 1.5);
            }
            TextBox hex = F<TextBox>("CrossHex");
            if (hex.IsKeyboardFocused) return;
            crossLoading = true;
            hex.Text = "#" + rgb.ToString("X6");
            crossLoading = false;
        }

        void ShowCross()
        {
            // Settings reset or imported meanwhile: back to the one chosen there.
            if (CrossIndex() < 0)
            {
                crossSave.Stop();
                Prefs.CrossFix(Prefs.Now);
                cross = CrossLoad(Prefs.Now.CrossNow) ?? new Crosshair();
                crossView.X = cross;
                if (overlay != null) overlay.View.X = cross;
            }
            crossRatio = 1;
            try
            {
                StretchSession fs;
                Res r = sessions.TryGetValue(Game.Fortnite.Id, out fs) && fs != null ? fs.Last : null;
                if (r != null && r.W > 0 && r.H > 0) crossRatio = Display.Saved().Aspect / r.Aspect;
            }
            catch (InvalidOperationException) { }
            CheckBox wide = F<CheckBox>("CrossWide");
            bool has = Math.Abs(crossRatio - 1) > 0.01;
            wide.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (!has) wide.IsChecked = false;
            crossView.Wide = wide.IsChecked == true ? crossRatio : 1;
            ShowCrossList();
            ShowCrossValues();
        }

        // The dropdown's list: names with the chosen one checked, × on hover, double click (or F2) renames, "+ Nuevo crosshair" at the bottom.
        void ShowCrossList()
        {
            Drop pick = F<Drop>("CrossPick");
            pick.Content = new TextBlock { Text = cross.Name, TextTrimming = TextTrimming.CharacterEllipsis }; // the player's name: not translated
            crossList.Children.Clear();
            Style row = (Style)w.FindResource("PopRow"), link = F<Btn>("ShowVersions").Style;
            List<string> l = Prefs.Now.CrossSaved;
            int at = CrossIndex();
            for (int i = 0; i < l.Count; i++)
            {
                int j = i;
                string name = Crosshair.Deserialize(l[i]).Name;
                if (crossEditing == j) { crossList.Children.Add(CrossNameBox(name, n => CrossRename(j, n))); continue; }
                Button r = new Button { Style = row };
                AutomationProperties.SetName(r, name);
                DockPanel dp = new DockPanel();
                System.Windows.Shapes.Path tick = new System.Windows.Shapes.Path { Data = (Geometry)w.FindResource("IcoCheck"), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 24, Height = 24 };
                tick.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "On");
                dp.Children.Add(new Viewbox { Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0), Child = tick, Visibility = j == at ? Visibility.Visible : Visibility.Hidden });
                if (l.Count > 1) // the last one stays
                {
                    Btn del = new Btn { Style = link, Content = "×", FontSize = 17, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(6, 0, 0, 0) };
                    AutomationProperties.SetName(del, L.T("Borrar") + " " + name);
                    del.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("IsMouseOver") { Source = r, Converter = new BooleanToVisibilityConverter() });
                    del.Click += (s, e) => { e.Handled = true; CrossDelete(j); };
                    DockPanel.SetDock(del, Dock.Right);
                    dp.Children.Add(del);
                }
                dp.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                r.Content = dp;
                r.Click += (s, e) => CrossPickAt(j);
                r.MouseDoubleClick += (s, e) => { e.Handled = true; CrossRenameStart(j); };
                r.KeyDown += (s, e) => { if (e.Key == Key.F2) CrossRenameStart(j); else if (e.Key == Key.Delete) CrossDelete(j); };
                crossList.Children.Add(r);
            }
            Border line = new Border { Height = 1, Margin = new Thickness(4) };
            line.SetResourceReference(Border.BackgroundProperty, "Line");
            crossList.Children.Add(line);
            if (crossEditing == -1) crossList.Children.Add(CrossNameBox("", CrossNew));
            else
            {
                Button add = new Button { Style = row, Content = new TextBlock { Text = L.T("+ Nuevo crosshair") } };
                add.Click += (s, e) => { crossClose.Stop(); crossEditing = -1; crossListErr.Visibility = Visibility.Collapsed; ShowCrossList(); };
                crossList.Children.Add(add);
            }
            crossList.Children.Add(crossListErr);
        }
        TextBox CrossNameBox(string start, Action<string> done)
        {
            TextBox t = new TextBox { Style = F<TextBox>("CrossHex").Style, Height = 34, MaxLength = 30, Text = start, Tag = L.T("Nombre del crosshair"), Margin = new Thickness(0, 1, 0, 1) };
            AutomationProperties.SetName(t, L.T("Nombre del crosshair"));
            t.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; done(t.Text); }
                else if (e.Key == Key.Escape) { e.Handled = true; crossEditing = -2; crossListErr.Visibility = Visibility.Collapsed; ShowCrossList(); }
            };
            t.Dispatcher.BeginInvoke(new Action(() => { t.Focus(); t.SelectAll(); }), DispatcherPriority.Input);
            return t;
        }
        void CrossRenameStart(int j) { crossClose.Stop(); crossEditing = j; crossListErr.Visibility = Visibility.Collapsed; ShowCrossList(); }
        // Empty or taken names stay in the box with the reason under it.
        bool CrossNameOk(string n, int self)
        {
            string bad = n == "" ? "Falta el nombre." : Prefs.Now.CrossSaved.Where((x, i) => i != self).Any(x => string.Equals(Crosshair.Deserialize(x).Name.Trim(), n, StringComparison.CurrentCultureIgnoreCase)) ? "Ya hay un crosshair con ese nombre." : null;
            crossListErr.Text = bad == null ? "" : L.T(bad);
            crossListErr.Visibility = bad == null ? Visibility.Collapsed : Visibility.Visible;
            return bad == null;
        }
        void CrossPickAt(int j)
        {
            CrossFlush();
            CrossUse(Crosshair.Deserialize(Prefs.Now.CrossSaved[j]));
            crossClose.Stop();
            crossClose.Start();
        }
        // A copy of the one on screen, with the new name, becomes the chosen one.
        void CrossNew(string text)
        {
            string n = text.Trim();
            if (!CrossNameOk(n, -1)) return;
            CrossFlush();
            Crosshair c = cross.Clone();
            c.Name = n;
            Prefs.Now.CrossSaved.Add(c.Serialize());
            crossEditing = -2;
            CrossUse(c);
            F<Drop>("CrossPick").Close(); // made and chosen: back to editing it
        }
        void CrossRename(int j, string text)
        {
            string n = text.Trim();
            if (!CrossNameOk(n, j)) return;
            CrossFlush();
            Crosshair c = Crosshair.Deserialize(Prefs.Now.CrossSaved[j]);
            bool now = j == CrossIndex();
            c.Name = n;
            Prefs.Now.CrossSaved[j] = c.Serialize();
            if (now) { cross.Name = n; Prefs.Now.CrossNow = Prefs.Now.CrossSaved[j]; }
            crossEditing = -2;
            SavePrefs();
            ShowCrossList();
        }
        void CrossDelete(int j)
        {
            List<string> l = Prefs.Now.CrossSaved;
            if (l.Count <= 1 || j >= l.Count) return; // the last one stays
            crossEditing = -2;
            CrossFlush();
            bool now = j == CrossIndex();
            l.RemoveAt(j);
            if (now) CrossUse(Crosshair.Deserialize(l[Math.Min(j, l.Count - 1)]));
            else { SavePrefs(); ShowCrossList(); }
        }
        // c is already in the saved list.
        void CrossUse(Crosshair c)
        {
            cross = c;
            crossView.X = c;
            if (overlay != null) overlay.View.X = c;
            ShowCrossValues();
            Prefs.Now.CrossNow = Prefs.Now.CrossSaved[Math.Max(0, CrossIndex())];
            SavePrefs();
            ShowCrossList();
        }

        void CrossImport()
        {
            TextBox box = F<TextBox>("CrossCode");
            TextBlock err = F<TextBlock>("CrossError");
            string code = box.Text.Trim();
            if (code == "") return;
            Crosshair c;
            try { c = Crosshair.FromCode(code); }
            catch (FormatException ex) { err.Text = ex.Message; err.Visibility = Visibility.Visible; return; }
            err.Visibility = Visibility.Collapsed;
            box.Text = "";
            c.Name = cross.Name; // the code changes the chosen crosshair's look, not its name
            SetCross(c);
            Say("Crosshair importado.", false);
        }

        // The overlay only exists while it is on, and only shows with Fortnite in front, so it never covers the desktop.
        void CrossWatch()
        {
            if (Prefs.Now.CrossOn) { crossPoll.Start(); CrossTick(); return; }
            crossPoll.Stop();
            if (overlay != null) overlay.Hide();
        }
        void CrossTick()
        {
            bool want = Prefs.Now.CrossOn && Macros.Front();
#if SHOT
            want = false; // tests never draw over the real screen
#endif
            if (!want) { if (overlay != null && overlay.IsVisible) overlay.Hide(); return; }
            if (overlay == null) overlay = new CrossOverlay(cross);
            if (!overlay.IsVisible) overlay.Show();
        }
    }
}
