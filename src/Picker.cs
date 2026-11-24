using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tablero
{
    // Spring stepped every frame, so a drag can hand over its position and speed to the snap.
    class Spring
    {
        public double Pos, Vel, Target, Omega, Zeta, Eps;
        public bool Running;
        public Action Moved, Settled;
        TimeSpan last;
        public Spring(double omega, double zeta, double eps) { Omega = omega; Zeta = zeta; Eps = eps; }
        public void Go()
        {
            if (!Fx.On) { Stop(); Pos = Target; Vel = 0; Moved(); Settled(); return; }
            if (Running) return;
            Running = true;
            last = TimeSpan.Zero;
            CompositionTarget.Rendering += Tick;
        }
        public void Stop()
        {
            if (!Running) return;
            Running = false;
            CompositionTarget.Rendering -= Tick;
        }
        void Tick(object s, EventArgs e)
        {
            TimeSpan now = ((RenderingEventArgs)e).RenderingTime;
            double dt = last == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (now - last).TotalSeconds);
            if (dt <= 0) return; // same frame
            last = now;
            double z = Fx.Level == 0 ? Zeta : 1; // "no bounce": critically damped
            for (int i = 0; i < 4; i++)
            {
                double h = dt / 4;
                Vel += (Omega * Omega * (Target - Pos) - 2 * z * Omega * Vel) * h;
                Pos += Vel * h;
            }
            if (Math.Abs(Target - Pos) < Eps && Math.Abs(Vel) < Eps * 10) { Stop(); Pos = Target; Vel = 0; Moved(); Settled(); return; }
            Moved();
        }
    }

    // Three-position switch: the knob follows the mouse while dragging and snaps to the nearest third.
    public class Tri : Grid
    {
        const double Inset = 3;
        readonly Border knob = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(Inset), HorizontalAlignment = HorizontalAlignment.Left };
        readonly TranslateTransform pos = new TranslateTransform();
        readonly TextBlock[] labels = new TextBlock[3];
        readonly Spring s = new Spring(22, 0.72, 0.3);
        int value, lit = -1;
        bool dragging;
        double grab;
        public int Value { get { return value; } }
        public event Action<int> Changed;

        public Tri()
        {
            Height = 36;
            Focusable = true;
            Cursor = Cursors.Hand;
            SetResourceReference(FocusVisualStyleProperty, "Focus");
            Border track = new Border { CornerRadius = new CornerRadius(10) };
            track.SetResourceReference(Border.BackgroundProperty, "Off");
            knob.SetResourceReference(Border.BackgroundProperty, "On");
            knob.RenderTransform = pos;
            UniformGrid row = new UniformGrid { Rows = 1, Columns = 3, Margin = new Thickness(Inset), IsHitTestVisible = false };
            for (int i = 0; i < 3; i++)
            {
                labels[i] = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
                row.Children.Add(labels[i]);
            }
            Children.Add(track);
            Children.Add(knob);
            Children.Add(row);
            s.Moved = Show;
            s.Settled = () =>
            {
                int v = Nearest(s.Pos);
                if (v == value) return;
                value = v;
                if (Changed != null) Changed(v);
            };
            SizeChanged += (o, e) => { knob.Width = Third(); if (!s.Running && !dragging) s.Pos = s.Target = value * Third(); Show(); };
        }
        public void Labels(string[] names) { for (int i = 0; i < 3; i++) labels[i].Text = L.T(names[i]); }

        double Third() { return Math.Max(0, (ActualWidth - 2 * Inset) / 3); }
        int Nearest(double x) { double t = Third(); return t <= 0 ? value : Math.Max(0, Math.Min(2, (int)Math.Round(x / t))); }
        void Show()
        {
            pos.X = s.Pos;
            double t = Third();
            // The label turns on when the knob's center enters its third.
            int on = t <= 0 ? value : Math.Max(0, Math.Min(2, (int)Math.Floor((s.Pos + t / 2) / t)));
            if (on == lit) return;
            lit = on;
            for (int i = 0; i < 3; i++) labels[i].SetResourceReference(TextBlock.ForegroundProperty, i == on ? "OnInk" : "Ink2");
        }
        // From code (load, reset): no animation and no event.
        public void Set(int v)
        {
            value = Math.Max(0, Math.Min(2, v));
            if (dragging) return;
            s.Stop();
            s.Vel = 0;
            s.Pos = s.Target = value * Third();
            Show();
        }
        public void Snap(int v)
        {
            v = Math.Max(0, Math.Min(2, v));
            s.Target = v * Third();
            s.Go();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            e.Handled = true;
            double x = e.GetPosition(this).X - Inset, t = Third();
            if (t <= 0) return;
            if (x < s.Pos || x > s.Pos + t) { Snap((int)Math.Floor(x / t)); return; }
            // Grabbed: the knob keeps the same point under the mouse.
            dragging = true;
            grab = x - s.Pos;
            s.Stop();
            s.Vel = 0;
            CaptureMouse();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            s.Pos = Math.Max(0, Math.Min(2 * Third(), e.GetPosition(this).X - Inset - grab));
            Show();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (dragging) ReleaseMouseCapture(); // OnLostMouseCapture snaps
        }
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (!dragging) return;
            dragging = false;
            Snap(Nearest(s.Pos));
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key != Key.Left && e.Key != Key.Right) return;
            Snap(Nearest(s.Target) + (e.Key == Key.Left ? -1 : 1));
            e.Handled = true;
        }
    }

    // iOS-style timer wheel, one column: drag, mouse wheel, arrows or click on a number.
    public class Wheel : Grid
    {
        const double Row = 40;
        public int Min = 5, Max = 30;
        public string Unit = "s";
        readonly Canvas strip = new Canvas();
        readonly List<Border> items = new List<Border>();
        readonly List<bool> center = new List<bool>();
        readonly Spring s = new Spring(18, 0.78, 0.002);
        readonly TextBlock unit = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        readonly TranslateTransform unitAt = new TranslateTransform(0, 2);
        int value = 15, ticked;
        DateTime tickAt;
        bool dragging, moved;
        double downY, downPos, lastY;
        DateTime lastT;
        double speed; // rows per second while dragging
        public int Value { get { return value; } }
        public event Action<int> Changed;

        public Wheel()
        {
            Height = Row * 5;
            ClipToBounds = true;
            Focusable = true;
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;
            SetResourceReference(FocusVisualStyleProperty, "Focus");
            Border band = new Border { Height = Row, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center };
            band.SetResourceReference(Border.BackgroundProperty, "CardHover");
            unit.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            unit.RenderTransform = unitAt;
            Children.Add(band);
            Children.Add(strip);
            Children.Add(unit);
            s.Moved = Show;
            s.Settled = () =>
            {
                int v = Min + (int)Math.Round(s.Pos);
                if (v == value) return;
                value = v;
                if (Changed != null) Changed(v);
            };
            Loaded += (o, e) => Build();
            SizeChanged += (o, e) => Show();
        }
        void Build()
        {
            if (items.Count > 0) return;
            unit.Text = Unit;
            for (int v = Min; v <= Max; v++)
            {
                TextBlock t = new TextBlock { Text = v.ToString(), FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
                System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
                Border b = new Border { Width = 60, Height = Row, Child = t, RenderTransformOrigin = new Point(1, 0.5), IsHitTestVisible = false };
                TransformGroup g = new TransformGroup();
                g.Children.Add(new ScaleTransform(1, 1));
                g.Children.Add(new TranslateTransform());
                b.RenderTransform = g;
                t.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
                items.Add(b);
                center.Add(false);
                strip.Children.Add(b);
            }
            Show();
        }
        void Show()
        {
            // Numbers end just right of the middle and the unit follows, so the pair looks centered.
            double x = ActualWidth / 2 + 8 - 60;
            for (int i = 0; i < items.Count; i++)
            {
                Border b = items[i];
                double d = i - s.Pos, a = Math.Abs(d);
                if (a > 2.6) { b.Visibility = Visibility.Hidden; continue; }
                b.Visibility = Visibility.Visible;
                b.Opacity = a <= 2 ? 1 - 0.32 * a : Math.Max(0, 0.36 - (a - 2) * 0.72);
                TransformGroup g = (TransformGroup)b.RenderTransform;
                ScaleTransform sc = (ScaleTransform)g.Children[0];
                sc.ScaleX = sc.ScaleY = 1 - 0.07 * Math.Min(a, 2.6);
                TranslateTransform tr = (TranslateTransform)g.Children[1];
                tr.X = x;
                tr.Y = 2 * Row + d * Row;
                bool c = a < 0.5;
                if (c != center[i]) { center[i] = c; ((TextBlock)b.Child).SetResourceReference(TextBlock.ForegroundProperty, c ? "Ink" : "Ink2"); }
            }
            unitAt.X = ActualWidth / 2 + 13;
            // One tick per number crossing the middle, and never two closer than 30 ms.
            int now = (int)Math.Round(Math.Max(0, Math.Min(Max - Min, s.Pos)));
            if (now != ticked)
            {
                ticked = now;
                if ((DateTime.Now - tickAt).TotalMilliseconds >= 30) { tickAt = DateTime.Now; Sound.Effect("hover"); }
            }
        }
        // From code (load, reset): no animation, no sound, no event.
        public void Set(int v)
        {
            value = Math.Max(Min, Math.Min(Max, v));
            if (dragging) return;
            s.Stop();
            s.Vel = 0;
            s.Pos = s.Target = ticked = value - Min;
            Show();
        }
        // Animated to a value; saved when it settles.
        public void Spin(int v)
        {
            s.Target = Math.Max(0, Math.Min(Max - Min, v - Min));
            s.Go();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            e.Handled = true;
            Spin(Min + (int)Math.Round(s.Target) + (e.Delta > 0 ? -1 : 1));
        }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key != Key.Up && e.Key != Key.Down) return;
            e.Handled = true;
            Spin(Min + (int)Math.Round(s.Target) + (e.Key == Key.Up ? -1 : 1));
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            e.Handled = true;
            dragging = true;
            moved = false;
            downY = lastY = e.GetPosition(this).Y;
            lastT = DateTime.Now;
            speed = 0;
            s.Stop();
            downPos = s.Pos;
            CaptureMouse();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            double y = e.GetPosition(this).Y;
            if (Math.Abs(y - downY) > 4) moved = true;
            if (!moved) return;
            double p = downPos - (y - downY) / Row, top = Max - Min;
            // Past either end it resists, like a rubber band.
            if (p < 0) p *= 0.35; else if (p > top) p = top + (p - top) * 0.35;
            double dt = (DateTime.Now - lastT).TotalSeconds;
            if (dt > 0.004) { speed = 0.6 * (-(y - lastY) / Row / dt) + 0.4 * speed; lastY = y; lastT = DateTime.Now; }
            s.Pos = p;
            Show();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!dragging) return;
            if (!moved)
            {
                // A click picks the number under the mouse.
                dragging = false;
                ReleaseMouseCapture();
                Spin(Min + (int)Math.Round(s.Pos + (e.GetPosition(this).Y - ActualHeight / 2) / Row));
                return;
            }
            ReleaseMouseCapture(); // OnLostMouseCapture snaps
        }
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (!dragging) return;
            dragging = false;
            // A little inertia: a fast release goes a few rows further.
            if ((DateTime.Now - lastT).TotalMilliseconds > 90) speed = 0;
            s.Vel = speed;
            s.Target = Math.Max(0, Math.Min(Max - Min, Math.Round(s.Pos + speed * 0.12)));
            s.Go();
        }
    }

    // Option with an icon (music).
    public class IconOption : LimitOption
    {
        public Geometry Icon { get; set; }
        public bool Muted { get; set; }
    }

    // Dropdown: a field that opens its Panel in a popup below it, as wide as the field (240 minimum).
    public class Drop : Button
    {
        public static readonly DependencyProperty PanelProperty = DependencyProperty.Register("Panel", typeof(object), typeof(Drop));
        public object Panel { get { return GetValue(PanelProperty); } set { SetValue(PanelProperty, value); } }
        public static Drop Shown; // the open one, if any
        Popup pop;
        FrameworkElement card;
        RotateTransform chev;
        Window win;
        public bool IsOpen { get { return pop != null && pop.IsOpen; } }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            pop = (Popup)GetTemplateChild("pop");
            card = (FrameworkElement)GetTemplateChild("card");
            chev = new RotateTransform();
            ((FrameworkElement)GetTemplateChild("chev")).RenderTransform = chev;
        }
        protected override void OnClick()
        {
            base.OnClick();
            if (IsOpen) Close(); else Open();
        }
        public void Open()
        {
            ApplyTemplate();
            if (pop == null || IsOpen) return;
            if (Shown != null) Shown.Close();
            Shown = this;
            win = Window.GetWindow(this);
            if (win != null)
            {
                win.PreviewMouseDown += Outside;
                win.PreviewMouseWheel += Outside;
                win.Deactivated += Away;
                win.LocationChanged += Away;
                win.SizeChanged += Away;
            }
            pop.IsOpen = true;
#if SHOT
            // The test window is off screen, but Windows moves popups onto the nearest monitor: keep them invisible (alpha 0 also lets clicks through).
            PresentationSource ps = PresentationSource.FromVisual(card);
            if (ps != null) ((UIElement)ps.RootVisual).Opacity = 0;
#endif
            ScaleTransform sc = new ScaleTransform(1, 1);
            card.RenderTransformOrigin = new Point(0.5, 0);
            card.RenderTransform = sc;
            Fx.From(sc, ScaleTransform.ScaleYProperty, 0.9, 1, 180, Fx.Out, 0);
            Fx.From(card, OpacityProperty, 0, 1, 180, Fx.Out, 0);
            Fx.To(chev, RotateTransform.AngleProperty, 180, 200, Fx.Out);
            // Focus goes inside, so arrows and Esc work at once; in a list, on the chosen row.
            UIElement p = Panel as UIElement;
            if (p != null) Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsOpen) return;
                ListBox l = p as ListBox;
                UIElement row = l != null && l.SelectedItem != null ? l.ItemContainerGenerator.ContainerFromItem(l.SelectedItem) as UIElement : null;
                if (row != null) row.Focus(); else p.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
        public void Close()
        {
            if (!IsOpen) return;
            if (win != null)
            {
                win.PreviewMouseDown -= Outside;
                win.PreviewMouseWheel -= Outside;
                win.Deactivated -= Away;
                win.LocationChanged -= Away;
                win.SizeChanged -= Away;
            }
            bool inside = card.IsKeyboardFocusWithin;
            pop.IsOpen = false;
            if (Shown == this) Shown = null;
            Fx.To(chev, RotateTransform.AngleProperty, 0, 200, Fx.Out);
            if (inside) Focus();
        }
        // Clicks inside the popup never reach the window; a click on the field itself toggles in OnClick.
        void Outside(object s, InputEventArgs e) { if (!IsMouseOver) Close(); }
        void Away(object s, EventArgs e) { Close(); }
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Key == Key.Escape && IsOpen) { Close(); e.Handled = true; }
        }
#if SHOT
        // Screenshots only see the window: the popup is drawn on top at its place.
        public BitmapSource Over(FrameworkElement root, BitmapSource under)
        {
            PresentationSource ps = PresentationSource.FromVisual(card);
            if (ps == null) return under;
            Visual top = ps.RootVisual;
            FrameworkElement fe = (FrameworkElement)top;
            Matrix m = ps.CompositionTarget.TransformFromDevice;
            Vector at = m.Transform((Vector)(top.PointToScreen(new Point()) - root.PointToScreen(new Point())));
            RenderTargetBitmap b = new RenderTargetBitmap((int)Math.Ceiling(fe.ActualWidth), (int)Math.Ceiling(fe.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            // Visible only for this capture; nothing reaches the screen within the same call.
            fe.Opacity = 1;
            b.Render(top);
            fe.Opacity = 0;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawImage(under, new Rect(0, 0, under.PixelWidth, under.PixelHeight));
                dc.DrawImage(b, new Rect(at.X, at.Y, b.PixelWidth, b.PixelHeight));
            }
            RenderTargetBitmap r = new RenderTargetBitmap(under.PixelWidth, under.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            r.Render(dv);
            return r;
        }
#endif
    }
}
