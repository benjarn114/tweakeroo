// Performance overlay: what is shown (OverlayModel, pure: the self-test checks it), the view (the same one in the settings
// preview and over the game) and the window, the crosshair's system: transparent, lets clicks through, nothing injected.
// The settings and when the window exists: OverlayUi.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Tablero
{
    // One datum as drawn: Label only in styles A and B; Unit small after the number.
    public class OverlayItem
    {
        public string Id, Label, Num, Unit;
    }

    public static class OverlayModel
    {
        public const string Graph = "graph", CpuTemp = "cputemp";
        public const int Points = 60; // samples in the frametime graph (2 min)
        // Canonical order: also the order of "Qué mostrar".
        public static readonly string[] Ids = { "fps", "low", "ping", "loss", "gputemp", "gpu", CpuTemp, "cpu", "ram", "vram", Graph, "time" };
        public static readonly string[] Names = { "FPS", "1 % bajo", "Ping", "Pérdida", "Temperatura GPU", "Uso GPU", "Temperatura CPU", "Uso CPU", "RAM", "VRAM", "Gráfico de frametime", "Hora" };
        internal static readonly string[] Labels = { "FPS", "1%", "PING", "PÉRD", "GPU °C", "GPU", "CPU °C", "CPU", "RAM", "VRAM", null, null };
        public static readonly string[] Styles = { "Una línea", "Columna", "Solo números" };
        public static readonly string[] Corners = { "Arriba a la izquierda", "Arriba a la derecha", "Abajo a la izquierda", "Abajo a la derecha" };
        public static List<string> Defaults() { return new List<string> { "fps", "low", "ping" }; }

        // Without the CPU temperature (Tracker.CpuTempAvailable) its switch is disabled and it is never shown.
        public static bool Enabled(string id, bool cpuTemp) { return id != CpuTemp || cpuTemp; }

        // What is drawn, in order. Column (1) puts the graph at the end, across the whole width; the others keep it in its place.
        public static List<string> Layout(int style, IEnumerable<string> shown, bool cpuTemp)
        {
            HashSet<string> on = new HashSet<string>(shown ?? new string[0]);
            List<string> r = Ids.Where(x => on.Contains(x) && Enabled(x, cpuTemp)).ToList();
            if (style == 1 && r.Remove(Graph)) r.Add(Graph);
            return r;
        }

        // A value that could not be read is "—" without a unit. In styles A and B the label already says it is FPS.
        public static OverlayItem Item(string id, int style, Sample s, DateTime local)
        {
            int i = Array.IndexOf(Ids, id);
            OverlayItem it = new OverlayItem { Id = id, Label = i < 0 ? null : L.T(Labels[i]) };
            s = s ?? new Sample();
            switch (id)
            {
                case "fps": Set(it, s.Fps, "0", style == 2 ? "FPS" : null); break;
                case "low": Set(it, s.Low1, "0", style == 2 ? "1%" : null); break;
                case "ping": Set(it, s.Ping >= 0 ? s.Ping : null, "0", "ms"); break; // -1 = lost
                case "loss": Set(it, s.Loss, s.Loss > 0 && s.Loss < 10 ? "0.#" : "0", "%"); break;
                case "gputemp": Set(it, s.GpuTemp, "0", "°C"); break;
                case "gpu": Set(it, s.Gpu, "0", "%"); break;
                case CpuTemp: Set(it, s.CpuTemp, "0", "°C"); break;
                case "cpu": Set(it, s.Cpu, "0", "%"); break;
                case "ram": Set(it, s.RamMb / 1024, "0.0", "GB"); break;
                case "vram": Set(it, s.VramMb / 1024, "0.0", "GB"); break;
                case "time": it.Num = local.ToString("HH:mm", CultureInfo.InvariantCulture); break;
                default: it.Num = "—"; break;
            }
            return it;
        }
        static void Set(OverlayItem it, double? v, string fmt, string unit)
        {
            if (v == null) { it.Num = "—"; return; }
            it.Num = v.Value.ToString(fmt, CultureInfo.InvariantCulture);
            if (!L.En) it.Num = it.Num.Replace(".", ",");
            it.Unit = unit;
        }

        // One point per sample: its slowest frame, or the average frame when there is no slowest.
        public static double? FrameMs(Sample s) { return s.WorstMs ?? (s.Fps > 0 ? 1000 / s.Fps : null); }

        // Sample data for the settings preview.
        public static Sample Example() { return new Sample { Fps = 144, Low1 = 98, Ping = 23, Loss = 0, GpuTemp = 64, Gpu = 97, CpuTemp = 58, Cpu = 41, RamMb = 9318, VramMb = 6144 }; }
        public static List<double> ExampleGraph() { return Enumerable.Range(0, Points).Select(i => i == 41 ? 19.5 : 7.2 + 0.9 * Math.Sin(i * 0.7) + 0.5 * Math.Sin(i * 2.3)).ToList(); }
    }

    // The overlay drawn from a sample. White text on its own (it goes over the game); only the graph line takes the palette's "On".
    public class OverlayView : ContentControl
    {
        static readonly Brush White = Frozen(Colors.White), Dim = Frozen(Color.FromArgb(166, 255, 255, 255)), Back = Frozen(Color.FromArgb(140, 0, 0, 0));
        static Brush Frozen(Color c) { SolidColorBrush b = new SolidColorBrush(c); b.Freeze(); return b; }

        public OverlayView() { Focusable = false; IsHitTestVisible = false; SnapsToDevicePixels = true; }

        public void Show(int style, IEnumerable<string> shown, bool cpuTemp, Sample s, IList<double> graph, DateTime local)
        {
            List<string> ids = OverlayModel.Layout(style, shown, cpuTemp);
            if (ids.Count == 0) { Content = null; return; }
            Panel p;
            if (style == 1)
            {
                StackPanel col = new StackPanel();
                foreach (string id in ids)
                {
                    if (id == OverlayModel.Graph) { col.Children.Add(Line(graph, double.NaN, 36, new Thickness(0, 6, 0, 0))); continue; }
                    OverlayItem it = OverlayModel.Item(id, style, s, local);
                    DockPanel row = new DockPanel { Margin = new Thickness(0, col.Children.Count == 0 ? 0 : 2, 0, 0) };
                    TextBlock v = Value(it, 14, Dim);
                    v.HorizontalAlignment = HorizontalAlignment.Right; // the time has no label: it stays with the other values
                    DockPanel.SetDock(v, Dock.Right);
                    row.Children.Add(v);
                    if (it.Label != null) row.Children.Add(new TextBlock { Text = it.Label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
                    col.Children.Add(row);
                }
                Content = new Border { Background = Back, CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Width = 170, Child = col };
                return;
            }
            p = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (string id in ids)
            {
                Thickness gap = new Thickness(p.Children.Count == 0 ? 0 : 12, 0, 0, 0);
                if (id == OverlayModel.Graph) { p.Children.Add(Line(graph, 80, 18, gap)); continue; }
                OverlayItem it = OverlayModel.Item(id, style, s, local);
                TextBlock t = style == 2 ? Value(it, 15, White) : Value(it, 14, Dim);
                if (style == 0 && it.Label != null) t.Inlines.InsertBefore(t.Inlines.FirstInline, new Run(it.Label + " ") { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Dim });
                t.Margin = gap;
                p.Children.Add(t);
            }
            if (style == 2) { p.Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 3, Opacity = 0.9 }; Content = p; return; }
            Content = new Border { Background = Back, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), Child = p };
        }

        static TextBlock Value(OverlayItem it, double size, Brush unit)
        {
            TextBlock t = new TextBlock { Foreground = White, VerticalAlignment = VerticalAlignment.Center };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            t.Inlines.Add(new Run(it.Num) { FontSize = size, FontWeight = FontWeights.Bold });
            if (it.Unit != null) t.Inlines.Add(new Run(" " + it.Unit) { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = unit });
            return t;
        }

        // The last Points frame times, newest on the right; the scale starts at 0 and reaches at least 20 ms so a calm line stays low.
        static FrameworkElement Line(IList<double> v, double w, double h, Thickness m)
        {
            Grid g = new Grid { Height = h, Margin = m, VerticalAlignment = VerticalAlignment.Center };
            if (!double.IsNaN(w)) g.Width = w;
            Polyline l = new Polyline { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.None };
            l.SetResourceReference(Shape.StrokeProperty, "On");
            g.Children.Add(l);
            g.SizeChanged += (s, e) =>
            {
                PointCollection pts = new PointCollection();
                int n = v == null ? 0 : Math.Min(v.Count, OverlayModel.Points);
                if (n >= 2)
                {
                    double top = Math.Max(20, v.Skip(v.Count - n).Max() * 1.1), W = g.ActualWidth - 1, H = g.ActualHeight - 1.5, dx = W / (OverlayModel.Points - 1);
                    for (int i = 0; i < n; i++) pts.Add(new Point(W - (n - 1 - i) * dx + 0.5, 0.75 + H - Math.Min(1, v[v.Count - n + i] / top) * H));
                }
                l.Points = pts;
            };
            return g;
        }
    }

    // The window over the game: main screen, 16 device pixels from the chosen corner. Never activated, never in the taskbar.
    public class PerfOverlay : Window
    {
        const int Gap = 16;
        const int ExStyle = -20, Transparent = 0x20, ToolWindow = 0x80, Layered = 0x80000, NoActivate = 0x8000000;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        public readonly OverlayView View = new OverlayView();
        public int Corner;
        public PerfOverlay()
        {
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual; UseLayoutRounding = true; SnapsToDevicePixels = true; Title = "Tweakeroo overlay";
            SizeToContent = SizeToContent.WidthAndHeight;
            Content = View;
            SourceInitialized += (s, e) =>
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, ExStyle, GetWindowLong(h, ExStyle) | Transparent | Layered | ToolWindow | NoActivate);
                Place();
            };
            SizeChanged += (s, e) => Place();
            // With stretched the desktop changes resolution: back to the corner.
            EventHandler moved = (s, e) => Dispatcher.BeginInvoke(new Action(Place));
            SystemEvents.DisplaySettingsChanged += moved;
            Closed += (s, e) => SystemEvents.DisplaySettingsChanged -= moved;
        }
        public void Place()
        {
            PresentationSource ps = PresentationSource.FromVisual(this);
            if (ps == null || ps.CompositionTarget == null) return;
            Res r;
            try { r = Display.Current(); }
            catch (InvalidOperationException) { return; }
            Point size = ps.CompositionTarget.TransformToDevice.Transform(new Point(ActualWidth, ActualHeight));
            double x = Corner % 2 == 0 ? Gap : r.W - Gap - size.X, y = Corner < 2 ? Gap : r.H - Gap - size.Y;
            Point at = ps.CompositionTarget.TransformFromDevice.Transform(new Point(Math.Round(x), Math.Round(y)));
            Left = at.X; Top = at.Y;
        }
    }
}

namespace Tablero
{
    // Self-test of what the overlay shows (SelfTest.cs): order per style, values that could not be read, CPU temperature missing.
    static class OverlayTest
    {
        static void Is(bool ok, string what) { if (!ok) throw new Exception("FALLA: " + what); }
        public static void Run()
        {
            string[] all = OverlayModel.Ids;
            Func<int, IEnumerable<string>, bool, string> lay = (st, on, ok) => string.Join(",", OverlayModel.Layout(st, on, ok));
            Is(lay(0, all, true) == string.Join(",", all) && lay(2, all, true) == string.Join(",", all), "overlay: una línea y solo números en el orden de la lista");
            Is(lay(1, all, true) == "fps,low,ping,loss,gputemp,gpu,cputemp,cpu,ram,vram,time,graph", "overlay: en columna el gráfico va al final");
            Is(lay(0, new[] { "time", "graph", "fps" }, true) == "fps,graph,time" && lay(1, new[] { "ping", "x", "ping" }, true) == "ping" && lay(0, null, true) == "", "overlay: orden fijo, sin repetidos ni desconocidos");
            Is(lay(0, OverlayModel.Defaults(), true) == "fps,low,ping", "overlay: por defecto FPS, 1 % bajo y ping");
            // Without the CPU temperature its switch is disabled and it is never drawn, even if it was chosen.
            Is(!OverlayModel.Enabled(OverlayModel.CpuTemp, false) && OverlayModel.Enabled(OverlayModel.CpuTemp, true) && OverlayModel.Enabled("cpu", false), "overlay: temperatura de CPU deshabilitada");
            Is(!OverlayModel.Layout(0, all, false).Contains(OverlayModel.CpuTemp) && OverlayModel.Layout(0, all, false).Count == all.Length - 1, "overlay: sin temperatura de CPU no se dibuja");
            // null = "—" with no unit; lost ping too.
            DateTime t = new DateTime(2026, 11, 27, 17, 24, 0);
            foreach (string id in all.Where(x => x != "time" && x != OverlayModel.Graph))
            {
                OverlayItem it = OverlayModel.Item(id, 0, new Sample(), t);
                Is(it.Num == "—" && it.Unit == null, "overlay: dato sin leer " + id);
            }
            Is(OverlayModel.Item("ping", 0, new Sample { Ping = -1 }, t).Num == "—" && OverlayModel.Item("fps", 0, null, t).Num == "—", "overlay: ping perdido y sin muestra");
            bool en = L.En;
            try
            {
                L.En = false;
                Sample s = OverlayModel.Example();
                OverlayItem f = OverlayModel.Item("fps", 0, s, t), f2 = OverlayModel.Item("fps", 2, s, t), r = OverlayModel.Item("ram", 1, s, t), p = OverlayModel.Item("ping", 2, s, t);
                Is(f.Label == "FPS" && f.Num == "144" && f.Unit == null && f2.Unit == "FPS", "overlay: FPS con etiqueta o con unidad");
                Is(r.Label == "RAM" && r.Num == "9,1" && r.Unit == "GB" && p.Num == "23" && p.Unit == "ms", "overlay: RAM y ping " + r.Num);
                Is(OverlayModel.Item("time", 0, s, t).Num == "17:24" && OverlayModel.Item("time", 0, s, t).Label == null && OverlayModel.Item("loss", 0, new Sample { Loss = 3.33 }, t).Num == "3,3", "overlay: hora y pérdida");
                L.En = true;
                Is(OverlayModel.Item("ram", 1, s, t).Num == "9.1", "overlay: decimales en inglés");
            }
            finally { L.En = en; }
            Is(OverlayModel.FrameMs(new Sample { WorstMs = 12, Fps = 200 }) == 12 && OverlayModel.FrameMs(new Sample { Fps = 200 }) == 5 && OverlayModel.FrameMs(new Sample()) == null, "overlay: punto del gráfico");
            Is(Prefs.Parse("{\"OverlayStyle\":7,\"OverlayCorner\":-1,\"OverlayShow\":[\"ram\",\"nada\",\"ram\"]}").OverlayShow.SequenceEqual(new[] { "ram" }) && Prefs.Parse("{\"OverlayStyle\":7}").OverlayStyle == 0 && Prefs.Parse("{}").OverlayShow.SequenceEqual(OverlayModel.Defaults()), "overlay: ajustes guardados");
        }
    }
}
