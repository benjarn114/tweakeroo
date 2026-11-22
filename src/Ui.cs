using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tablero
{
    public abstract class Vm : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise(string p) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(p)); }
    }

    public class Row : Vm
    {
        public readonly Tweak T;
        public State State;
        public Advice Advice;
        string error = "";
        public Row(Tweak t) { T = t; }
        public string Name { get { return T.Name; } }
        public string Desc { get { return T.Desc; } }
        public string StateText { get { return State == State.On ? "Activado" : State == State.Off ? "Sin activar" : "A medias"; } }
        public string Spoken { get { return L.T(T.Name) + ", " + L.T(StateText).ToLowerInvariant(); } }
        public bool? Checked { get { return State == State.On ? true : State == State.Off ? (bool?)false : null; } }
        public string EffectText { get { return "Efecto " + T.Effect.ToLowerInvariant(); } }
        public double Bar2 { get { return T.Effect == "Bajo" ? 0.22 : 1; } }
        public double Bar3 { get { return T.Effect == "Alto" ? 1 : 0.22; } }
        public bool IsRisk { get { return T.Level == Level.Riesgo; } }
        public string Note
        {
            get
            {
                List<string> n = new List<string>();
                if (T.Level == Level.Avanzado) n.Add("Avanzado");
                if (T.Restart.Length > 0) n.Add("Reinicia " + T.Restart);
                return string.Join("\n", n);
            }
        }
        public bool HasNote { get { return Note.Length > 0; } }
        public string AdviceText { get { return Advice == null ? "" : (Advice.Yes ? "Recomendado. " : "No recomendado. ") + Advice.Why; } }
        public bool HasAdvice { get { return Advice != null; } }
        public string Error { get { return error; } }
        public bool HasError { get { return error.Length > 0; } }
        public void SetAdvice(Advice a) { Advice = a; Raise("AdviceText"); Raise("HasAdvice"); }
        public void SetError(string e) { error = e; Raise("Error"); Raise("HasError"); }
        public void SetState(State s) { State = s; Raise("Checked"); Raise("Spoken"); Raise("StateText"); }
    }

    public class Circuit : Vm
    {
        public string Name { get; set; }
        public Geometry Icon { get; set; }
        public List<Row> Rows;
        public string Page = "ListPage";
        public bool Sub { get; set; }
        public bool Open;
        public bool HasSubs { get { return Page == "UpgradePage"; } }
        public double Size { get { return Sub ? 13 : 16; } }
        public double IconSize { get { return Sub ? 15 : 21; } }
        public string Count { get { return Rows == null ? "" : Rows.Count(r => r.State == State.On) + "/" + Rows.Count; } }
        public void Refresh() { Raise("Count"); }
    }

    public class Part : Vm
    {
        public string Key, Title;
        public Geometry Icon { get; set; }
        public string Main { get; set; }
        public string Sub { get; set; }
        public void Set(string main, string sub) { Main = main; Sub = sub; Raise("Main"); Raise("Sub"); }
    }

    public class MacroRow : Vm
    {
        public string Id;
        public string Name { get; set; }
        public string Desc { get; set; }
        public string KeyText { get; set; }
        public bool On { get; set; }
        public void Set(string key, bool on) { KeyText = key; On = on; Raise("KeyText"); Raise("On"); }
    }

    public class LimitOption
    {
        public string Name { get; set; }
        public string Note { get; set; }
        public string Value;
    }

    public class EstimateTile
    {
        public string Label { get; set; }
        public string Range { get; set; }
        public string Size { get; set; }
    }

    public class FpsRow
    {
        public Match M;
        public double BarW { get; set; }
        public static string Day(Match m) { return m.Start.Substring(8, 2) + "/" + m.Start.Substring(5, 2) + " " + m.Start.Substring(11, 5); }
        public string When { get { return Day(M); } }
        public string Map { get { return M.Map; } }
        public string Median { get { return M.Median.ToString(); } }
        public string Low { get { return M.Low.ToString(); } }
        public string Max { get { return M.Max.ToString(); } }
        public string Cap { get { return M.Cap > 0 ? M.Cap.ToString() : "Sin límite"; } }
    }

    public class SaveRow : Vm
    {
        public Snapshot S;
        public int Diff;
        bool armedRestore, armedDelete;
        public string Name { get { return S.Name; } }
        public string Info { get { return S.Date.Substring(0, 16) + "\n" + (Diff == 0 ? "Igual que ahora" : Diff == 1 ? "1 ajuste distinto" : Diff + " ajustes distintos"); } }
        public bool CanRestore { get { return Diff > 0; } }
        public bool CanDelete { get { return S.Kind != "original"; } }
        public string RestoreText { get { return armedRestore ? "Sí, restaurar" : "Restaurar"; } }
        public string DeleteText { get { return armedDelete ? "Sí, borrar" : "Borrar"; } }
        // Primer clic pide confirmacion en el mismo boton; el segundo actua.
        public bool Arm(bool restore)
        {
            bool was = restore ? armedRestore : armedDelete;
            armedRestore = restore && !was;
            armedDelete = !restore && !was;
            Raise("RestoreText"); Raise("DeleteText");
            return was;
        }
    }

    partial class Ui
    {
        const string Recommended = "Recomendados";
        readonly Window w;
        readonly string dir;
        readonly DispatcherTimer hide = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        Engine engine;
        List<Row> rows;
        ObservableCollection<Circuit> circuits;
        List<Circuit> subs;   // salen de Mejoras al desplegarla
        Circuit upgrades;
        LinearGradientBrush shimmer;
        Task fpsTask;
        Hw quick;             // solo CPU y grafica, para estimar sin escaneo
        bool fpsRead;
        readonly DispatcherTimer slow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        DateTime skelAt;
        List<Part> parts;
        Hw hw;
        bool working, ready;
        static readonly string[] Pages = { "PcPage", "StretchPage", "FpsPage", "MacroPage", "SettingsPage", "UpgradePage", "ListPage" };
        StretchSession stretch;
        DispatcherTimer watch;
        bool confirming, gameSeen;
        DateTime limit;
        List<Match> matches = new List<Match>();
        T F<T>(string n) where T : class { return w.FindName(n) as T; }
        Geometry Ico(string n) { return (Geometry)w.Resources["Ico" + n]; }

        public Ui(Window w, string dir)
        {
            this.w = w;
            this.dir = dir;
            hide.Tick += (s, e) => { hide.Stop(); HideToast(); };
            Skeleton();
            // El boton derecho no selecciona nada; solo los campos de texto tienen menu.
            w.PreviewMouseRightButtonDown += (s, e) => { if (capture == null && !(Keyboard.FocusedElement is TextBox && ((TextBox)Keyboard.FocusedElement).IsMouseOver)) e.Handled = true; };
        }

        // ---- Carga ----

        // Un solo brillo gris recorre todas las piezas; gris para que se note en paletas claras y oscuras.
        void Skeleton()
        {
            Color g = Color.FromArgb(0, 128, 128, 128);
            shimmer = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            shimmer.GradientStops.Add(new GradientStop(g, 0.2));
            shimmer.GradientStops.Add(new GradientStop(Color.FromArgb(80, 160, 160, 160), 0.5));
            shimmer.GradientStops.Add(new GradientStop(g, 0.8));
            shimmer.RelativeTransform = new TranslateTransform(-1, 0);
            w.Resources["Shimmer"] = shimmer;
            // Solo aparecen si la carga tarda de verdad.
            slow.Tick += (s, e) => { slow.Stop(); if (!ready) ShowSkeletons("SkelPage", "SkelNav", "SkelSaves"); };
            slow.Start();
        }
        void Shimmer(bool on)
        {
            DoubleAnimation a = !on || !Fx.On ? null : new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever };
            shimmer.RelativeTransform.BeginAnimation(TranslateTransform.XProperty, a);
        }
        internal void ShowSkeletons(params string[] names)
        {
            foreach (string n in names)
            {
                FrameworkElement p = F<FrameworkElement>(n);
                p.BeginAnimation(UIElement.OpacityProperty, null);
                p.Visibility = Visibility.Visible;
            }
            skelAt = DateTime.Now;
            Shimmer(true);
        }
        // Si llegaron a verse, se quedan un momento para que no parpadeen.
        async Task HideSkeletons()
        {
            slow.Stop();
            int left = 400 - (int)(DateTime.Now - skelAt).TotalMilliseconds;
            if (left > 0) await Task.Delay(left);
            foreach (string n in new[] { "SkelPage", "SkelNav", "SkelSaves" })
            {
                FrameworkElement p = F<FrameworkElement>(n);
                if (p.Visibility != Visibility.Visible) continue;
                p.IsHitTestVisible = false;
                Fx.To(p, UIElement.OpacityProperty, 0, 200, Fx.Out, 0, () => p.Visibility = Visibility.Collapsed);
            }
            Shimmer(false);
        }
        // Las partes de la pagina entran en orden; botones y tarjetas, uno a uno.
        void EnterPage(string page)
        {
            ScrollViewer sv = F<ScrollViewer>(page);
            sv.UpdateLayout();
            List<FrameworkElement> all = new List<FrameworkElement>();
            foreach (FrameworkElement e in ((Panel)sv.Content).Children) Collect(e, all);
            for (int i = 0; i < Math.Min(all.Count, 24); i++) Fx.Arrive(all[i], i);
        }
        static void Collect(FrameworkElement e, List<FrameworkElement> all)
        {
            if (e.Visibility != Visibility.Visible) return;
            ItemsControl list = e as ItemsControl;
            if (e is WrapPanel || e is UniformGrid) foreach (FrameworkElement c in ((Panel)e).Children) Collect(c, all);
            else if (list != null) for (int i = 0; i < list.Items.Count; i++) { FrameworkElement c = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement; if (c != null) all.Add(c); }
            else all.Add(e);
        }

        static List<Tweak> LoadCatalog() { List<Tweak> c = Catalog.Build(); foreach (Tweak t in c) { bool ignore = t.Available; } return c; }
        internal static Func<List<Tweak>> Source = LoadCatalog;

        string PaletteFile { get { return Path.Combine(dir, "paleta.txt"); } }

        public void LoadPalette()
        {
            string saved = "";
            try { if (File.Exists(PaletteFile)) saved = File.ReadAllText(PaletteFile).Trim(); }
            catch (IOException) { }
            Palette p = Theme.All.FirstOrDefault(x => x.Name == saved) ?? Theme.All[0];
            Theme.Apply(w, p);
            ListBox l = F<ListBox>("Palettes");
            l.ItemsSource = Theme.All;
            l.SelectedItem = p;
            F<TextBlock>("PaletteName").Text = "Color: " + p.Name;
            l.SelectionChanged += (s, e) => ChangePalette((Palette)l.SelectedItem);
            PaletteMenu(l);
            w.SourceInitialized += (s, e) => Theme.Apply(w, (Palette)l.SelectedItem);
        }

        // Plegado solo se ve el color activo; al pasar el raton salen los demas con rebote.
        void PaletteMenu(ListBox l)
        {
            Border zone = F<Border>("PaletteZone");
            bool open = false;
            Action<bool> place = animate =>
            {
                l.UpdateLayout();
                for (int i = 0; i < l.Items.Count; i++)
                {
                    ListBoxItem c = l.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                    if (c == null) continue;
                    bool on = i == l.SelectedIndex;
                    TranslateTransform t = c.RenderTransform as TranslateTransform;
                    if (t == null || t.IsFrozen) { t = new TranslateTransform(); c.RenderTransform = t; }
                    Panel.SetZIndex(c, on ? 1 : 0);
                    c.IsHitTestVisible = open || on;
                    double x = open ? 0 : -VisualTreeHelper.GetOffset(c).X;
                    Fx.To(t, TranslateTransform.XProperty, x, animate ? (open ? 560 : 260) : 1, open ? Fx.Bounce(0.58) : Fx.Out, open ? i * 18 : 0);
                    Fx.To(c, UIElement.OpacityProperty, open || on ? 1 : 0, animate ? 140 : 1, Fx.Out, open ? i * 18 : 0);
                }
            };
            Action update = () =>
            {
                bool want = zone.IsMouseOver || zone.IsKeyboardFocusWithin;
                if (want == open) return;
                open = want;
                place(true);
            };
            zone.MouseEnter += (s, e) => update();
            zone.MouseLeave += (s, e) => { if (zone.IsKeyboardFocusWithin) Keyboard.ClearFocus(); update(); };
            zone.IsKeyboardFocusWithinChanged += (s, e) => update();
            // Con el boton pulsado, el color cambia al pasar por encima.
            l.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                DependencyObject o = l.InputHitTest(e.GetPosition(l)) as DependencyObject;
                while (o != null && !(o is ListBoxItem)) o = VisualTreeHelper.GetParent(o);
                if (o != null) ((ListBoxItem)o).IsSelected = true;
            };
            l.SelectionChanged += (s, e) => { if (!open) place(false); };
            w.Loaded += (s, e) => place(false);
        }

        // La paleta nueva se abre en circulo desde la muestra pulsada.
        internal void ChangePalette(Palette p)
        {
            if (p == null) return;
            Grid shell = F<Grid>("Shell");
            Image wipe = F<Image>("Wipe");
            double W = shell.ActualWidth, H = shell.ActualHeight;
            if (Fx.On && W > 0)
            {
                wipe.Visibility = Visibility.Collapsed;
                RenderTargetBitmap old = new RenderTargetBitmap((int)W, (int)H, 96, 96, PixelFormats.Pbgra32);
                old.Render(shell);
                wipe.Source = old;
                ListBox l = F<ListBox>("Palettes");
                FrameworkElement sw = l.ItemContainerGenerator.ContainerFromItem(p) as FrameworkElement;
                Point c = sw == null ? new Point(60, H - 40) : sw.TranslatePoint(new Point(sw.ActualWidth / 2, sw.ActualHeight / 2), shell);
                EllipseGeometry hole = new EllipseGeometry(c, 0, 0);
                wipe.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, W, H)), hole);
                wipe.Visibility = Visibility.Visible;
                double r = Math.Sqrt(W * W + H * H);
                IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                Fx.To(hole, EllipseGeometry.RadiusXProperty, r, 620, ease);
                Fx.To(hole, EllipseGeometry.RadiusYProperty, r, 620, ease, 0, () => { if (wipe.Source == old) { wipe.Visibility = Visibility.Collapsed; wipe.Source = null; } });
                if (sw != null) Fx.Pop(sw, 0.7);
            }
            Theme.Apply(w, p);
            F<TextBlock>("PaletteName").Text = "Color: " + p.Name;
            try { Directory.CreateDirectory(dir); File.WriteAllText(PaletteFile, p.Name); }
            catch (IOException ex) { Say("No se pudo guardar la paleta: " + ex.Message, false); }
        }

        public async Task Start()
        {
            SetWorking(true, "Leyendo el estado del PC…");
            try { engine = await Task.Run(() => new Engine(dir, Source())); }
            catch (Exception ex) { Say("No se pudieron leer los datos de la app y no se hará ningún cambio. Revisa " + dir + ". Detalle: " + ex.Message, true); Task hid = HideSkeletons(); return; }
            rows = engine.Tweaks.Select(t => new Row(t)).ToList();
            Dictionary<string, string> icons = new Dictionary<string, string> {
                { "Fortnite", "Game" }, { "Gráficos y captura", "Gpu" }, { "Energía y CPU", "Bolt" }, { "Ratón y teclado", "Mouse" }, { "Red", "Net" },
                { "Segundo plano", "Layers" }, { "Servicios", "Server" }, { "Tareas programadas", "Clock" }, { "Interrupciones", "Bell" },
                { "Publicidad", "Tag" }, { "Búsqueda", "Search" }, { "Telemetría", "Eye" }, { "Interfaz", "Window" } };
            circuits = new ObservableCollection<Circuit> { new Circuit { Name = "Jugar", Icon = Ico("Launch"), Page = "StretchPage" }, new Circuit { Name = "Mi PC", Icon = Ico("Pc"), Page = "PcPage" },
                new Circuit { Name = "Mis FPS", Icon = Ico("Gauge"), Page = "FpsPage" } };
            circuits.Add(new Circuit { Name = "Macros", Icon = Ico("Keys"), Page = "MacroPage" });
            upgrades = new Circuit { Name = "Mejoras", Icon = Ico("Up"), Page = "UpgradePage" };
            circuits.Add(upgrades);
            subs = new List<Circuit> { new Circuit { Name = "Todos", Rows = rows, Icon = Ico("All"), Sub = true } };
            foreach (IGrouping<string, Row> g in rows.GroupBy(r => r.T.Cat))
                subs.Add(new Circuit { Name = g.Key, Rows = g.ToList(), Icon = Ico(icons.ContainsKey(g.Key) ? icons[g.Key] : "All"), Sub = true });
            parts = new List<Part> {
                new Part { Key = "Gpu", Title = "Gráfica", Icon = Ico("Gpu") }, new Part { Key = "Cpu", Title = "Procesador", Icon = Ico("Cpu") },
                new Part { Key = "Ram", Title = "Memoria", Icon = Ico("Ram") }, new Part { Key = "Disk", Title = "Disco", Icon = Ico("Disk") },
                new Part { Key = "Net", Title = "Red", Icon = Ico("Net") }, new Part { Key = "Screen", Title = "Pantalla", Icon = Ico("Pc") } };
            foreach (Part p in parts) p.Set(p.Title, "Sin escanear");

            bool first = !engine.List().Any(s => s.Kind == "original");
            string fail = null;
            try { if (first) await Task.Run(() => engine.Save("Original", "original")); }
            catch (Exception ex) { fail = ex.Message; }
            if (fail != null) { Say("No se pudo crear el guardado original y no se hará ningún cambio. Detalle: " + fail, true); await HideSkeletons(); return; }

            F<ItemsControl>("Parts").ItemsSource = parts;
            F<ListBox>("Circuits").ItemsSource = circuits;
            ListBox nav = F<ListBox>("Circuits");
            nav.SelectionChanged += (s, e) => Navigate();
            nav.PreviewMouseLeftButtonDown += (s, e) =>
            {
                FrameworkElement hit = e.OriginalSource as FrameworkElement;
                if (hit == null || hit.DataContext != upgrades) return;
                // Abre siempre; solo se cierra si ya estabas en Mejoras.
                bool open = !(upgrades.Open && nav.SelectedItem == upgrades);
                w.Dispatcher.BeginInvoke(new Action(() => Expand(open)), DispatcherPriority.Input);
            };
            nav.PreviewKeyDown += (s, e) =>
            {
                if (nav.SelectedItem != upgrades) return;
                if (e.Key == Key.Enter || e.Key == Key.Space) Expand(!upgrades.Open);
                else if (e.Key == Key.Right) Expand(true);
                else if (e.Key == Key.Left) Expand(false);
                else return;
                e.Handled = true;
            };
            F<TextBox>("Search").TextChanged += (s, e) => Filter(false);
            F<CheckBox>("OnlyOff").Click += (s, e) => Filter(true);
            F<ListBox>("Rows").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(RowClick));
            F<ListBox>("Saves").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(SaveClick));
            F<Btn>("Scan").Click += async (s, e) => { if (hw == null) await Scan(); else await Bulk(Wanted(), true); };
            F<Btn>("Rescan").Click += async (s, e) => await Scan();
            F<Btn>("ShowRec").Click += (s, e) => Open(Recommended);
            F<Btn>("AllOn").Click += async (s, e) => await Bulk(rows, true);
            F<Btn>("AllOff").Click += async (s, e) => await Bulk(rows, false);
            F<Btn>("CatOn").Click += async (s, e) => await Bulk(Shown(), true);
            F<Btn>("CatOff").Click += async (s, e) => await Bulk(Shown(), false);
            F<Btn>("SaveNow").Click += async (s, e) => await SaveNow();
            F<TextBox>("SaveName").KeyDown += async (s, e) => { if (e.Key == Key.Enter) await SaveNow(); };
            F<Grid>("Meter").SizeChanged += (s, e) => Meter(false);
            w.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control) return;
                Circuit at = nav.SelectedItem as Circuit;
                if (at == null || at.Rows == null) Open("Todos");
                F<TextBox>("Search").Focus();
                e.Handled = true;
            };
            w.Closing += (s, e) =>
            {
                if (working) { e.Cancel = true; Say("Hay cambios en curso. Espera a que terminen.", true); }
                else if (HideOnClose()) e.Cancel = true;
                else if (stretch != null && stretch.Pending && IniOp.GameRunning()) { e.Cancel = true; Say("Tweakeroo debe quedar abierto hasta cerrar Fortnite para restaurar la resolución.", true); }
                else if (stretch != null && stretch.Pending) try { stretch.End(); } catch (Exception ex) { MessageBox.Show(w, ex.Message, "Tweakeroo"); }
            };
            F<ItemsControl>("Checks").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(CheckClick));
            F<Btn>("Review").Click += async (s, e) => await Scan();
            StartStretch();
            StartMacros();
            StartSettings();
            w.Closed += (s, e) => Macros.Stop();

            await Refresh();
            ScanData saved = await Task.Run(() => Scanner.Load(dir));
            if (saved != null) ApplyScan(saved);
            ready = true;
            slow.Stop();
            // El canguro levanta las orejas cuando la app termina de cargar.
            ScaleTransform ears = new ScaleTransform();
            F<FrameworkElement>("LogoEars").RenderTransform = ears;
            Fx.From(ears, ScaleTransform.ScaleYProperty, 0.25, 1, 700, Fx.Spring, 150);
            nav.SelectedItem = circuits.First(x => x.Name == Prefs.Now.Page);
            await HideSkeletons();
            for (int i = 0; i < circuits.Count; i++)
            {
                FrameworkElement item = nav.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (item != null) Fx.Enter(item, i, 14);
            }
            SetWorking(false, first ? "Estado actual guardado como Original. Se puede restaurar en cualquier momento." : "Listo. Los interruptores muestran el estado real del PC.");
            if (saved != null) { Task again = Recheck(); }
        }

        // Motivo para no reiniciar la app ahora, o null.
        internal string Busy() { return working ? "Hay un cambio en curso. Espera a que termine." : stretch != null && stretch.Pending ? "Cierra Fortnite primero: la resolución aún no se ha restaurado." : null; }

        // ---- Menu ----

        internal void Open(string name)
        {
            if (name == "Ajustes") { OpenSettings(); return; }
            Circuit c = circuits.Concat(subs).FirstOrDefault(x => x.Name == name);
            if (c == null) return;
            if (c.Sub) Expand(true);
            F<ListBox>("Circuits").SelectedItem = c;
        }

        static T Find<T>(DependencyObject o, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(o, i);
                T t = c as T;
                if (t != null && t.Name == name) return t;
                t = Find<T>(c, name);
                if (t != null) return t;
            }
            return null;
        }

        // Las secciones salen de Mejoras y caen a su sitio con rebote; al cerrar vuelven a ella.
        void Expand(bool open)
        {
            if (upgrades.Open == open) return;
            upgrades.Open = open;
            ListBox l = F<ListBox>("Circuits");
            if (open && !circuits.Contains(subs[0])) foreach (Circuit c in subs) circuits.Add(c);
            if (!open && subs.Contains(l.SelectedItem)) l.SelectedItem = upgrades;
            l.UpdateLayout();
            FrameworkElement head = (FrameworkElement)l.ItemContainerGenerator.ContainerFromItem(upgrades);
            FrameworkElement chev = Find<FrameworkElement>(head, "Chevron");
            if (chev != null)
            {
                RotateTransform r = chev.RenderTransform as RotateTransform;
                if (r == null) { r = new RotateTransform(); chev.RenderTransformOrigin = new Point(0.5, 0.5); chev.RenderTransform = r; }
                Fx.To(r, RotateTransform.AngleProperty, open ? 180 : 0, open ? 480 : 180, open ? Fx.Spring : Fx.Out);
            }
            double top = VisualTreeHelper.GetOffset(head).Y;
            for (int i = 0; i < subs.Count; i++)
            {
                FrameworkElement item = l.ItemContainerGenerator.ContainerFromItem(subs[i]) as FrameworkElement;
                if (item == null) continue;
                double y = VisualTreeHelper.GetOffset(item).Y - top;
                TranslateTransform t = new TranslateTransform();
                item.RenderTransform = t;
                if (open)
                {
                    Fx.From(t, TranslateTransform.YProperty, -y, 0, 520, Fx.Bounce(0.58), i * 8);
                    item.BeginAnimation(UIElement.OpacityProperty, null);
                    Fx.From(item, UIElement.OpacityProperty, 0, 1, 110, Fx.Out, i * 8);
                }
                else
                {
                    Action done = null;
                    if (i == subs.Count - 1) done = () => { if (!upgrades.Open) foreach (Circuit c in subs) circuits.Remove(c); };
                    Fx.To(t, TranslateTransform.YProperty, -y, 170, Fx.In);
                    Fx.To(item, UIElement.OpacityProperty, 0, 170, Fx.In, 0, done);
                }
            }
        }

        // ---- Jugar ----

        void StartStretch()
        {
            watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            watch.Tick += (s, e) => Watch();
            BitmapImage photo = new BitmapImage();
            photo.BeginInit();
            photo.StreamSource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("personaje.jpg");
            photo.CacheOption = BitmapCacheOption.OnLoad;
            photo.EndInit();
            foreach (string n in new[] { "FigureNormal", "FigureWide" }) ((ImageBrush)w.FindName(n)).ImageSource = photo;
            ListBox l = F<ListBox>("Resolutions");
            Btn play = F<Btn>("Play");
            List<Res> list;
            try
            {
                stretch = new StretchSession(dir);
                Res native = Display.Saved();
                native.Note = "Normal, " + native.Hz + " Hz";
                list = Display.Stretched(Display.Modes(), native);
                foreach (Res r in list) r.Note = "Stretched, " + r.Hz + " Hz";
                list.Insert(0, native);
            }
            catch (Exception ex) { play.IsEnabled = false; F<TextBlock>("StretchInfo").Text = "No se pudo preparar el stretched: " + ex.Message; return; }
            l.ItemsSource = list;
            l.SelectionChanged += (s, e) => { Preview(); if (F<Border>("ResPanel").IsVisible) ToggleRes(false); };
            l.SelectedItem = list.FirstOrDefault(r => r.Same(stretch.Last)) ?? (list.Count > 1 ? list[1] : list[0]);
            F<Btn>("ResOpen").Click += (s, e) => ToggleRes(!F<Border>("ResPanel").IsVisible);
            F<Btn>("CustomGo").Click += (s, e) => UseCustom();
            foreach (string n in new[] { "CustomW", "CustomH" }) F<TextBox>(n).KeyDown += (s, e) => { if (e.Key == Key.Enter) UseCustom(); };
            bool can = Custom.Available();
            F<WrapPanel>("CustomRow").Visibility = can ? Visibility.Visible : Visibility.Collapsed;
            F<TextBlock>("CustomHint").Text = can ? "Admite casi cualquier resolución menor que la de la pantalla. Se crea en modo de prueba y solo se guarda al confirmarla." : "Esta gráfica no permite crear resoluciones desde la app. Créala en el panel del fabricante y aparecerá en la lista.";
            List<LimitOption> limits = new List<LimitOption> { new LimitOption { Name = "El de Fortnite", Note = "Sin cambios", Value = "" }, new LimitOption { Name = Display.Saved().Hz + " FPS", Note = "Igual que la pantalla", Value = Display.Saved().Hz.ToString() }, new LimitOption { Name = "Sin límite", Note = "Menos retraso, más calor", Value = "0" } };
            ListBox lim = F<ListBox>("Limits");
            lim.ItemsSource = limits;
            lim.SelectedItem = limits.FirstOrDefault(x => x.Value == stretch.Limit) ?? limits[0];
            lim.SelectionChanged += (s, e) => Try(() => stretch.Limit = ((LimitOption)lim.SelectedItem).Value);
            CheckBox min = F<CheckBox>("Minimize");
            min.IsChecked = stretch.Minimize;
            min.Click += (s, e) => Try(() => stretch.Minimize = min.IsChecked == true);
            play.Click += (s, e) => Play();
            F<Btn>("Back").Click += (s, e) => StopStretch("Resolución restaurada.");
            if (list.Count == 1) F<TextBlock>("StretchInfo").Text = "La pantalla no ofrece ninguna resolución más estrecha. Para usar stretched, crea 1720 × 1080 en el panel de la gráfica y reinicia la app.";
            if (!stretch.Pending) return;
            // La app se cerro a medias la ultima vez.
            if (IniOp.GameRunning()) { gameSeen = true; watch.Start(); ShowStretch(); }
            else StopStretch(null);
        }

        // La foto de la derecha se ensancha igual que lo haran los personajes.
        void Preview()
        {
            Res r = F<ListBox>("Resolutions").SelectedItem as Res;
            if (r == null) return;
            double native = Display.Saved().Aspect, wide = native / r.Aspect;
            F<TextBlock>("StretchInfo").Text = wide < 1.005 ? "Sin estirar: los personajes se ven normales." : "Personajes " + Math.Round((wide - 1) * 100) + " % más anchos y " + Math.Round((1 - r.Aspect / native) * 100) + " % menos de visión lateral.";
            Fx.To((ScaleTransform)w.FindName("FigureScale"), ScaleTransform.ScaleXProperty, wide, 560, Fx.Spring);
            F<Btn>("ResOpen").Content = r.Name + "   " + L.T(r.Note);
            if (stretch != null && !stretch.Pending) ShowStretch();
            ShowPlay();
        }

        // La lista se despliega con rebote debajo del boton.
        void ToggleRes(bool open)
        {
            Border p = F<Border>("ResPanel");
            p.BeginAnimation(UIElement.OpacityProperty, null);
            if (!open) { Fx.To(p, UIElement.OpacityProperty, 0, 130, Fx.In, 0, () => p.Visibility = Visibility.Collapsed); return; }
            p.Visibility = Visibility.Visible;
            ListBox l = F<ListBox>("Resolutions");
            l.UpdateLayout();
            for (int i = 0; i < l.Items.Count; i++)
            {
                FrameworkElement c = l.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (c == null) continue;
                Fx.Pop(c, 0.6, i * 14);
                Fx.From(c, UIElement.OpacityProperty, 0, 1, 120, Fx.Out, i * 14);
            }
        }

        void UseCustom()
        {
            Res native = Display.Saved();
            string ws = F<TextBox>("CustomW").Text.Trim(), hs = F<TextBox>("CustomH").Text.Trim(), bad = Custom.Invalid(ws, hs, native);
            if (bad != null) { F<TextBlock>("CustomHint").Text = bad; return; }
            ListBox l = F<ListBox>("Resolutions");
            List<Res> list = l.Items.Cast<Res>().ToList();
            Res r = new Res { W = int.Parse(ws), H = int.Parse(hs), Hz = native.Hz };
            Res had = list.FirstOrDefault(x => x.Same(r));
            if (had == null)
            {
                Res mode = Display.Modes().Where(m => m.Same(r)).OrderByDescending(m => m.Hz).FirstOrDefault();
                if (mode != null) r.Hz = mode.Hz;
                r.Note = "Personalizada, " + r.Hz + " Hz";
                list.Add(r);
                l.ItemsSource = list;
                had = r;
            }
            l.SelectedItem = had;
        }

        async Task LoadPlay()
        {
            await LoadFps();
            ShowPlay();
            Dictionary<string, string> heavy = null;
            await Task.Run(() =>
            {
                int session = Process.GetCurrentProcess().SessionId;
                string[] skip = { "Tweakeroo", "explorer", "dwm", "Memory Compression", "FortniteClient-Win64-Shipping", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "svchost" };
                try { heavy = Process.GetProcesses().Where(p => p.SessionId == session && !skip.Contains(p.ProcessName)).GroupBy(p => p.ProcessName).Select(g => new { g.Key, Mb = g.Sum(p => p.WorkingSet64) / (1 << 20) }).Where(x => x.Mb >= 300).OrderByDescending(x => x.Mb).Take(5).ToDictionary(x => x.Key, x => x.Mb >= 1024 ? (x.Mb / 1024.0).ToString("0.0") + " GB" : x.Mb + " MB"); }
                catch (InvalidOperationException) { heavy = new Dictionary<string, string>(); }
                catch (Win32Exception) { heavy = new Dictionary<string, string>(); }
            });
            F<ItemsControl>("Heavy").ItemsSource = heavy;
        }

        void ShowPlay()
        {
            Res r = F<ListBox>("Resolutions").SelectedItem as Res;
            Hw h = hw ?? quick;
            Estimate e = r == null || h == null ? null : Estimator.For(h, r.W, r.H);
            F<TextBlock>("PlayEst").Text = e == null ? "…" : e.Low + "–" + e.High;
            F<TextBlock>("PlayLast").Text = matches.Count == 0 ? "…" : matches[0].Median + " FPS";
            F<TextBlock>("PlayRam").Text = Display.FreeRamGb().ToString("0.0") + " GB";
        }

        void ShowStretch()
        {
            Btn play = F<Btn>("Play");
            Res r = F<ListBox>("Resolutions").SelectedItem as Res;
            bool on = stretch.Pending;
            play.IsEnabled = !on || confirming;
            play.Content = confirming ? play.Content : on ? (IniOp.GameRunning() ? "Jugando a " + r.Name : "Abriendo Fortnite…") : r == null ? "Abrir Fortnite" : "Jugar a " + r.Name;
            F<Btn>("Back").Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            F<Btn>("ResOpen").IsEnabled = F<ListBox>("Limits").IsEnabled = !on;
            if (on && F<Border>("ResPanel").IsVisible) ToggleRes(false);
        }

        void Play()
        {
            if (confirming)
            {
                confirming = false;
                try { stretch.Confirm(stretch.Last); }
                catch (Exception ex) { StopStretch(ex.Message); return; }
                Launch();
                return;
            }
            Res r = F<ListBox>("Resolutions").SelectedItem as Res;
            if (r == null || working) return;
            try { stretch.Begin(r); }
            catch (Exception ex) { ShowStretch(); Say(ex.Message, true); return; }
            // Si la ventana no cabe en la resolucion nueva, los botones tienen que verse igual.
            if (w.ActualWidth > SystemParameters.WorkArea.Width || w.ActualHeight > SystemParameters.WorkArea.Height) w.WindowState = WindowState.Maximized;
            if (stretch.Known(r)) { Launch(); return; }
            confirming = true;
            limit = DateTime.Now.AddSeconds(Prefs.Now.Confirm);
            watch.Start();
            Watch();
            ShowStretch();
            Say("¿Conservar esta resolución? Sin confirmación se restaura la anterior.", true);
        }

        void Launch()
        {
            gameSeen = false;
            limit = DateTime.Now.AddMinutes(10);
            try { StretchSession.Open(StretchSession.Game); }
            catch (Exception ex) { StopStretch("No se pudo abrir Fortnite: " + ex.Message); return; }
            watch.Start();
            ShowStretch();
            Say("Abriendo Fortnite. La resolución se restaura al cerrarlo; Tweakeroo debe quedar abierto.", false);
        }

        void Watch()
        {
            if (confirming)
            {
                int left = (int)Math.Ceiling((limit - DateTime.Now).TotalSeconds);
                if (left <= 0) StopStretch("Sin respuesta: resolución restaurada.");
                else F<Btn>("Play").Content = "Conservar (" + left + ")";
                return;
            }
            bool running = IniOp.GameRunning();
            Precise.Set(running && Precise.Wanted);
            if (running && !gameSeen && stretch.Minimize) w.WindowState = WindowState.Minimized;
            if (running) gameSeen = true;
            else if (gameSeen) { StopStretch("Fortnite cerrado: resolución restaurada."); return; }
            else if (DateTime.Now > limit) { StopStretch("Fortnite no se abrió en 10 minutos: resolución restaurada."); return; }
            ShowStretch();
        }

        void StopStretch(string msg)
        {
            confirming = false;
            if (stretch.Minimize && w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            string err = null;
            try { stretch.End(); } catch (Exception ex) { err = ex.Message; }
            // Con el juego abierto falta reponer su archivo: se sigue vigilando.
            if (stretch.Pending && IniOp.GameRunning()) { gameSeen = true; watch.Start(); if (err == null) msg = "Pantalla restaurada. La configuración de Fortnite se restaura al cerrar el juego."; }
            else { watch.Stop(); Precise.Set(false); }
            ShowStretch();
            if (err != null || msg != null) Say(err ?? msg, err != null);
        }
        static void Try(Action a) { try { a(); } catch (IOException) { } }

        // ---- Macros ----

        MacroConfig macros;
        List<MacroRow> macroRows;
        Action<int> capture;   // recibe la proxima tecla o boton
        int captureAt;

        void StartMacros()
        {
            macros = Macros.Load(dir);
            macroRows = new List<MacroRow> {
                new MacroRow { Id = "edit", Name = "Edición rápida", Desc = "Mantener: entra a editar y selecciona. Soltar: confirma." },
                new MacroRow { Id = "doble", Name = "Doble edición", Desc = "Igual que la rápida y, al soltar, edita otra vez donde apunta la mira." },
                new MacroRow { Id = "editreset", Name = "Editar y resetear", Desc = "Igual que la rápida y, al soltar, devuelve la pieza a su forma." },
                new MacroRow { Id = "reset", Name = "Reset", Desc = "Un toque devuelve la pieza a su forma." } };
            F<ItemsControl>("MacroRows").ItemsSource = macroRows;
            F<ItemsControl>("MacroRows").AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(MacroClick));
            F<CheckBox>("MacroAccept").Click += (s, e) => { macros.Accepted = F<CheckBox>("MacroAccept").IsChecked == true; SaveMacros(null); };
            F<CheckBox>("MacroConfirm").Click += (s, e) => { macros.ConfirmOnRelease = F<CheckBox>("MacroConfirm").IsChecked == true; SaveMacros(null); };
            F<Btn>("KeyEdit").Click += (s, e) => GameKey("KeyEdit", vk => macros.Edit = vk);
            F<Btn>("KeySelect").Click += (s, e) => GameKey("KeySelect", vk => macros.Select = vk);
            F<Btn>("KeyReset").Click += (s, e) => GameKey("KeyReset", vk => macros.Reset = vk);
            List<LimitOption> d = new[] { 10, 20, 35, 60 }.Select(x => new LimitOption { Name = x + " ms", Note = x == 10 ? "Puede fallar" : x == 20 ? "Normal" : x == 35 ? "Más segura" : "Lenta", Value = x.ToString() }).ToList();
            ListBox l = F<ListBox>("MacroDelay");
            l.ItemsSource = d;
            l.SelectedItem = d.FirstOrDefault(x => x.Value == macros.Delay.ToString()) ?? d[1];
            l.SelectionChanged += (s, e) => { macros.Delay = int.Parse(((LimitOption)l.SelectedItem).Value); SaveMacros(null); };
            w.PreviewKeyDown += (s, e) =>
            {
                if (capture == null) return;
                e.Handled = true;
                Captured(e.Key == Key.Escape ? 0 : KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key));
            };
            w.PreviewMouseDown += (s, e) =>
            {
                if (capture == null || Environment.TickCount - captureAt < 150) return;
                e.Handled = true;
                Captured(e.ChangedButton == MouseButton.Left ? 1 : e.ChangedButton == MouseButton.Right ? 2 : e.ChangedButton == MouseButton.Middle ? 4 : e.ChangedButton == MouseButton.XButton1 ? 5 : 6);
            };
            w.PreviewMouseWheel += (s, e) =>
            {
                if (capture == null) return;
                e.Handled = true;
                Captured(e.Delta > 0 ? Macros.WheelUp : Macros.WheelDown);
            };
            w.Deactivated += (s, e) => { if (capture != null) Captured(0); };
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += (s, e) => { if (F<FrameworkElement>("MacroPage").IsVisible) F<TextBlock>("MacroState").Text = "Estado: " + Macros.Status; };
            t.Start();
            ShowMacros();
            Macros.Use(macros);
        }

        internal void Captured(int vk) { Action<int> c = capture; capture = null; c(vk); }
        void Capture(Action<int> then) { if (capture != null) Captured(0); captureAt = Environment.TickCount; capture = then; }

        void GameKey(string btn, Action<int> set)
        {
            Capture(vk =>
            {
                if (vk == 0) { ShowMacros(); return; }
                if (btn == "KeySelect" && Macros.Wheel(vk)) { ShowMacros(); Say("La rueda no se puede mantener: elige otra tecla para seleccionar.", false); return; }
                if (macros.Keys.ContainsValue(vk)) { ShowMacros(); Say("Esa tecla ya es de otra macro.", false); return; }
                set(vk);
                SaveMacros(null);
            });
            F<Btn>(btn).Content = "Pulsa una tecla…";
        }

        void MacroClick(object sender, RoutedEventArgs e)
        {
            MacroRow r = (e.OriginalSource as FrameworkElement).DataContext as MacroRow;
            if (r == null) return;
            if (e.OriginalSource is CheckBox)
            {
                bool has = macros.Keys.ContainsKey(r.Id);
                macros.On.Remove(r.Id);
                if (r.On && has) macros.On.Add(r.Id);
                SaveMacros(r.On && !has ? "Elige primero una tecla para esta macro." : null);
                return;
            }
            Capture(vk =>
            {
                string no = vk == 0 ? null : Macros.Refuse(macros, r.Id, vk);
                if (vk == 0 || no != null) { ShowMacros(); if (no != null) Say(no, false); return; }
                macros.Keys[r.Id] = vk;
                if (!macros.On.Contains(r.Id)) macros.On.Add(r.Id);
                SaveMacros(null);
            });
            r.Set("Pulsa una tecla…", r.On);
        }

        void SaveMacros(string msg)
        {
            ShowMacros();
            Macros.Use(macros);
            try { Macros.Save(dir, macros); }
            catch (IOException ex) { msg = "No se pudo guardar: " + ex.Message; }
            catch (UnauthorizedAccessException ex) { msg = "No se pudo guardar: " + ex.Message; }
            if (msg != null) Say(msg, false);
        }

        void ShowMacros()
        {
            foreach (MacroRow r in macroRows)
            {
                int vk;
                macros.Keys.TryGetValue(r.Id, out vk);
                r.Set(Macros.KeyName(vk), macros.On.Contains(r.Id));
            }
            F<Btn>("KeyEdit").Content = Macros.KeyName(macros.Edit);
            F<Btn>("KeySelect").Content = Macros.KeyName(macros.Select);
            F<Btn>("KeyReset").Content = Macros.KeyName(macros.Reset);
            F<CheckBox>("MacroAccept").IsChecked = macros.Accepted;
            F<CheckBox>("MacroConfirm").IsChecked = macros.ConfirmOnRelease;
            F<FrameworkElement>("MacroBody").IsEnabled = macros.Accepted;
            F<FrameworkElement>("MacroBody").Opacity = macros.Accepted ? 1 : 0.45;
            F<TextBlock>("MacroState").Text = "Estado: " + Macros.Status;
        }

        // ---- Mis FPS ----

        Task LoadFps()
        {
            if (fpsTask == null || fpsTask.IsCompleted) fpsTask = ReadFps();
            return fpsTask;
        }
        async Task OpenFps()
        {
            bool first = !fpsRead;
            Task t = LoadFps();
            if (first && await Task.WhenAny(t, Task.Delay(350)) != t) ShowSkeletons("SkelPage");
            await t;
            if (!first) return;
            await HideSkeletons();
            EnterPage("FpsPage");
        }
        async Task ReadFps()
        {
            List<Match> m = null;
            string err = null;
            await Task.Run(() =>
            {
                try { m = FpsLog.Update(dir); } catch (Exception ex) { err = ex.Message; }
                if (hw != null || quick != null) return;
                Hw q = new Hw();
                try { foreach (Scanner.Step s in Scanner.Steps.Where(x => x.Key == "Cpu" || x.Key == "Gpu")) s.Run(q); quick = q; } catch (Exception) { }
            });
            fpsRead = true;
            if (err != null) { F<TextBlock>("FpsText").Text = "No se pudieron leer los registros de Fortnite: " + err; return; }
            matches = m;
            ShowFps();
        }

        void ShowFps()
        {
            bool any = matches.Count > 0;
            F<FrameworkElement>("FpsNow").Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            F<TextBlock>("FpsText").Text = any ? "Se leen de los registros de Fortnite, segundo a segundo, sin modificar el juego." : "No hay partidas de más de un minuto en los registros de Fortnite.";
            if (any)
            {
                Match a = matches[0];
                Fx.CountTo(F<TextBlock>("FpsMedian"), a.Median);
                Fx.CountTo(F<TextBlock>("FpsLow"), a.Low);
                Fx.CountTo(F<TextBlock>("FpsMax"), a.Max);
                F<TextBlock>("FpsLast").Text = "Última partida: " + FpsRow.Day(a) + ", " + a.Map + (a.Cap > 0 ? ", con límite de " + a.Cap + " FPS" : ", sin límite");
                // El ultimo cambio masivo separa el antes del despues.
                Snapshot last = engine.List().Where(s => s.Kind == "auto" && s.Name.StartsWith("Antes de activar")).OrderByDescending(s => s.Date, StringComparer.Ordinal).FirstOrDefault();
                F<TextBlock>("FpsCompare").Text = FpsLog.Compare(matches, last == null ? null : last.Date);
                List<FpsRow> list = matches.Take(30).Select(m => new FpsRow { M = m }).ToList();
                double top = Math.Max(1, list.Max(x => x.M.Median));
                foreach (FpsRow x in list) x.BarW = 100 * x.M.Median / top;
                F<ItemsControl>("FpsRows").ItemsSource = list;
            }
            Hw h = hw ?? quick;
            Res native = Display.Saved();
            Res chosen = F<ListBox>("Resolutions").SelectedItem as Res;
            List<EstimateTile> tiles = new List<EstimateTile>();
            bool gpu = false;
            foreach (Res r in new[] { native, new Res { W = 1920, H = 1080 }, chosen })
            {
                Estimate e = r == null || h == null || tiles.Any(x => x.Size == r.Name) ? null : Estimator.For(h, r.W, r.H);
                if (e == null) continue;
                if (tiles.Count == 0) gpu = e.GpuLimits;
                tiles.Add(new EstimateTile { Label = r == native ? "Pantalla nativa" : r == chosen ? "Stretched" : "Full HD", Size = r.Name, Range = e.Low + "–" + e.High });
            }
            F<ItemsControl>("Estimates").ItemsSource = tiles;
            F<TextBlock>("FpsEstimate").Text = tiles.Count == 0 ? "Sin datos suficientes de esta gráfica o procesador para una estimación fiable."
                : "Aproximado, en modo Rendimiento y sin límite de FPS. A la resolución nativa limita " + (gpu ? "la gráfica." : "el procesador.");
        }

        // ---- Mejoras ----

        void ShowReview()
        {
            List<Check> c = Review.For(hw, matches);
            int todo = c.Count(x => x.Kind == Kind.Gratis), buy = c.Count(x => x.Kind == Kind.Comprar);
            F<TextBlock>("UpTitle").Text = todo + buy == 0 ? "Configuración correcta" : "Mejoras disponibles";
            F<TextBlock>("UpText").Text = (todo == 0 ? "Ninguna mejora gratis pendiente" : todo == 1 ? "1 mejora gratis" : todo + " mejoras gratis") + (buy == 0 ? " y ninguna compra recomendada." : buy == 1 ? " y 1 compra recomendada." : " y " + buy + " compras recomendadas.");
            F<Btn>("Review").Content = "Revisar otra vez";
            F<Btn>("Review").Glint = false;
            F<ItemsControl>("Plan").ItemsSource = Review.Plan(hw, matches);
            F<FrameworkElement>("PlanBox").Visibility = Visibility.Visible;
            ItemsControl l = F<ItemsControl>("Checks");
            l.ItemsSource = c;
            l.UpdateLayout();
            for (int i = 0; i < c.Count; i++)
            {
                FrameworkElement x = l.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (x != null) Fx.Enter(x, i, 18);
            }
        }

        void CheckClick(object sender, RoutedEventArgs e)
        {
            Button b = e.OriginalSource as Button;
            Check c = b == null ? null : b.DataContext as Check;
            if (c == null || c.Url == null) return;
            try { StretchSession.Open(c.Url); } catch (Exception ex) { Say("No se pudo abrir: " + ex.Message, false); }
        }

        List<Row> Shown() { return ((IEnumerable<Row>)F<ListBox>("Rows").ItemsSource).ToList(); }
        List<Row> Wanted() { return rows.Where(r => r.Advice != null && r.Advice.Yes).ToList(); }

        // Aviso inferior: entra con resorte y se va solo, un poco mas rapido.
        void Say(string msg, bool stay)
        {
            Border t = F<Border>("Toast");
            F<TextBlock>("Status").Text = msg;
            TransformGroup g = t.RenderTransform as TransformGroup;
            if (g == null)
            {
                g = new TransformGroup();
                g.Children.Add(new ScaleTransform(0.95, 0.95));
                g.Children.Add(new TranslateTransform(0, 36));
                t.RenderTransformOrigin = new Point(0.5, 1);
                t.RenderTransform = g;
            }
            if (t.Opacity < 0.9)
            {
                Fx.To(g.Children[0], ScaleTransform.ScaleXProperty, 1, 560, Fx.Spring);
                Fx.To(g.Children[0], ScaleTransform.ScaleYProperty, 1, 560, Fx.Spring);
                Fx.To(g.Children[1], TranslateTransform.YProperty, 0, 560, Fx.Spring);
                Fx.To(t, UIElement.OpacityProperty, 1, 160, Fx.Out);
            }
            hide.Stop();
            if (!stay) hide.Start();
        }

        void HideToast()
        {
            Border t = F<Border>("Toast");
            TransformGroup g = t.RenderTransform as TransformGroup;
            if (g == null) return;
            Fx.To(g.Children[1], TranslateTransform.YProperty, 36, 200, Fx.In);
            Fx.To(g.Children[0], ScaleTransform.ScaleXProperty, 0.95, 200, Fx.In);
            Fx.To(g.Children[0], ScaleTransform.ScaleYProperty, 0.95, 200, Fx.In);
            Fx.To(t, UIElement.OpacityProperty, 0, 180, Fx.In);
        }

        void SetProgress(double p)
        {
            F<Grid>("Progress").Visibility = p < 0 ? Visibility.Collapsed : Visibility.Visible;
            Fx.To(F<Border>("ProgressFill").RenderTransform, ScaleTransform.ScaleXProperty, Math.Max(0, p), p <= 0 ? 1 : 220, Fx.Out);
        }

        void SetWorking(bool on, string msg)
        {
            working = on;
            foreach (string n in new[] { "Scan", "Rescan", "AllOn", "AllOff", "CatOn", "CatOff", "SaveNow", "Review" }) F<Btn>(n).IsEnabled = !on;
            F<ListBox>("Saves").IsEnabled = !on;
            SetProgress(-1);
            if (msg != null) Say(msg, on);
        }

        void Navigate()
        {
            Circuit c = F<ListBox>("Circuits").SelectedItem as Circuit;
            if (c == null) return;
            MoveMark();
            foreach (string p in Pages) F<FrameworkElement>(p).Visibility = p == c.Page ? Visibility.Visible : Visibility.Collapsed;
            if (c.Page != "FpsPage" && ready && F<FrameworkElement>("SkelPage").Opacity > 0) F<FrameworkElement>("SkelPage").Visibility = Visibility.Collapsed;
            if (c.Page == "PcPage") Meter(true);
            if (c.Page == "FpsPage") { Task t = OpenFps(); }
            if (c.Page == "StretchPage") { Task t = LoadPlay(); }
            if (c.Rows == null) { if (ready) EnterPage(c.Page); return; }
            F<TextBox>("Search").Text = "";
            Filter(true);
        }

        // La marca de seleccion es una sola pieza que viaja entre secciones.
        void MoveMark()
        {
            ListBox l = F<ListBox>("Circuits");
            l.UpdateLayout();
            FrameworkElement item = l.ItemContainerGenerator.ContainerFromItem(l.SelectedItem) as FrameworkElement;
            if (item == null) return;
            Border m = F<Border>("NavMark");
            // Posicion de reposo: sin la animacion que pueda llevar el elemento.
            double y = VisualTreeHelper.GetOffset(item).Y;
            m.Height = item.ActualHeight;
            m.Visibility = Visibility.Visible;
            TranslateTransform t = m.RenderTransform as TranslateTransform;
            if (t == null || t.IsFrozen) { m.RenderTransform = new TranslateTransform(0, y); return; }
            Fx.To(t, TranslateTransform.YProperty, y, 560, Fx.Bounce(0.62));
        }

        void Filter(bool animate)
        {
            Circuit c = F<ListBox>("Circuits").SelectedItem as Circuit;
            if (c == null || c.Rows == null) return;
            string q = F<TextBox>("Search").Text.Trim();
            bool off = F<CheckBox>("OnlyOff").IsChecked == true;
            // Con texto en el buscador se busca en todo.
            IEnumerable<Row> src = q.Length > 0 ? rows : c.Rows;
            CompareInfo ci = CultureInfo.GetCultureInfo("es").CompareInfo;
            const CompareOptions o = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            List<Row> r = src.Where(x => (q.Length == 0 || ci.IndexOf(L.T(x.Name) + " " + L.T(x.Desc), q, o) >= 0) && (!off || x.State != State.On)).ToList();
            ListBox list = F<ListBox>("Rows");
            list.ItemsSource = r;
            F<TextBlock>("CatTitle").Text = q.Length > 0 ? "Búsqueda: " + r.Count + (r.Count == 1 ? " tweak" : " tweaks") : c.Name;
            TextBlock empty = F<TextBlock>("Empty");
            empty.Visibility = r.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            empty.Text = q.Length > 0 ? "Sin resultados para “" + q + "”. Usa una palabra más corta." : "Todo activado en esta sección.";
            if (r.Count > 0) list.ScrollIntoView(r[0]);
            if (!animate) return;
            list.UpdateLayout();
            for (int i = 0; i < Math.Min(r.Count, 12); i++)
            {
                FrameworkElement item = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (item != null) Fx.Enter(item, i, 18);
            }
        }

        void Meter(bool animate)
        {
            Grid m = F<Grid>("Meter");
            Border fill = F<Border>("MeterFill");
            if (rows == null || m.ActualWidth <= 0) return;
            List<Row> goal = hw == null ? rows : Wanted();
            double p = goal.Count == 0 ? 0 : goal.Count(r => r.State == State.On) / (double)goal.Count;
            m.Clip = new RectangleGeometry(new Rect(0, 0, m.ActualWidth, m.ActualHeight), 8, 8);
            fill.Visibility = Visibility.Visible;
            TranslateTransform t = fill.RenderTransform as TranslateTransform;
            bool fresh = t == null || t.IsFrozen;
            if (fresh) { t = new TranslateTransform(-m.ActualWidth, 0); fill.RenderTransform = t; }
            Fx.To(t, TranslateTransform.XProperty, -(1 - p) * m.ActualWidth, animate || fresh ? 700 : 1, Fx.Out);
        }

        void Totals()
        {
            foreach (Circuit c in subs) c.Refresh();
            List<Row> goal = hw == null ? rows : Wanted();
            int on = goal.Count(r => r.State == State.On), left = goal.Count - on;
            Fx.CountTo(F<TextBlock>("TotalOn"), on);
            F<TextBlock>("TotalText").Text = "de " + goal.Count + (hw == null ? " tweaks activados" : " recomendados activados");
            Btn b = F<Btn>("Scan");
            if (hw == null)
                F<TextBlock>("PcText").Text = "Analiza los componentes y recomienda solo los tweaks adecuados. Antes de aplicar cambios se muestra la lista y se guarda una copia.";
            else
            {
                b.Content = left == 0 ? "Todo listo" : left == 1 ? "Activar 1 recomendado" : "Activar " + left + " recomendados";
                b.Icon = Ico(left == 0 ? "Check" : "Bolt");
                b.Glint = left > 0;
                F<TextBlock>("PcTitle").Text = left == 0 ? "PC optimizado" : "Recomendado para este PC";
                F<TextBlock>("PcText").Text = left == 0 ? "Todo lo recomendado está activado." : goal.Count + " tweaks recomendados para estos componentes. Los otros " + (rows.Count - goal.Count) + " son opcionales o no aplican.";
            }
            Meter(true);
        }

        async Task Scan()
        {
            if (working) return;
            SetWorking(true, "Escaneando el PC…");
            F<Btn>("Scan").Content = "Escaneando…";
            Hw found = new Hw();
            Dictionary<string, string> texts = new Dictionary<string, string>();
            List<string> fails = new List<string>();
            for (int i = 0; i < Scanner.Steps.Count; i++)
            {
                Scanner.Step step = Scanner.Steps[i];
                string text = null;
                await Task.Run(() => { try { text = step.Run(found); } catch (Exception) { } });
                Part p = parts.First(x => x.Key == step.Key);
                if (text == null) { fails.Add(p.Title.ToLowerInvariant()); p.Set(p.Title, "No se pudo leer"); }
                else { texts[step.Key] = text; ShowPart(p, text); }
                FrameworkElement card = F<ItemsControl>("Parts").ItemContainerGenerator.ContainerFromItem(p) as FrameworkElement;
                if (card != null) Fx.Pop(card, 0.9);
                SetProgress((i + 1.0) / Scanner.Steps.Count);
            }
            if (fails.Count > 0)
            {
                // Con datos a medias no se recomienda nada: mejor volver a intentar.
                hw = null;
                F<Btn>("Scan").Content = "Escanear otra vez";
                SetWorking(false, "No se pudo leer: " + string.Join(", ", fails) + ". Sin esos datos no hay recomendaciones; repite el escaneo.");
                return;
            }
            await Task.Run(() => Review.Read(found));
            await LoadFps();
            ScanData d = new ScanData { Hw = found, Text = texts };
            ApplyScan(d);
            ShowFps();
            Scanner.Save(dir, d);
            Fx.Pop(F<Btn>("Scan"), 0.92);
            SetWorking(false, "Escaneo completado. Los resultados están en Recomendados.");
        }

        static void ShowPart(Part p, string text)
        {
            string[] s = text.Split('\n');
            p.Set(s[0], p.Title + (s.Length > 1 && s[1].Length > 0 ? ". " + s[1] : ""));
        }

        // Con el escaneo guardado ya en pantalla, mira por detras si cambio algo (cable, disco, driver, una pieza).
        async Task Recheck()
        {
            Hw before = hw;
            ScanData now = null;
            await Task.Run(() => { now = Scanner.Run(); });
            await LoadFps();
            if (working || hw != before) return;
            if (now == null) { ShowReview(); return; }
            ApplyScan(now);
            ShowFps();
            Scanner.Save(dir, now);
        }

        void ApplyScan(ScanData d)
        {
            hw = d.Hw;
            foreach (Part p in parts) ShowPart(p, d.Text[p.Key]);
            ShowReview();
            foreach (Row r in rows) r.SetAdvice(Advisor.For(r.T, hw));
            Circuit rec = subs.FirstOrDefault(c => c.Name == Recommended);
            if (rec == null)
            {
                rec = new Circuit { Name = Recommended, Icon = Ico("Check"), Sub = true };
                subs.Insert(0, rec);
                if (circuits.Contains(subs[1])) circuits.Insert(circuits.IndexOf(upgrades) + 1, rec);
            }
            rec.Rows = Wanted();
            MoveMark();
            ItemsControl notes = F<ItemsControl>("Notes");
            notes.ItemsSource = Advisor.Notes(hw);
            notes.UpdateLayout();
            for (int i = 0; i < notes.Items.Count; i++)
            {
                FrameworkElement n = notes.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (n != null) Fx.Enter(n, i, 18);
            }
            F<Btn>("Rescan").Visibility = F<Btn>("ShowRec").Visibility = Visibility.Visible;
            Totals();
        }

        async Task Refresh()
        {
            Dictionary<Row, State> st = null;
            List<SaveRow> sv = null;
            await Task.Run(() =>
            {
                Dictionary<string, string> now = engine.ReadAll();
                st = rows.ToDictionary(r => r, r => r.T.Read(now));
                sv = engine.List().Select(s => new SaveRow { S = s, Diff = engine.DiffCount(s, now) }).ToList();
            });
            foreach (KeyValuePair<Row, State> p in st) if (!ready || p.Key.State != p.Value) p.Key.SetState(p.Value);
            ListBox l = F<ListBox>("Saves");
            HashSet<string> had = new HashSet<string>(l.Items.Cast<SaveRow>().Select(x => x.S.File));
            l.ItemsSource = sv;
            if (ready)
            {
                l.UpdateLayout();
                foreach (SaveRow s in sv.Where(x => !had.Contains(x.S.File)))
                {
                    FrameworkElement i = l.ItemContainerGenerator.ContainerFromItem(s) as FrameworkElement;
                    if (i != null) Fx.Pop(i, 0.9);
                }
            }
            Totals();
        }

        async Task SafeRefresh()
        {
            try { await Refresh(); }
            catch (Exception ex) { Say("No se pudo actualizar el estado: " + ex.Message, true); }
        }

        // Punto de restauracion de Windows, una sola vez, antes del primer cambio.
        string RestorePoint()
        {
            string flag = Path.Combine(dir, "punto-de-restauracion.txt");
            if (File.Exists(flag)) return "";
            const string cmd = "$ErrorActionPreference='Stop'; try { Checkpoint-Computer -Description 'Tweakeroo' -RestorePointType MODIFY_SETTINGS -WarningAction SilentlyContinue; if ((Get-ComputerRestorePoint | Select-Object -Last 1).Description -eq 'Tweakeroo') { exit 0 } } catch {}; exit 1";
            ProcessStartInfo si = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -NonInteractive -Command \"" + cmd + "\"") { UseShellExecute = false, CreateNoWindow = true };
            bool ok = false;
            using (Process p = Process.Start(si))
            {
                if (p.WaitForExit(120000)) ok = p.ExitCode == 0;
                else p.Kill();
            }
            File.WriteAllText(flag, (ok ? "creado " : "no creado ") + DateTime.Now);
            return ok ? "" : " Windows no creó un punto de restauración; los guardados de la app siguen disponibles.";
        }

        async void RowClick(object sender, RoutedEventArgs e)
        {
            Button b = e.OriginalSource as Button;
            Row r = b == null ? null : b.DataContext as Row;
            if (r == null || working) return;
            bool on = r.State != State.On;
            if (on && r.T.Level == Level.Riesgo && MessageBox.Show(w, L.T(r.T.Desc) + "\n\n" + L.T("¿Activar de todas formas?"), L.T(r.T.Name), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            SetWorking(true, null);
            // La perilla responde al momento; si Windows lo rechaza, Refresh la devuelve.
            r.SetState(on ? State.On : State.Off);
            string err = null, note = "";
            await Task.Run(() =>
            {
                try { note = RestorePoint(); if (on) engine.Enable(r.T); else engine.Disable(r.T, false); }
                catch (Exception ex) { err = ex.Message; }
            });
            r.SetError(err ?? "");
            await SafeRefresh();
            SetWorking(false, err != null ? "No se cambió: " + r.Name + "." : r.Name + (on ? ": activado." : ": quitado.") + (r.T.Restart.Length > 0 ? " Reinicia " + r.T.Restart + " para que haga efecto." : "") + note);
        }

        async Task Bulk(List<Row> target, bool on)
        {
            if (working) return;
            List<Row> todo = target.Where(r => on ? r.State != State.On && r.T.Level != Level.Riesgo : r.State != State.Off && engine.Mine(r.T)).ToList();
            bool game = IniOp.GameRunning();
            int skipped = game ? todo.RemoveAll(r => r.T.Ops.Any(o => o is IniOp)) : 0;
            string skip = skipped > 0 ? " Fortnite está abierto: " + skipped + " ajustes del juego quedaron sin tocar." : "";
            if (todo.Count == 0)
            {
                SetWorking(false, (on ? "No queda nada por activar en esta sección." : "Nada que deshacer: lo activado ya estaba así antes de usar Tweakeroo. Se puede quitar uno a uno.") + skip);
                return;
            }
            SetWorking(true, "Guardando el estado actual…");
            string note = "", stop = null;
            int ok = 0;
            Dictionary<Row, string> errs = new Dictionary<Row, string>();
            await Task.Run(() =>
            {
                try { note = RestorePoint(); engine.Save(on ? "Antes de activar " + todo.Count : "Antes de deshacer " + todo.Count, "auto"); }
                catch (Exception ex) { stop = ex.Message; return; }
                for (int i = 0; i < todo.Count; i++)
                {
                    Row r = todo[i];
                    string line = (on ? "Activando " : "Deshaciendo ") + (i + 1) + " de " + todo.Count + ": " + r.Name;
                    double part = (i + 1.0) / todo.Count;
                    w.Dispatcher.BeginInvoke(new Action(() => { F<TextBlock>("Status").Text = line; SetProgress(part); }));
                    try { if (on) engine.Enable(r.T); else engine.Disable(r.T, true); ok++; }
                    catch (Exception ex) { errs[r] = ex.Message; }
                }
            });
            foreach (Row r in todo) r.SetError(errs.ContainsKey(r) ? errs[r] : "");
            await SafeRefresh();
            string msg = stop != null ? "Ningún cambio aplicado: falló el guardado previo: " + stop
                : ok + (on ? " activados." : " deshechos.") + (errs.Count > 0 ? " " + errs.Count + " fallaron y quedaron como estaban; el motivo está en cada fila." : "")
                  + (todo.Any(r => !errs.ContainsKey(r) && r.T.Restart.Length > 0) ? " Reinicia el PC para completar los cambios." : "");
            SetWorking(false, msg + skip + note);
        }

        async Task SaveNow()
        {
            if (working) return;
            string name = F<TextBox>("SaveName").Text.Trim();
            if (name.Length == 0) name = "Guardado manual";
            SetWorking(true, "Guardando…");
            string err = null;
            await Task.Run(() => { try { engine.Save(name, "manual"); } catch (Exception ex) { err = ex.Message; } });
            if (err == null) F<TextBox>("SaveName").Text = "";
            await SafeRefresh();
            SetWorking(false, err == null ? "Guardado: " + name + "." : "No se pudo guardar: " + err);
        }

        async void SaveClick(object sender, RoutedEventArgs e)
        {
            Button b = e.OriginalSource as Button;
            SaveRow s = b == null ? null : b.DataContext as SaveRow;
            if (s == null || working) return;
            bool restore = (string)b.CommandParameter == "restore";
            if (s.Arm(restore)) await SaveAction(s, restore);
        }
        async Task SaveAction(SaveRow s, bool restore)
        {
            SetWorking(true, restore ? "Restaurando " + s.Name + "…" : "Borrando " + s.Name + "…");
            string err = null;
            int n = 0;
            List<string> errs = new List<string>();
            await Task.Run(() =>
            {
                try
                {
                    if (restore) { engine.Save("Antes de restaurar", "auto"); errs = engine.Restore(s.S, out n); }
                    else engine.Delete(s.S);
                }
                catch (Exception ex) { err = ex.Message; }
            });
            await SafeRefresh();
            SetWorking(false, err != null ? err
                : !restore ? "Borrado: " + s.Name + "."
                : n + (n == 1 ? " ajuste restaurado." : " ajustes restaurados.") + (errs.Count > 0 ? " No se pudo con " + errs.Count + ": " + errs[0] : "") + " Reinicia el PC para completar.");
        }
    }
}
