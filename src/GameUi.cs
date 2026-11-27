using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tablero
{
    // A resolution as the dropdown shows it; the extras never reach the saved session.
    public class ResRow : Res
    {
        [System.Web.Script.Serialization.ScriptIgnore] public string Detail { get; set; }
        [System.Web.Script.Serialization.ScriptIgnore] public bool Best { get; set; }
    }

    // Picking a game, its colors and what changes when switching from one to another.
    partial class Ui
    {
        Game game = Game.Of(Prefs.Now.Game);
        readonly Dictionary<string, StretchSession> sessions = new Dictionary<string, StretchSession>();
        readonly Dictionary<string, List<Match>> played = new Dictionary<string, List<Match>>();
        readonly HashSet<string> fpsRead = new HashSet<string>();
        List<Row> every;      // tweaks of all games; rows are those of the game on screen
        bool swapping;        // the list changes by itself, not from a click
        Circuit macroCircuit;
        ColorSession colors;
        Game lastOpen;
        bool valClosed;       // Valorant seen closed: its next opening joins the stretch
        DateTime colorTrial;  // until when the color trial lasts
        BitmapSource photo;
        WriteableBitmap painted;
        byte[] photoBytes;

        List<Match> matches { get { return PlayedIn(game); } }
        List<Match> PlayedIn(Game g) { List<Match> m; return played.TryGetValue(g.Id, out m) ? m : new List<Match>(); }
        ValorantSession Val { get { return stretch as ValorantSession; } }

        // ---- Selector ----

        void StartGames()
        {
            ListBox l = F<ListBox>("Games");
            l.SelectionChanged += (s, e) => { Game g = l.SelectedItem as Game; if (!swapping && g != null && !SwitchGame(g, true)) Reselect(); };
            l.SizeChanged += (s, e) => GameMark(false);
            Toggle("SetAutoGame", () => !Prefs.Now.KeepGame, v => Prefs.Now.KeepGame = !v);
            F<Btn>("AllGames").Click += (s, e) => AllGames(!allOpen);
            foreach (string n in GamePages) { Border b = MissingCard(); ((Panel)F<ScrollViewer>(n).Content).Children.Insert(1, b); missing.Add(b); }
            foreach (Game g in Game.All) GameCard(g);
            refresh.Add(ShowGames);
            ShowGames();
            LoadPhoto();
            ShowGame();
            // CS2's FPS are counted while it runs (opened from Play or from Steam). Its ETW session outlives the app: stopped on close.
            Cs2Fps cs2Fps = new Cs2Fps(dir);
            w.Closed += (s, e) => cs2Fps.Dispose();
            // Listing processes takes a few ms each time: off the UI thread, which keeps drawing the background meanwhile.
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += async (s, e) =>
            {
                t.Stop();
                Game open = null;
                bool val = false;
                List<Match> cs2 = null;
                await Task.Run(() => { open = Game.Open; val = ValOpen(open); cs2 = cs2Fps.Tick(); });
                GameTick(open, val);
                // CS2 closed and its matches were saved.
                if (cs2 != null) { played[Game.Cs2.Id] = cs2; if (game == Game.Cs2 && F<FrameworkElement>("FpsPage").IsVisible) ShowFps(); }
                t.Start();
            };
            t.Start();
        }
        // A game not pinned leaves both slots unmarked.
        void Reselect() { swapping = true; ListBox l = F<ListBox>("Games"); l.SelectedItem = l.Items.Contains(game) ? game : null; swapping = false; }
        // The box slides from one game to the other: eases in and out, overshoots a little and comes back.
        void GameMark(bool animate)
        {
            ListBox l = F<ListBox>("Games");
            Border m = F<Border>("GameMark");
            int at = l.Items.IndexOf(game);
            m.Width = Math.Max(0, l.ActualWidth / Math.Max(1, l.Items.Count));
            // A name that does not fit its slot ("Counter-Strike") gets a smaller font, just enough.
            foreach (object g in l.Items)
            {
                FrameworkElement it = l.ItemContainerGenerator.ContainerFromItem(g) as FrameworkElement;
                TextBlock tx = it == null ? null : Find<TextBlock>(it, "SlotText");
                if (tx == null) continue;
                tx.FontSize = 16;
                tx.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double room = m.Width - 12;
                if (room > 0 && tx.DesiredSize.Width > room) tx.FontSize = Math.Floor(16 * room / tx.DesiredSize.Width * 2) / 2;
            }
            // The game on screen is not pinned: the box fades out; when it comes back it fades in at its slot.
            if (at < 0 || m.Opacity < 1)
            {
                double o = at < 0 ? 0 : 1;
                if (animate && Fx.On) Fx.To(m, UIElement.OpacityProperty, o, 200, Fx.Out); else { m.BeginAnimation(UIElement.OpacityProperty, null); m.Opacity = o; }
                if (at < 0) return;
                animate = false;
            }
            double x = m.Width * at;
            TransformGroup t = m.RenderTransform as TransformGroup;
            if (t == null || t.IsFrozen)
            {
                t = new TransformGroup();
                t.Children.Add(new ScaleTransform());
                t.Children.Add(new TranslateTransform());
                m.RenderTransformOrigin = new Point(0.5, 0.5);
                m.RenderTransform = t;
            }
            TranslateTransform move = (TranslateTransform)t.Children[1];
            if (!animate || !Fx.On) { Fx.To(move, TranslateTransform.XProperty, x, 1, Fx.Out); return; }
            double from = move.X, over = Fx.Level == 0 ? Math.Sign(x - from) * 7 : 0;
            move.BeginAnimation(TranslateTransform.XProperty, Frames(640, new[] { 0.0, 0.62, 0.82, 1 }, new[] { from, x + over, x - over * 0.35, x }));
            if (over != 0) t.Children[0].BeginAnimation(ScaleTransform.ScaleXProperty, Frames(640, new[] { 0.0, 0.3, 0.62, 0.82, 1 }, new[] { 1, 1.14, 0.94, 1.03, 1 }));
        }
        // Frames with smooth start and stop.
        static DoubleAnimationUsingKeyFrames Frames(int ms, double[] at, double[] value)
        {
            DoubleAnimationUsingKeyFrames a = new DoubleAnimationUsingKeyFrames();
            for (int i = 0; i < at.Length; i++) a.KeyFrames.Add(new EasingDoubleKeyFrame(value[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * at[i]))) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            return a;
        }
        const int SwitchMs = 400;

        // The whole app switches to the other game. false = not possible right now.
        // from: where the change starts (a slot, or a card of All games).
        internal bool SwitchGame(Game g, bool byHand, FrameworkElement from = null)
        {
            if (g == game) return true;
            string no = working ? "Hay un cambio en curso. Espera a que termine." : stretch != null && stretch.Pending ? "Cierra " + game.Name + " antes de cambiar de juego." : null;
            if (no != null) { if (byHand) Say(no, false); return false; }
            Sound.Effect("switch");
            ListBox l = F<ListBox>("Games");
            FrameworkElement b = from ?? l.ItemContainerGenerator.ContainerFromItem(g) as FrameworkElement;
            // A theme is the same for every game: only Simple changes colors with the game.
            Palette mine = Prefs.PaletteOf(dir, g), before = Applied(), after = look.Simple ? mine : before;
            Wipe(b, SwitchMs, F<FrameworkElement>("GameBox"), () =>
            {
                game = g;
                Prefs.Now.Game = g.Id;
                SavePrefs();
                Reselect();
                swapping = true;
                F<ListBox>("Palettes").SelectedItem = mine;
                swapping = false;
                Theme.Apply(w, after, !look.Simple);
                F<TextBlock>("PaletteName").Text = "Color: " + mine.Name;
                Theme.LookOf("simple").Pal = mine;
                stretch = sessions[g.Id];
                LoadPhoto();
                ShowGame();
                ShowCards();
                LoadSession();
                BuildSubs();
                Totals();
                ShowColors();
                ShowBoosts();
                Page();
            }, () =>
            {
                GameMark(true);
                Ears(640);
                Tint(F<Border>("GameMark"), "On", before, after);
                Tint(F<Border>("GameBox"), "Off", before, after);
                ListBox nav = F<ListBox>("Circuits");
                for (int i = 0; i < nav.Items.Count; i++)
                {
                    FrameworkElement item = nav.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (item == null) continue;
                    Fx.Enter(item, 0, 0);
                    TranslateTransform t = new TranslateTransform();
                    item.RenderTransform = t;
                    Fx.From(t, TranslateTransform.XProperty, -26, 0, 360, Fx.Bounce(0.6), 96 + i * 20);
                    Fx.From(item, UIElement.OpacityProperty, 0, 1, 208, Fx.Out, 96 + i * 20);
                }
                foreach (string n in new[] { "FigureBoxA", "FigureBoxB", "ColorBoxA", "ColorBoxB" }) Fx.Pop(F<FrameworkElement>(n), 0.6, 160, 496);
                Circuit c = nav.SelectedItem as Circuit;
                if (c != null && c.Rows == null && ready) EnterPage(c.Page);
            });
            return true;
        }
        // The selector is not covered: its color shifts from one palette to the other.
        static void Tint(Border b, string key, Palette before, Palette after)
        {
            if (!Fx.On || before == null) return;
            SolidColorBrush c = new SolidColorBrush(Theme.Parse(before.C[key]));
            b.Background = c;
            ColorAnimation a = new ColorAnimation(Theme.Parse(after.C[key]), TimeSpan.FromMilliseconds(400)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            a.Completed += (s, e) => { if (b.Background == c) b.SetResourceReference(Border.BackgroundProperty, key); };
            c.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }
        // The open page is reloaded with the new game's data.
        void Page()
        {
            Circuit c = F<ListBox>("Circuits").SelectedItem as Circuit;
            if (c != null) Navigate();
            else if (F<FrameworkElement>("SettingsPage").IsVisible && ready) EnterPage("SettingsPage");
        }
        // The ears slowly lower until they almost cover the eyes and rise again.
        void Ears(int ms = 800)
        {
            FrameworkElement e = F<FrameworkElement>("LogoEars");
            ScaleTransform ears = new ScaleTransform();
            e.RenderTransform = ears;
            Panel.SetZIndex(e, 0);
            if (!Fx.On) return;
            bool bounce = Fx.Level == 0;
            ears.BeginAnimation(ScaleTransform.ScaleYProperty, Frames(ms, new[] { 0.0, 0.36, 0.46, 0.78, 0.88, 1 }, new[] { 1, -0.62, -0.62, bounce ? 1.12 : 1, bounce ? 0.96 : 1, 1 }));
            // When folding they pass in front of the face.
            Int32AnimationUsingKeyFrames z = new Int32AnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            foreach (KeyValuePair<double, int> k in new Dictionary<double, int> { { 0, 0 }, { 0.2, 5 }, { 0.62, 0 } }) z.KeyFrames.Add(new DiscreteInt32KeyFrame(k.Value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * k.Key))));
            z.Duration = TimeSpan.FromMilliseconds(ms);
            e.BeginAnimation(Panel.ZIndexProperty, z);
        }
        // The new content opens in a circle from the pressed button. It starts once already drawn, so it does not jump.
        void Wipe(FrameworkElement from, int ms, FrameworkElement free, Action change, Action start)
        {
            Grid shell = F<Grid>("Shell");
            Image wipe = F<Image>("Wipe");
            double W = shell.ActualWidth, H = shell.ActualHeight;
            if (!Fx.On || W <= 0) { change(); start(); return; }
            wipe.Visibility = Visibility.Collapsed;
            RenderTargetBitmap old = new RenderTargetBitmap((int)W, (int)H, 96, 96, PixelFormats.Pbgra32);
            old.Render(shell);
            wipe.Source = old;
            Point c = from == null ? new Point(60, H - 40) : from.TranslatePoint(new Point(from.ActualWidth / 2, from.ActualHeight / 2), shell);
            EllipseGeometry hole = new EllipseGeometry(c, 0, 0);
            Geometry gone = hole;
            if (free != null) gone = new CombinedGeometry(GeometryCombineMode.Union, hole, new RectangleGeometry(new Rect(free.TranslatePoint(new Point(), shell), new Size(free.ActualWidth, free.ActualHeight)), 14, 14));
            wipe.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, W, H)), gone);
            wipe.Visibility = Visibility.Visible;
            change();
            w.Dispatcher.BeginInvoke(new Action(() =>
            {
                double r = Math.Sqrt(W * W + H * H);
                IEasingFunction ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                Fx.To(hole, EllipseGeometry.RadiusXProperty, r, ms, ease);
                Fx.To(hole, EllipseGeometry.RadiusYProperty, r, ms, ease, 0, () => { if (wipe.Source == old) { wipe.Visibility = Visibility.Collapsed; wipe.Source = null; } });
                start();
            }), DispatcherPriority.ContextIdle);
        }

        // Texts and parts that belong to a single game.
        void ShowGame()
        {
            bool val = game == Game.Valorant, fn = game == Game.Fortnite;
            Visibility fort = fn ? Visibility.Visible : Visibility.Collapsed, valo = val ? Visibility.Visible : Visibility.Collapsed;
            foreach (string n in new[] { "LimitTitle", "Limits", "SetLimitBox", "EstBox" }) F<FrameworkElement>(n).Visibility = fort;
            F<FrameworkElement>("ValBox").Visibility = valo;
            F<TextBlock>("PlayText").Text = game == Game.Cs2 ? "Abre CS2 por Steam con la resolución elegida. Al cerrarlo, la pantalla se restaura." : val ? "Abre Valorant y, si está activado, estira la pantalla al cargar un mapa. Al cerrarlo, todo se restaura." : "Abre Fortnite con la resolución elegida en Stretched Resolution. Al cerrarlo, la pantalla y la configuración se restauran.";
            F<TextBlock>("SetGameTitle").Text = game.Name;
            F<TextBlock>("PlayEstLabel").Text = fn ? "FPS estimados" : "Media de 10 partidas";
            ShowNote();
            ShowMissing();
            if (circuits != null)
            {
                foreach (Circuit only in new[] { macroCircuit, crossCircuit })
                    if (!fn) { if (F<ListBox>("Circuits").SelectedItem == only) F<ListBox>("Circuits").SelectedItem = circuits[0]; circuits.Remove(only); }
                    else if (!circuits.Contains(only)) circuits.Insert(circuits.IndexOf(upgrades), only);
            }
            F<ItemsControl>("ValChecks").ItemTemplate = F<ItemsControl>("Checks").ItemTemplate;
            if (netTiles == null) return;
            ShowNetGame();
            NetResult net = NetTest.Load(dir);
            if (net != null && !testing) ShowNet(net, null);
        }

        // Each game only sees its own tweaks and the shared ones.
        void BuildSubs()
        {
            ListBox l = F<ListBox>("Circuits");
            Circuit at = l.SelectedItem as Circuit;
            bool shown = subs != null && circuits.Contains(subs[0]);
            if (shown) { if (at != null && at.Sub) l.SelectedItem = upgrades; foreach (Circuit c in subs) circuits.Remove(c); }
            rows = every.Where(r => r.T.Game == null || r.T.Game == game.Id).ToList();
            subs = new List<Circuit> { new Circuit { Name = "Todos", Rows = rows, Icon = Ico("All"), Sub = true } };
            foreach (IGrouping<string, Row> g in rows.GroupBy(r => r.T.Cat))
                subs.Add(new Circuit { Name = g.Key, Rows = g.ToList(), Icon = Ico(Icons.ContainsKey(g.Key) ? Icons[g.Key] : "All"), Sub = true });
            if (hw != null) subs.Insert(0, new Circuit { Name = Recommended, Icon = Ico("Check"), Sub = true, Rows = Wanted() });
            if (shown) foreach (Circuit c in subs) circuits.Add(c);
            if (shown && at != null && at.Sub) l.SelectedItem = subs.FirstOrDefault(c => c.Name == at.Name) ?? subs.FirstOrDefault(c => c.Name == game.Name) ?? subs[0];
        }
        static readonly Dictionary<string, string> Icons = new Dictionary<string, string> {
            { "Fortnite", "Game" }, { "Valorant", "Game" }, { "Counter-Strike 2", "Game" }, { "Gráficos y captura", "Gpu" }, { "Energía y CPU", "Bolt" }, { "Retraso", "Gauge" }, { "Ratón y teclado", "Mouse" }, { "Red", "Net" },
            { "Segundo plano", "Layers" }, { "Servicios", "Server" }, { "Tareas programadas", "Clock" }, { "Interrupciones", "Bell" },
            { "Publicidad", "Tag" }, { "Búsqueda", "Search" }, { "Telemetría", "Eye" }, { "Interfaz", "Window" } };

        void LoadPhoto()
        {
            BitmapImage b = new BitmapImage();
            b.BeginInit();
            // A game whose photo resource is missing falls back to Fortnite's.
            b.StreamSource = Assembly.GetExecutingAssembly().GetManifestResourceStream(game.Photo) ?? Assembly.GetExecutingAssembly().GetManifestResourceStream(Game.Fortnite.Photo);
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.EndInit();
            photo = new FormatConvertedBitmap(b, PixelFormats.Bgr32, null, 0);
            photoBytes = new byte[photo.PixelWidth * photo.PixelHeight * 4];
            photo.CopyPixels(photoBytes, photo.PixelWidth * 4, 0);
            painted = new WriteableBitmap(photo.PixelWidth, photo.PixelHeight, 96, 96, PixelFormats.Bgr32, null);
            foreach (string n in new[] { "FigureNormal", "FigureWide", "ColorNow" }) ((ImageBrush)w.FindName(n)).ImageSource = photo;
            ((ImageBrush)w.FindName("ColorNew")).ImageSource = painted;
        }

        // ---- When a game opens ----

        static bool ValOpen(Game open) { return open == Game.Valorant || open != null && Game.Valorant.Running(); }
        void GameTick() { Game open = Game.Open; GameTick(open, ValOpen(open)); }
        void GameTick(Game open, bool val)
        {
            if (open != null && open != lastOpen && !Prefs.Now.KeepGame && ready && Game.Visible.Contains(open)) SwitchGame(open, false);
            lastOpen = open;
            SceneNow();
            SteadyNow();
            // Only an opening seen by the app joins: a game already open when the app starts may be mid-match.
            if (!val) valClosed = true;
            else if (valClosed && ready) valClosed = !Join();
            MusicNow(open);
            BoostTick(open);
            if (colors == null) return;
            try
            {
                if (DateTime.Now < colorTrial) colors.Keep();
                else
                {
                    ColorPick want = open == null ? null : Prefs.Now.ColorOf(open);
                    if (want == null || want.Profile == Vivid.None) colors.Restore();
                    else if (colors.Now != want) colors.Apply(want);
                    else colors.Keep();
                }
            }
            catch (Exception ex) { F<TextBlock>("ColorState").Text = "No se pudo cambiar el color: " + ex.Message; return; }
            if (F<FrameworkElement>("ColorPage").IsVisible) ColorState();
        }

        // ---- Game not installed ----

        static readonly string[] GamePages = { "StretchPage", "ResPage", "ColorPage", "BoostPage", "FpsPage" };
        readonly List<Border> missing = new List<Border>();
        readonly Dictionary<Game, CheckBox> pickerChecks = new Dictionary<Game, CheckBox>();
        readonly Dictionary<Game, TextBlock> exeTexts = new Dictionary<Game, TextBlock>();

        // The same card at the top of every game page; it always names the game on screen.
        Border MissingCard()
        {
            Grid dot = new Grid { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            Border ring = new Border { CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2.5) };
            ring.SetResourceReference(Border.BorderBrushProperty, "Ink");
            dot.Children.Add(ring);
            DockPanel.SetDock(dot, Dock.Left);
            Tb title = new Tb { FontSize = 16.5, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            Tb text = new Tb { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
            // The theme stores a color under the Btn key: the style is copied from a window button.
            Btn find = new Btn { Style = F<Btn>("CheckNow").Style, Content = "Buscar el archivo", Icon = Ico("Search"), Height = 38, Margin = new Thickness(0, 10, 10, 0) };
            Btn drop = new Btn { Style = F<Btn>("CheckNow").Style, Content = "Quitar del selector", Height = 38, Margin = new Thickness(0, 10, 0, 0) };
            find.Click += (s, e) => FindExe(game);
            drop.Click += (s, e) => ShowInPicker(game, false);
            WrapPanel buttons = new WrapPanel();
            buttons.Children.Add(find);
            buttons.Children.Add(drop);
            StackPanel body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(title);
            body.Children.Add(text);
            body.Children.Add(buttons);
            DockPanel row = new DockPanel();
            row.Children.Add(dot);
            row.Children.Add(body);
            Border b = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 6, 0, 16), Child = row, Tag = new[] { title, text } };
            b.SetResourceReference(Border.BackgroundProperty, "Card");
            return b;
        }
        void ShowMissing()
        {
            bool no = !game.Installed();
            foreach (Border b in missing)
            {
                b.Visibility = no ? Visibility.Visible : Visibility.Collapsed;
                Tb[] t = (Tb[])b.Tag;
                t[0].Text = game.Name + " no está instalado o no se encontró";
                t[1].Text = "Si está en otra carpeta, elige su archivo " + game.ExeName + ".";
            }
            if (stretch != null) ShowStretch();
        }

        // Settings card: where the game is and whether it shows in the selector.
        void GameCard(Game g)
        {
            Btn change = new Btn { Style = F<Btn>("CheckNow").Style, Content = "Cambiar carpeta", Height = 38, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            change.Click += (s, e) => FindExe(g);
            DockPanel.SetDock(change, Dock.Right);
            TextBlock path = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            path.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
            CheckBox c = new CheckBox { Style = F<CheckBox>("SetAutoGame").Style, Content = L.T("Mostrar en el selector") };
            AutomationProperties.SetName(c, g.Name + ": " + L.T("Mostrar en el selector"));
            c.Click += (s, e) => { ShowInPicker(g, c.IsChecked == true); c.IsChecked = !Prefs.Now.Hidden.Contains(g.Id); };
            w.RegisterName("SetShow" + System.Text.RegularExpressions.Regex.Replace(g.Name, "[^A-Za-z0-9]", ""), c); // "Counter-Strike 2" is not a valid name
            StackPanel body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(new Tb { Text = g.Name, FontSize = 16, FontWeight = FontWeights.Bold });
            body.Children.Add(path);
            body.Children.Add(c);
            DockPanel row = new DockPanel();
            row.Children.Add(change);
            row.Children.Add(body);
            Border b = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 0, 0, 8), Child = row };
            b.SetResourceReference(Border.BackgroundProperty, "Card");
            F<Panel>("SetGames").Children.Add(b);
            pickerChecks[g] = c;
            exeTexts[g] = path;
        }

        // The selector, the Settings cards and the warning, kept in sync with the preferences.
        void ShowGames() { ShowGames(false); }
        void ShowGames(bool animate)
        {
            ListBox l = F<ListBox>("Games");
            // A hidden pinned game gives its slot to the next visible one.
            List<Game> slots = Game.Slots(Prefs.Now.Pinned, Game.Visible);
            Prefs.Now.Pinned = slots.Select(g => g.Id).ToList();
            swapping = true;
            l.ItemsSource = slots;
            l.SelectedItem = slots.Contains(game) ? game : null;
            swapping = false;
            l.UpdateLayout();
            GameMark(animate);
            foreach (Game g in Game.All)
            {
                pickerChecks[g].IsChecked = !Prefs.Now.Hidden.Contains(g.Id);
                exeTexts[g].Text = g.Installed() ? g.Exe : L.T("No encontrado");
            }
            ShowCards();
            ShowMissing();
        }

        // ---- All games, in Play ----

        bool allOpen;
        readonly List<Game> cardGames = new List<Game>();
        readonly Dictionary<Game, FrameworkElement[]> cardParts = new Dictionary<Game, FrameworkElement[]>(); // card, Play, Pin, "not installed"
        readonly Dictionary<Game, ImageSource> cardPhotos = new Dictionary<Game, ImageSource>();

        // Opens inside the page, pushing the rest down; without animations it opens at once.
        void AllGames(bool open)
        {
            if (allOpen == open) return;
            allOpen = open;
            Border box = F<Border>("AllGamesBox");
            FrameworkElement chev = F<FrameworkElement>("AllGamesChev");
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
            // From the current height, also when it changes direction halfway.
            box.Height = box.Visibility == Visibility.Visible ? box.ActualHeight : 0;
            box.Visibility = Visibility.Visible;
            box.UpdateLayout(); // the photos take their 16:9 height once they know their width
            FrameworkElement grid = (FrameworkElement)box.Child;
            grid.Measure(new Size(((FrameworkElement)box.Parent).ActualWidth, double.PositiveInfinity));
            Fx.To(box, FrameworkElement.HeightProperty, open ? grid.DesiredSize.Height : 0, open ? 420 : 220, open ? Fx.Out : Fx.In, 0, () =>
            {
                if (allOpen != open) return;
                box.BeginAnimation(FrameworkElement.HeightProperty, null);
                box.Height = double.NaN;
                if (!open) box.Visibility = Visibility.Collapsed;
            });
        }

        // One card per visible game; built again only when that list changes.
        void ShowCards()
        {
            List<Game> shown = Game.Visible;
            if (!shown.SequenceEqual(cardGames))
            {
                UniformGrid grid = F<UniformGrid>("AllGamesGrid");
                grid.Children.Clear();
                cardGames.Clear();
                cardParts.Clear();
                foreach (Game g in shown) { grid.Children.Add(GameTile(g)); cardGames.Add(g); }
            }
            CardState();
        }
        Border GameTile(Game g)
        {
            // Photo at 16:9 with the top corners rounded; a game without its photo yet gets the game icon.
            Border pic = new Border { CornerRadius = new CornerRadius(16.5, 16.5, 0, 0) };
            pic.SizeChanged += (s, e) => { if (e.WidthChanged) pic.Height = e.NewSize.Width * 9 / 16; };
            ImageSource src = CardPhoto(g);
            if (src != null) pic.Background = new ImageBrush(src) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top }; // faces, not legs
            else
            {
                pic.SetResourceReference(Border.BackgroundProperty, "Btn");
                System.Windows.Shapes.Path icon = new System.Windows.Shapes.Path { Data = Ico("Game"), Width = 24, Height = 24, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
                icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Ink2");
                pic.Child = new Viewbox { Width = 40, Height = 40, Child = icon };
            }
            Tb name = new Tb { Text = g.Name, FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            Tb no = new Tb { Text = "No instalado", Margin = new Thickness(0, 2, 0, 0) };
            no.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
            // Two equal columns, 8 px apart: always on one row.
            Btn play = new Btn { Style = F<Btn>("Play").Style, Content = "Jugar", Glint = false, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 4, 0) };
            Btn pin = new Btn { Style = F<Btn>("CheckNow").Style, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(4, 0, 0, 0) };
            AutomationProperties.SetName(play, L.T("Jugar") + " " + g.Name);
            // Exactly what the main Play does, once this game is on screen.
            play.Click += (s, e) => { if (SwitchGame(g, true, play)) Play(); };
            pin.Click += (s, e) => Pin(g);
            Grid buttons = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(pin, 1);
            buttons.Children.Add(play);
            buttons.Children.Add(pin);
            // The buttons sit at the bottom, so a card with one line more ("No instalado") leaves no gap in the others.
            DockPanel text = new DockPanel { Margin = new Thickness(16, 12, 16, 16), LastChildFill = false };
            DockPanel.SetDock(buttons, Dock.Bottom);
            DockPanel.SetDock(name, Dock.Top);
            DockPanel.SetDock(no, Dock.Top);
            text.Children.Add(buttons);
            text.Children.Add(name);
            text.Children.Add(no);
            DockPanel body = new DockPanel();
            DockPanel.SetDock(pic, Dock.Top);
            body.Children.Add(pic);
            body.Children.Add(text);
            Border card = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 12, 12), Child = body };
            card.SetResourceReference(Border.BackgroundProperty, "Card");
            cardParts[g] = new FrameworkElement[] { card, play, pin, no };
            return card;
        }
        ImageSource CardPhoto(Game g)
        {
            ImageSource p;
            if (cardPhotos.TryGetValue(g, out p)) return p;
            System.IO.Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(g.Card ?? g.Photo) ?? Assembly.GetExecutingAssembly().GetManifestResourceStream(g.Photo);
            if (s != null)
            {
                BitmapImage b = new BitmapImage();
                b.BeginInit();
                b.StreamSource = s;
                b.DecodePixelWidth = 480;
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.EndInit();
                b.Freeze();
                p = b;
            }
            return cardPhotos[g] = p;
        }
        // Current game outlined, pinned ones say so, and Play off like the main one while a game is running.
        void CardState()
        {
            bool busy = stretch == null || stretch.Pending || confirming;
            foreach (Game g in cardGames)
            {
                FrameworkElement[] p = cardParts[g];
                Border card = (Border)p[0];
                if (g == game) card.SetResourceReference(Border.BorderBrushProperty, "On"); else card.BorderBrush = Brushes.Transparent;
                bool inst = g.Installed(), pinned = Prefs.Now.Pinned.Contains(g.Id);
                p[1].IsEnabled = inst && !busy;
                p[3].Visibility = inst ? Visibility.Collapsed : Visibility.Visible;
                Btn pin = (Btn)p[2];
                pin.Content = pinned ? "Fijado" : "Fijar";
                pin.IsEnabled = !pinned;
                pin.ToolTip = L.T(pinned ? "Fijado" : "Fijar arriba") + " " + g.Name;
                AutomationProperties.SetName(pin, (string)pin.ToolTip);
            }
        }
        // Takes the slot without the game on screen; if that game is not pinned, the right one.
        void Pin(Game g)
        {
            List<Game> slots = Game.Slots(Prefs.Now.Pinned, Game.Visible);
            if (slots.Contains(g)) return;
            List<string> p = slots.Select(x => x.Id).ToList();
            p[slots.IndexOf(game) == 1 ? 0 : p.Count - 1] = g.Id;
            Prefs.Now.Pinned = p;
            SavePrefs();
            ShowGames(true);
        }

        // false = it could not be done: the game stays in the selector.
        bool ShowInPicker(Game g, bool on)
        {
            List<string> hidden = Prefs.Now.Hidden;
            if (on) hidden.Remove(g.Id);
            else
            {
                List<Game> rest = Game.Visible.Where(x => x != g).ToList();
                if (rest.Count == 0) { Say("Tiene que quedar al menos un juego.", false); return false; }
                if (g == game && !SwitchGame(rest[0], true)) return false;
                if (!hidden.Contains(g.Id)) hidden.Add(g.Id);
            }
            SavePrefs();
            ShowGames();
            return true;
        }

        void FindExe(Game g)
        {
            Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog { Filter = g.Name + "|" + g.ExeName, FileName = g.ExeName };
            if (d.ShowDialog(w) != true) return;
            if (!g.IsExe(d.FileName)) { Say("Ese no es el archivo de " + g.Name + ". Se llama " + g.ExeName + ".", false); return; }
            Prefs.Now.Paths[g.Id] = d.FileName;
            SavePrefs();
            ShowGames();
            Say(g.Name + " encontrado. Los tweaks del juego se actualizan al volver a abrir Tweakeroo.", false);
        }

        // ---- Colors ----

        static readonly string[] Sliders = { "ColVib", "ColBright", "ColContrast", "ColGamma" };
        void StartColors()
        {
            colors = new ColorSession(dir);
            // The app closed with a game's color applied.
            try { if (Game.Open == null) colors.Restore(); } catch (Exception) { }
            ListBox l = F<ListBox>("ColorProfiles");
            l.ItemsSource = Vivid.All;
            l.SelectionChanged += (s, e) =>
            {
                ColorProfile p = l.SelectedItem as ColorProfile;
                if (swapping || p == null) return;
                ColorPick old = Prefs.Now.ColorOf(game), pick = p.Pick.Copy();
                // Custom starts from what was there.
                if (p.Id == Vivid.Mine && old.Profile != Vivid.None) { pick = old.Copy(); pick.Profile = Vivid.Mine; }
                SetColor(pick);
            };
            foreach (string n in Sliders)
                F<Slider>(n).ValueChanged += (s, e) =>
                {
                    if (swapping) return;
                    SetColor(Vivid.Clean(new ColorPick { Profile = Vivid.Mine, Vib = (int)F<Slider>("ColVib").Value, Bright = (int)F<Slider>("ColBright").Value, Contrast = (int)F<Slider>("ColContrast").Value, Gamma = F<Slider>("ColGamma").Value }));
                };
            F<Btn>("ColorTry").Click += (s, e) => TryColor();
            w.Closed += (s, e) => { try { colors.Restore(); } catch (Exception) { } };
            ShowColors();
        }
        void SetColor(ColorPick p)
        {
            Prefs.Now.Colors[game.Id] = p;
            SavePrefs();
            ShowColors();
            if (DateTime.Now < colorTrial) Attempt(() => colors.Apply(p));
        }
        void ShowColors()
        {
            if (colors == null) return;
            ColorPick p = Prefs.Now.ColorOf(game);
            int? vib = null;
            bool ramp = false;
            try { vib = Vivid.GetVib(); ramp = Vivid.GetRamp() != null; } catch (Exception) { }
            swapping = true;
            F<ListBox>("ColorProfiles").SelectedItem = Vivid.All.First(x => x.Id == p.Profile);
            F<Slider>("ColVib").Value = p.Vib; F<Slider>("ColBright").Value = p.Bright; F<Slider>("ColContrast").Value = p.Contrast; F<Slider>("ColGamma").Value = p.Gamma;
            swapping = false;
            F<Slider>("ColVib").IsEnabled = vib != null;
            foreach (string n in Sliders.Skip(1)) F<Slider>(n).IsEnabled = ramp;
            F<TextBlock>("ColVibText").Text = p.Vib + " %";
            F<TextBlock>("ColBrightText").Text = p.Bright + " %";
            F<TextBlock>("ColContrastText").Text = p.Contrast + " %";
            F<TextBlock>("ColGammaText").Text = p.Gamma.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            F<TextBlock>("ColorHint").Text = (vib == null ? "La intensidad necesita una gráfica NVIDIA. " : "") + (ramp ? "" : "Windows no permite cambiar brillo, contraste y gamma en esta pantalla. Pasa con HDR o con el monitor desactivado en el Administrador de dispositivos.");
            F<TextBlock>("ColorHint").Visibility = vib != null && ramp ? Visibility.Collapsed : Visibility.Visible;
            F<Btn>("ColorTry").IsEnabled = p.Profile != Vivid.None && (vib != null || ramp);
            // The preview shows only what this display can do.
            bool none = p.Profile == Vivid.None;
            ColorPick shown = p.Copy();
            if (none || !ramp) { shown.Bright = shown.Contrast = 50; shown.Gamma = 1; }
            byte[] o = new byte[photoBytes.Length];
            Vivid.Paint(photoBytes, o, shown, none || vib == null ? shown.Vib : vib.Value);
            painted.WritePixels(new Int32Rect(0, 0, painted.PixelWidth, painted.PixelHeight), o, painted.PixelWidth * 4, 0);
            F<TextBlock>("ColorNowText").Text = vib == null ? "Ahora" : "Ahora, intensidad " + vib + " %";
            ColorState();
        }
        void ColorState()
        {
            ColorPick p = Prefs.Now.ColorOf(game);
            int left = (int)Math.Ceiling((colorTrial - DateTime.Now).TotalSeconds);
            F<TextBlock>("ColorState").Text = left > 0 ? "Probando. Vuelve solo en unos segundos." : colors.Now != null ? "Activo ahora." : p.Profile == Vivid.None ? "Sin perfil: la pantalla no cambia." : "Se aplica al abrir " + game.Name + ".";
        }
        async void TryColor()
        {
            Btn b = F<Btn>("ColorTry");
            if (DateTime.Now < colorTrial) return;
            colorTrial = DateTime.Now.AddSeconds(10);
            string err = null;
            try { if (!colors.Apply(Prefs.Now.ColorOf(game)) && F<Slider>("ColBright").IsEnabled) err = "Windows no aceptó ese brillo. Prueba valores más cercanos a 50."; }
            catch (Exception ex) { err = "No se pudo cambiar el color: " + ex.Message; colorTrial = DateTime.MinValue; }
            if (err != null) Say(err, false);
            ColorState();
            // Counts down in Cta until the colors go back.
            b.SetResourceReference(Control.ForegroundProperty, "Cta"); b.SetResourceReference(Control.BorderBrushProperty, "Cta");
            while (DateTime.Now < colorTrial) { b.Content = "Volviendo en " + Math.Max(0, (int)Math.Ceiling((colorTrial - DateTime.Now).TotalSeconds)) + " s"; await Task.Delay(200); }
            b.Content = "Probar 10 segundos"; b.ClearValue(Control.ForegroundProperty); b.ClearValue(Control.BorderBrushProperty);
            GameTick();
            ShowColors();
        }

        // ---- Play ----

        List<Res> resolutions;
        readonly List<Res> extra = new List<Res>(); // created in this session
        Game resume;   // its session is still open from before
        List<LimitOption> limits;
        void StartStretch()
        {
            watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            watch.Tick += (s, e) => Watch();
            ListBox l = F<ListBox>("Resolutions"), lim = F<ListBox>("Limits");
            Btn play = F<Btn>("Play");
            try
            {
                foreach (Game g in Game.All) sessions[g.Id] = g.Session(dir);
                stretch = sessions[game.Id];
                Res native = Display.Saved();
                native.Note = "Normal, " + native.Hz + " Hz";
                resolutions = Display.Stretched(Display.Modes(), native);
                resolutions.Insert(0, native);
                limits = new List<LimitOption> { new LimitOption { Name = "El de Fortnite", Note = "Sin cambios", Value = "" }, new LimitOption { Name = native.Hz + " FPS", Note = "Igual que la pantalla", Value = native.Hz.ToString() }, new LimitOption { Name = "Sin límite", Note = "Menos retraso, más calor", Value = "0" } };
            }
            catch (Exception ex) { stretch = null; sessions.Clear(); foreach (Game g in Game.All) sessions[g.Id] = null; play.IsEnabled = false; F<TextBlock>("StretchInfo").Text = "No se pudo preparar el stretched: " + ex.Message; return; }
            l.SelectionChanged += (s, e) => { if (!swapping) Preview(); };
            // Play shows the same list in its own dropdown, always in sync.
            ListBox pl = F<ListBox>("ResOpenList");
            pl.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("ItemsSource") { Source = l });
            pl.SetBinding(Selector.SelectedItemProperty, new System.Windows.Data.Binding("SelectedItem") { Source = l, Mode = System.Windows.Data.BindingMode.TwoWay });
            foreach (Drop d in new[] { F<Drop>("ResDrop"), F<Drop>("ResOpen") })
            {
                ListBox x = (ListBox)d.Panel;
                // A click or Enter on a row closes; arrows change it without closing.
                x.PreviewMouseLeftButtonUp += (s, e) => { if (ItemsControl.ContainerFromElement(x, e.OriginalSource as DependencyObject) != null) d.Close(); };
                x.PreviewKeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Space) { d.Close(); e.Handled = true; } };
            }
            F<Btn>("CustomGo").Click += (s, e) => UseCustom();
            foreach (string n in new[] { "CustomW", "CustomH" }) { F<TextBox>(n).KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) UseCustom(); }; F<TextBox>(n).TextChanged += (s, e) => CustomOk(); }
            bool can = Custom.Available();
            F<WrapPanel>("CustomRow").Visibility = can ? Visibility.Visible : Visibility.Collapsed;
            F<TextBlock>("CustomHint").Text = customHint = can ?"Admite casi cualquier resolución menor que la de la pantalla. Se crea en modo de prueba y solo se guarda al confirmarla." : "Esta gráfica no permite crear resoluciones desde la app. Créala en el panel del fabricante y aparecerá en la lista.";
            lim.ItemsSource = limits;
            lim.SelectedItem = limits.FirstOrDefault(x => x.Value == sessions[Game.Fortnite.Id].Limit) ?? limits[0];
            lim.SelectionChanged += (s, e) => Try(() => sessions[Game.Fortnite.Id].Limit = ((LimitOption)lim.SelectedItem).Value);
            CheckBox min = F<CheckBox>("Minimize");
            min.IsChecked = stretch.Minimize;
            min.Click += (s, e) => Try(() => { foreach (StretchSession x in sessions.Values) x.Minimize = min.IsChecked == true; });
            CheckBox val = F<CheckBox>("ValOn");
            val.Click += (s, e) => { ValorantSession v = (ValorantSession)sessions[Game.Valorant.Id]; Try(() => v.Stretch = val.IsChecked == true); LoadSession(); };
            play.Click += (s, e) => Play();
            F<Btn>("Back").Click += (s, e) => StopStretch("Resolución restaurada.");
            // The app closed halfway last time.
            foreach (Game g in Game.All)
            {
                StretchSession x = sessions[g.Id];
                if (!x.Pending) continue;
                if (g.Running()) resume = g;
                else try { x.End(); } catch (Exception ex) { Say(ex.Message, true); }
            }
            LoadSession();
        }

        // Valorant opened from Riot Client or elsewhere: its stretch starts as if Play had been pressed.
        // false = a change is in progress: retried on the next tick.
        bool Join()
        {
            StretchSession s;
            ValorantSession v = sessions.TryGetValue(Game.Valorant.Id, out s) ? s as ValorantSession : null;
            Res r = v == null ? null : v.Last;
            // Off, already open from Play, a resolution never confirmed, under 1.45:1 or the normal one: nothing to do.
            try { if (r == null || !v.Stretch || v.Pending || v.Asks(r) || r.Aspect < Display.ValorantRatio - 0.0001 || r.Same(Display.Saved()) || !Game.Visible.Contains(Game.Valorant)) return true; }
            catch (InvalidOperationException) { return true; }
            if (working) return false;
            // Another game's session is open: not retried, the match may have started by then.
            if (!SwitchGame(Game.Valorant, false)) return true;
            string err = null;
            try { v.Begin(r); }
            catch (Exception ex) { err = ex.Message; }
            // With the game open a failed start stays pending: watching it restores everything on close.
            if (v.Pending) { gameSeen = true; watch.Start(); }
            ShowStretch();
            Say(err ?? (v.Warning != null ? "Algún monitor no se pudo desactivar: " + v.Warning : "Valorant abierto: la pantalla se estira al cargar un mapa y vuelve al cerrarlo."), err != null);
            return true;
        }

        // The resolution and options of the game on screen.
        void LoadSession()
        {
            if (stretch == null) return;
            ListBox l = F<ListBox>("Resolutions");
            swapping = true;
            bool val = game == Game.Valorant;
            List<ResRow> list = ResRows();
            Res last = stretch.Last;
            l.ItemsSource = null;
            l.ItemsSource = list;
            // A saved one no longer listed (in Valorant, under 1.45:1) goes back to the recommended one; Preview saves it.
            l.SelectedItem = list.FirstOrDefault(r => r.Same(last)) ?? (last != null ? list.FirstOrDefault(r => r.Best) : null) ?? list[0];
            swapping = false;
            ValorantSession v = Val;
            F<FrameworkElement>("ResBox").Visibility = v == null || v.Stretch ? Visibility.Visible : Visibility.Collapsed;
            F<FrameworkElement>("ValChecks").Visibility = v != null && v.Stretch || game == Game.Cs2 ? Visibility.Visible : Visibility.Collapsed;
            F<CheckBox>("ValOn").IsChecked = v != null && v.Stretch;
            Preview();
            F<TextBlock>("ResHint").Text = game == Game.Cs2 ? "La pantalla se estira al pulsar Jugar y vuelve al cerrar CS2. Jugar también pone esa resolución y su relación de aspecto en el vídeo de CS2, y las repone al cerrarlo." : val ? "Valorant solo estira con una relación de aspecto de 1,45:1 o más: las más cuadradas, como 4:3, no funcionan." : "La pantalla se estira al pulsar Jugar y vuelve al cerrar el juego. No hace falta crear nada en el panel de la gráfica: se usan los Hz más altos de cada resolución.";
            if (list.Count == 1 && val) F<TextBlock>("StretchInfo").Text = "La pantalla no ofrece resoluciones estiradas que cumplan 1,45:1. Para estirar Valorant, crea " + Display.ValorantBest(list[0]).Name + " en el panel de la gráfica y reinicia la app.";
            else if (list.Count == 1 && game == Game.Cs2) F<TextBlock>("StretchInfo").Text = "La pantalla no ofrece ninguna resolución más estrecha. Para estirar CS2, crea 1280 × 960 en el panel de la gráfica y reinicia la app.";
            else if (list.Count == 1) F<TextBlock>("StretchInfo").Text = "La pantalla no ofrece ninguna resolución más estrecha. Para usar stretched, crea 1720 × 1080 en el panel de la gráfica y reinicia la app.";
            ShowStretch();
            if (v != null && v.Stretch || game == Game.Cs2) { Task t = ShowChecks(); }
        }
        // Native first, then from least to most stretched; in Valorant only those from 1.45:1.
        List<ResRow> ResRows()
        {
            bool val = game == Game.Valorant;
            double least = val ? Display.ValorantRatio - 0.0001 : 0;
            Res native = resolutions[0], last = stretch.Last, best = Display.ValorantBest(native);
            List<Res> list = resolutions.Skip(1).Where(r => r.Aspect >= least).ToList();
            // Nearly equal: the most stretched one that really works is enough.
            if (val && list.Any(r => r.Same(best))) list.RemoveAll(r => r.Aspect >= least && r.H == native.H && r.W < best.W);
            bool create = Custom.Available();
            // The ones created in this session and the one picked last time.
            foreach (Res r in extra.Concat(last == null || !create || last.W >= native.W ? new Res[0] : new[] { last }))
                if (r.Aspect >= least && !list.Any(x => x.Same(r))) list.Add(r);
            if (val && create && !list.Any(r => r.Aspect >= least && r.Aspect < 1.47)) list.Add(best);
            Func<Res, string> ratio = r => r.Aspect.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace(".", L.En ? "." : ",") + ":1";
            return new[] { new ResRow { W = native.W, H = native.H, Hz = native.Hz, Note = "Normal", Detail = ratio(native) + " · " + native.Hz + " Hz" } }
                .Concat(list.OrderByDescending(r => r.Aspect).ThenByDescending(r => r.W * r.H)
                .Select(r => new ResRow { W = r.W, H = r.H, Hz = r.Hz, Note = ratio(r), Detail = ratio(r) + " · " + r.Hz + " Hz", Best = val && r.Aspect < 1.47 })).ToList();
        }
        // "Antes de jugar" of Valorant or CS2.
        async Task ShowChecks()
        {
            List<Check> c = null;
            Hw h = hw ?? quick;
            bool cs2 = game == Game.Cs2;
            await Task.Run(() =>
            {
                int on = 0;
                if (!cs2) try { on = Monitors.On().Count; } catch (InvalidOperationException) { }
                string gpu = Valorant.ScaleBrand(h);
                c = cs2 ? Cs2.Checks(Cs2.Video(), Cs2.LaunchOptions(), gpu, dir) : Valorant.Checks(Valorant.Settings(), on, gpu, dir);
            });
            if ((game == Game.Cs2) != cs2) return; // the game changed meanwhile
            F<ItemsControl>("ValChecks").ItemsSource = c;
        }
    }
}
