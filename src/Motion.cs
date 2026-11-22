using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Tablero
{
    // Resorte amortiguado: pasa un poco de largo y vuelve.
    public class SpringEase : EasingFunctionBase
    {
        public double Damping = 0.5, Freq = 11;
        public SpringEase() { EasingMode = EasingMode.EaseIn; }
        protected override double EaseInCore(double t)
        {
            if (t >= 1) return 1;
            double wd = Freq * Math.Sqrt(1 - Damping * Damping);
            return 1 - Math.Exp(-Damping * Freq * t) * (Math.Cos(wd * t) + Damping * Freq / wd * Math.Sin(wd * t));
        }
        protected override Freezable CreateInstanceCore() { return new SpringEase { Damping = Damping, Freq = Freq }; }
        protected override void CloneCore(Freezable s) { Copy(s); base.CloneCore(s); }
        protected override void CloneCurrentValueCore(Freezable s) { Copy(s); base.CloneCurrentValueCore(s); }
        protected override void GetAsFrozenCore(Freezable s) { Copy(s); base.GetAsFrozenCore(s); }
        protected override void GetCurrentValueAsFrozenCore(Freezable s) { Copy(s); base.GetCurrentValueAsFrozenCore(s); }
        void Copy(Freezable s) { SpringEase e = (SpringEase)s; Damping = e.Damping; Freq = e.Freq; }
    }

    public static class Fx
    {
        // Si Windows tiene las animaciones apagadas, todo salta al estado final.
        // Level: 0 todas, 1 sin rebotes, 2 apagadas (Ajustes).
        public static int Level;
        public static bool On { get { return Level < 2 && SystemParameters.ClientAreaAnimation; } }
        public static readonly IEasingFunction Out = new QuinticEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction In = new CubicEase { EasingMode = EasingMode.EaseIn };
        public static IEasingFunction Spring { get { return Bounce(0.5); } }
        public static IEasingFunction Bounce(double damping) { return Level == 0 ? new SpringEase { Damping = damping } : Out; }

        public static void To(IAnimatable t, DependencyProperty p, double to, int ms, IEasingFunction ease, int delay = 0, Action done = null)
        {
            if (!On) ms = 1;
            DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(On ? delay : 0) };
            if (done != null) a.Completed += (s, e) => done();
            t.BeginAnimation(p, a);
        }
        public static void From(IAnimatable t, DependencyProperty p, double from, double to, int ms, IEasingFunction ease, int delay)
        {
            if (!On) return;
            DoubleAnimationUsingKeyFrames a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay + ms))) { EasingFunction = ease });
            t.BeginAnimation(p, a);
        }
        // Entrada de una lista: sube y aparece, 30 ms entre elementos.
        public static void Enter(FrameworkElement e, int index, double rise)
        {
            TranslateTransform t = new TranslateTransform();
            e.RenderTransform = t;
            From(t, TranslateTransform.YProperty, rise, 0, 320, Out, index * 30);
            From(e, UIElement.OpacityProperty, 0, 1, 200, Out, index * 30);
        }
        public static ScaleTransform Scale(FrameworkElement e)
        {
            ScaleTransform s = e.RenderTransform as ScaleTransform;
            if (s != null && !s.IsFrozen) return s;
            s = new ScaleTransform(1, 1);
            e.RenderTransformOrigin = new Point(0.5, 0.5);
            e.RenderTransform = s;
            return s;
        }
        public static void ScaleTo(FrameworkElement e, double v, int ms, IEasingFunction ease)
        {
            ScaleTransform s = Scale(e);
            To(s, ScaleTransform.ScaleXProperty, v, ms, ease);
            To(s, ScaleTransform.ScaleYProperty, v, ms, ease);
        }
        public static void Pop(FrameworkElement e, double from, int delay = 0)
        {
            ScaleTransform s = Scale(e);
            From(s, ScaleTransform.ScaleXProperty, from, 1, 620, Spring, delay);
            From(s, ScaleTransform.ScaleYProperty, from, 1, 620, Spring, delay);
        }
        // Los botones entran con rebote; el resto sube y aparece.
        public static void Arrive(FrameworkElement e, int index)
        {
            if (!(e is Btn)) { Enter(e, index, 20); return; }
            Pop(e, 0.72, index * 30);
            From(e, UIElement.OpacityProperty, 0, 1, 160, Out, index * 30);
        }

        // El numero se desliza hasta el nuevo valor en vez de cambiar de golpe.
        static readonly DependencyProperty CountProperty = DependencyProperty.RegisterAttached("Count", typeof(double), typeof(Fx),
            new PropertyMetadata(0.0, (d, e) => ((TextBlock)d).Text = ((int)Math.Round((double)e.NewValue)).ToString()));
        public static void CountTo(TextBlock t, int v)
        {
            if (t.Text.Length == 0) t.Text = "0";
            To(t, CountProperty, v, 500, Out);
        }
    }

    // Boton que se hunde al pulsar y vuelve con rebote.
    public class Btn : Button
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register("Icon", typeof(Geometry), typeof(Btn));
        public Geometry Icon { get { return (Geometry)GetValue(IconProperty); } set { SetValue(IconProperty, value); } }
        static Btn() { ContentProperty.OverrideMetadata(typeof(Btn), new FrameworkPropertyMetadata(null, null, (d, v) => v is string ? L.T((string)v) : v)); }
        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            if (IsPressed) Fx.ScaleTo(this, 0.97, 90, Fx.Out);
            else Fx.ScaleTo(this, IsMouseOver ? 1.03 : 1, 520, Fx.Spring);
        }
        protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e) { base.OnMouseEnter(e); Fx.ScaleTo(this, 1.03, 420, Fx.Spring); }
        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e) { base.OnMouseLeave(e); Fx.ScaleTo(this, 1, 420, Fx.Spring); }

        // Brillo que cruza el boton principal y halo que late, para que se vea que hay que tocarlo.
        public static readonly DependencyProperty GlintProperty = DependencyProperty.Register("Glint", typeof(bool), typeof(Btn), new PropertyMetadata(false, (d, e) => ((Btn)d).Shine()));
        public bool Glint { get { return (bool)GetValue(GlintProperty); } set { SetValue(GlintProperty, value); } }
        Border shine, halo;
        public Btn() { IsVisibleChanged += (s, e) => Shine(); IsEnabledChanged += (s, e) => Shine(); }
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            shine = GetTemplateChild("shine") as Border;
            halo = GetTemplateChild("halo") as Border;
            Shine();
        }
        static void Loop(IAnimatable t, DependencyProperty p, double from, double to, IEasingFunction ease)
        {
            DoubleAnimationUsingKeyFrames a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromMilliseconds(3200) };
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))) { EasingFunction = ease });
            t.BeginAnimation(p, a);
        }
        void Shine()
        {
            if (shine == null || halo == null) return;
            bool on = Glint && IsVisible && IsEnabled && Fx.On && Fx.Level == 0;
            shine.Visibility = halo.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) { shine.Background = null; halo.RenderTransform = null; halo.BeginAnimation(OpacityProperty, null); return; }
            LinearGradientBrush g = new LinearGradientBrush { StartPoint = new Point(0, 0.3), EndPoint = new Point(1, 0.7) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.36));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), 0.5));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.64));
            TranslateTransform x = new TranslateTransform(-1, 0);
            g.RelativeTransform = x;
            shine.Background = g;
            Loop(x, TranslateTransform.XProperty, -1, 1, new CubicEase { EasingMode = EasingMode.EaseInOut });
            ScaleTransform s = new ScaleTransform(1, 1);
            halo.RenderTransform = s;
            Loop(s, ScaleTransform.ScaleXProperty, 1, 1.07, Fx.Out);
            Loop(s, ScaleTransform.ScaleYProperty, 1, 1.3, Fx.Out);
            Loop(halo, OpacityProperty, 0.55, 0, Fx.Out);
        }
    }

    // Interruptor: la perilla viaja con resorte al cambiar, y salta sin animar cuando la fila se recicla al desplazarse.
    public class Switch : Button
    {
        const double Travel = 26;
        public static readonly DependencyProperty StateProperty = DependencyProperty.Register("State", typeof(bool?), typeof(Switch), new PropertyMetadata(false, (d, e) => ((Switch)d).Move()));
        public bool? State { get { return (bool?)GetValue(StateProperty); } set { SetValue(StateProperty, value); } }
        FrameworkElement knob, track;
        TranslateTransform pos;
        object owner;
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            knob = (FrameworkElement)GetTemplateChild("knob");
            track = (FrameworkElement)GetTemplateChild("live");
            pos = new TranslateTransform();
            TransformGroup g = new TransformGroup();
            g.Children.Add(new ScaleTransform(1, 1));
            g.Children.Add(pos);
            knob.RenderTransformOrigin = new Point(0.5, 0.5);
            knob.RenderTransform = g;
            owner = null;
            Move();
        }
        void Move()
        {
            if (pos == null) return;
            double x = State == true ? Travel : State == null ? Travel / 2 : 0;
            double live = State == true ? 1 : State == null ? 0.55 : 0;
            bool animate = owner != null && owner == DataContext && Fx.On;
            owner = DataContext;
            if (animate)
            {
                Fx.To(pos, TranslateTransform.XProperty, x, 560, Fx.Spring);
                Fx.To(track, OpacityProperty, live, 220, Fx.Out);
                return;
            }
            pos.BeginAnimation(TranslateTransform.XProperty, null);
            track.BeginAnimation(OpacityProperty, null);
            pos.X = x;
            track.Opacity = live;
        }
        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            if (knob == null) return;
            // Al pulsar, la perilla se estira como en un interruptor de telefono.
            ScaleTransform s = (ScaleTransform)((TransformGroup)knob.RenderTransform).Children[0];
            Fx.To(s, ScaleTransform.ScaleXProperty, IsPressed ? 1.18 : 1, IsPressed ? 110 : 480, IsPressed ? Fx.Out : Fx.Spring);
        }
    }
}
