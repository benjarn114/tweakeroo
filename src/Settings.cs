using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Tablero
{
    // Settings, version history and tray.
    partial class Ui
    {
        const string Repo = "https://github.com/benjarn114/tweakeroo";
        public Func<Task<string>> CheckUpdates;
        public static bool TrayIcon = true; // tests do not add an icon by the clock
        System.Windows.Forms.NotifyIcon tray;
        bool quit;

        void StartSettings()
        {
            F<Btn>("OpenSettings").Click += (s, e) => OpenSettings();

            CheckBox boot = F<CheckBox>("SetBoot");
            boot.IsEnabled = Installer.RunningInstalled;
            boot.IsChecked = Autostart.Read();
            boot.Click += (s, e) =>
            {
                try { Autostart.Write(boot.IsChecked == true); }
                catch (Exception ex) { Say("No se pudo cambiar: " + ex.Message, false); }
                boot.IsChecked = Autostart.Read();
            };
            Toggle("SetMin", () => Prefs.Now.StartMin, v => Prefs.Now.StartMin = v);
            Toggle("SetTray", () => Prefs.Now.Tray, v => { Prefs.Now.Tray = v; ShowTray(); });
            Toggle("SetAuto", () => Prefs.Now.AutoCheck, v => Prefs.Now.AutoCheck = v);
            StartHeavy();
            if (Laptop.Has())
            {
                F<FrameworkElement>("SetLaptopBox").Visibility = Visibility.Visible;
                // Turning it off forgets the saved plan; turning it on applies the mode at once.
                Toggle("SetLaptop", () => Prefs.Now.LaptopMode, v => { Prefs.Now.LaptopMode = v; if (!v) Prefs.Now.LaptopPlan = null; else if (!Laptop.Force) Laptop.Apply(dir); });
                Laptop.Start(dir, w.Dispatcher, m => { if (Game.Open != null) Say(m, false); });
                w.Closed += (s, e) => Laptop.Stop();
            }
            Choice("SetLang", new[] { "English", "Español" }, new[] { "en", "es" }, () => L.En ? "en" : "es", v => ChangeLanguage(v == "en"));
            Tri motion = F<Tri>("SetMotion");
            motion.Labels(new[] { "Todas", "Sin rebote", "Ninguna" });
            refresh.Add(() => motion.Set(Prefs.Now.Motion));
            motion.Set(Prefs.Now.Motion);
            motion.Changed += v => { Prefs.Now.Motion = Fx.Level = v; SavePrefs(); SceneNow(); };

            Drop confirm = F<Drop>("SetConfirm");
            Wheel secs = F<Wheel>("SetConfirmWheel");
            Action showSecs = () => { secs.Set(Prefs.Now.Confirm); confirm.Content = Prefs.Now.Confirm + " s"; };
            refresh.Add(showSecs);
            showSecs();
            secs.Changed += v => { Prefs.Now.Confirm = v; confirm.Content = v + " s"; ShowNote(); SavePrefs(); };
            F<Btn>("SetConfirmReset").Click += (s, e) => secs.Spin(15);
            ShowNote();

            Drop music = F<Drop>("SetMusicDrop");
            ListBox tracks = F<ListBox>("SetMusic");
            Choice(tracks, new List<LimitOption> {
                new IconOption { Name = "Noche", Note = "lo-fi tranquilo", Value = Sound.Themes[0], Icon = Ico("Night") },
                new IconOption { Name = "Arcade", Note = "8 bits", Value = Sound.Themes[1], Icon = Ico("Arcade") },
                new IconOption { Name = "Lluvia", Note = "pad y lluvia", Value = Sound.Themes[2], Icon = Ico("Rain") },
                new IconOption { Name = "Apagada", Value = "", Icon = Ico("Mute"), Muted = true } },
                () => Prefs.Now.Music, v => { Prefs.Now.Music = v; MusicNow(); });
            music.Content = tracks.SelectedItem;
            tracks.SelectionChanged += (s, e) => music.Content = tracks.SelectedItem;
            showMusic = () => tracks.SelectedItem = tracks.Items.Cast<LimitOption>().FirstOrDefault(x => x.Value == Prefs.Now.Music);
            // A click or Enter on a row closes; arrows change the music without closing.
            tracks.PreviewMouseLeftButtonUp += (s, e) => { if (ItemsControl.ContainerFromElement(tracks, e.OriginalSource as DependencyObject) != null) music.Close(); };
            tracks.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { music.Close(); e.Handled = true; } };
            Choice("SetMusicMode", new[] { "Solo con la app delante", "Siempre menos al jugar", "Silenciada" }, new[] { "0", "1", "2" }, () => Prefs.Now.MusicMode.ToString(), v => { Prefs.Now.MusicMode = int.Parse(v); MusicNow(); });
            Volume("SetMusicVol", () => Prefs.Now.MusicVolume, v => { Prefs.Now.MusicVolume = v; MusicNow(); });
            Toggle("SetSfx", () => Prefs.Now.Sfx, v => Prefs.Now.Sfx = v);
            Volume("SetSfxVol", () => Prefs.Now.SfxVolume, v => Prefs.Now.SfxVolume = v);
            w.Activated += (s, e) => MusicNow();
            w.Deactivated += (s, e) => MusicNow();
            w.StateChanged += (s, e) => MusicNow();
            w.IsVisibleChanged += (s, e) => MusicNow();
            MusicNow();

            // The same options as in Play, always in sync in both places.
            if (stretch != null)
            {
                CheckBox min = F<CheckBox>("Minimize"), min2 = F<CheckBox>("SetMinimize");
                min2.IsChecked = stretch.Minimize;
                min2.Click += (s, e) => { Try(() => { foreach (StretchSession x in sessions.Values) x.Minimize = min2.IsChecked == true; }); min.IsChecked = min2.IsChecked; };
                min.Click += (s, e) => min2.IsChecked = min.IsChecked;
                ListBox lim = F<ListBox>("Limits"), lim2 = F<ListBox>("SetLimits");
                lim2.ItemsSource = lim.ItemsSource;
                lim2.SelectedItem = lim.SelectedItem;
                lim2.SelectionChanged += (s, e) => { if (lim2.SelectedItem != null) lim.SelectedItem = lim2.SelectedItem; };
                lim.SelectionChanged += (s, e) => lim2.SelectedItem = lim.SelectedItem;
            }
            else F<FrameworkElement>("SetGame").Visibility = F<FrameworkElement>("SetGameTitle").Visibility = Visibility.Collapsed;

            F<Btn>("CheckNow").Click += async (s, e) =>
            {
                if (CheckUpdates == null) return;
                F<Btn>("CheckNow").IsEnabled = false;
                string msg = await CheckUpdates();
                F<Btn>("CheckNow").IsEnabled = true;
                if (msg != null) Say(msg, false);
            };
            F<Btn>("OpenData").Click += (s, e) => Attempt(() => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = false }));
            F<Btn>("ExportSet").Click += (s, e) => Export();
            F<Btn>("ImportSet").Click += (s, e) => Import();
            StartShare();
            StartOverlay(); // OverlayUi.cs: its card is in "Al jugar"; the mark shortcut row is here
            Armed("ClearFps", "Borrar historial de FPS", ClearFps);
            Armed("ClearScan", "Borrar escaneo", () => { Attempt(() => Scanner.Delete(dir)); Say("Escaneo borrado. Se pedirá otra vez al volver a abrir.", false); });
            Armed("RestoreOrig", "Volver al estado Original", async () =>
            {
                SaveRow o = F<ListBox>("Saves").Items.Cast<SaveRow>().FirstOrDefault(x => x.S.Kind == "original");
                if (o == null || working) return;
                if (!o.CanRestore) { Say("El PC ya está en el estado Original.", false); return; }
                await SaveAction(o, true);
            });
            Armed("ResetSet", "Restablecer Ajustes", () =>
            {
                Dictionary<string, string> paths = Prefs.Now.Paths;
                Prefs.Now = Prefs.Default();
                Prefs.Now.Game = game.Id;
                Prefs.Now.Paths = paths; // where each game lives belongs to the PC, not a setting
                Fx.Level = 0;
                Attempt(() => { if (Installer.RunningInstalled) Autostart.Write(false); Prefs.Save(dir); });
                ShowTray();
                foreach (Action a in refresh) a();
                ShowColors();
                boot.IsChecked = Autostart.Read();
                ShowNote();
                MusicNow();
                Say("Ajustes restablecidos.", false);
            });
            F<Btn>("LinkGit").Click += (s, e) => Attempt(() => StretchSession.Open(Repo));
            F<Btn>("LinkBug").Click += (s, e) => Attempt(() => StretchSession.Open(Repo + "/issues/new"));
            F<Btn>("CopySupport").Click += (s, e) =>
            {
                try { Clipboard.SetText(Support()); Say("Datos copiados. No incluyen nada personal.", false); }
                catch (COMException ex) { Say("No se pudo copiar: " + ex.Message, false); }
            };
            F<Btn>("ShowTerms").Click += (s, e) => Info("Términos y privacidad", L.Terms().Replace("# ", ""));
            F<Btn>("ShowVersions").Click += (s, e) => Info("Historial de versiones", null);
            F<Btn>("InfoClose").Click += (s, e) => F<FrameworkElement>("InfoBox").Visibility = Visibility.Collapsed;
            F<FrameworkElement>("InfoBox").PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) F<FrameworkElement>("InfoBox").Visibility = Visibility.Collapsed; };
            F<FrameworkElement>("InfoShade").MouseLeftButtonDown += (s, e) => F<FrameworkElement>("InfoBox").Visibility = Visibility.Collapsed;
            F<TextBlock>("SetVersion").Text = "Tweakeroo " + Installer.Short;
            ShowTray();
            w.Closed += (s, e) => { if (tray != null) tray.Dispose(); };
        }

        readonly List<Action> refresh = new List<Action>();
        void SavePrefs() { Attempt(() => Prefs.Save(dir)); }
        void Attempt(Action a)
        {
            try { a(); }
            catch (Exception ex) { Say("No se pudo: " + ex.Message, false); }
        }
        void Toggle(string name, Func<bool> get, Action<bool> set)
        {
            CheckBox c = F<CheckBox>(name);
            refresh.Add(() => c.IsChecked = get());
            c.IsChecked = get();
            c.Click += (s, e) => { set(c.IsChecked == true); SavePrefs(); };
        }
        void Choice(string name, string[] labels, string[] values, Func<string> get, Action<string> set)
        {
            Choice(F<ListBox>(name), labels.Select((x, i) => new LimitOption { Name = x, Value = values[i] }).ToList(), get, set);
        }
        void Choice(ListBox l, List<LimitOption> o, Func<string> get, Action<string> set)
        {
            l.ItemsSource = o;
            refresh.Add(() => l.SelectedItem = o.FirstOrDefault(x => x.Value == get()));
            l.SelectedItem = o.FirstOrDefault(x => x.Value == get());
            l.SelectionChanged += (s, e) =>
            {
                LimitOption x = l.SelectedItem as LimitOption;
                if (x == null || x.Value == get()) return;
                set(x.Value);
                SavePrefs();
            };
        }
        void Volume(string name, Func<int> get, Action<int> set)
        {
            Slider s = F<Slider>(name);
            TextBlock t = F<TextBlock>(name + "Text");
            Action show = () => { s.Value = get(); t.Text = get() + " %"; };
            refresh.Add(show);
            show();
            s.ValueChanged += (o, e) =>
            {
                if ((int)s.Value == get()) return;
                set((int)s.Value);
                t.Text = get() + " %";
                SavePrefs();
            };
        }
        // Mode 0: only with the window in front. Mode 1: always, except with a game open. Mode 2: never.
        void MusicNow() { MusicNow(Prefs.Now.MusicMode == 1 ? Game.Open : null); }
        void MusicNow(Game open)
        {
            int m = Prefs.Now.MusicMode;
            bool on = m == 0 ? w.IsActive && w.IsVisible && w.WindowState != WindowState.Minimized : m == 1 && open == null;
            Sound.Music(on ? Prefs.Now.Music : "", Prefs.Now.MusicVolume / 100.0);
        }
        // First click asks for confirmation on the same button; after 4 seconds it is forgotten.
        void Armed(string name, string label, Action act)
        {
            Btn b = F<Btn>(name);
            b.Content = label;
            bool armed = false;
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            t.Tick += (s, e) => { t.Stop(); armed = false; b.Content = label; };
            b.Click += (s, e) =>
            {
                t.Stop();
                if (!armed) { armed = true; b.Content = "Pulsa otra vez para confirmar"; t.Start(); return; }
                armed = false;
                b.Content = label;
                act();
            };
        }

        void ShowNote()
        {
            if (game == Game.Valorant) { F<TextBlock>("PlayNote").Text = "Tweakeroo debe quedar abierto durante la partida. Si la pantalla no se estira sola, el botón Estirar ahora lo hace a mano."; return; }
            if (game == Game.Cs2) { F<TextBlock>("PlayNote").Text = "La primera vez que se usa una resolución se pide confirmación. Sin respuesta en " + Prefs.Now.Confirm + " segundos, se restaura la anterior. Tweakeroo debe quedar abierto durante la partida. En CS2 valen Pantalla completa y Ventana a pantalla completa; se recomienda Pantalla completa."; return; }
            F<TextBlock>("PlayNote").Text = "La primera vez que se usa una resolución se pide confirmación. Sin respuesta en " + Prefs.Now.Confirm + " segundos, se restaura la anterior. Tweakeroo debe quedar abierto durante la partida. En torneos oficiales Epic puede forzar 16:9.";
        }

        internal void OpenSettings()
        {
            F<ListBox>("Circuits").SelectedItem = null;
            F<Border>("NavMark").Visibility = Visibility.Hidden;
            foreach (string p in Pages) F<FrameworkElement>(p).Visibility = p == "SettingsPage" ? Visibility.Visible : Visibility.Collapsed;
            if (ready) EnterPage("SettingsPage");
        }

        void ChangeLanguage(bool en)
        {
            if (en == L.En) return;
            string no = Busy();
            if (no == null)
            {
                try { L.Save(dir, en); Again(); return; }
                catch (Exception ex) { no = "No se pudo cambiar el idioma: " + ex.Message; }
            }
            Say(no, false);
            // The list marks again the language that is still set.
            w.Dispatcher.BeginInvoke(new Action(() => { foreach (Action a in refresh) a(); }));
        }
        void Again() { quit = true; Installer.Start(Installer.Me); Application.Current.Shutdown(); }

        async void ClearFps()
        {
            await LoadFps();
            Attempt(() => { foreach (Game g in Game.All) FpsLog.Clear(dir, g); });
            played.Clear();
            ShowFps();
            ShowPlay();
            Say("Historial de FPS borrado.", false);
        }

        void Export()
        {
            Microsoft.Win32.SaveFileDialog d = new Microsoft.Win32.SaveFileDialog { FileName = "Tweakeroo.json", Filter = "JSON|*.json" };
            if (d.ShowDialog(w) != true) return;
            try { File.WriteAllText(d.FileName, Prefs.Export(dir)); Say("Ajustes exportados. No incluyen los tweaks.", false); }
            catch (Exception ex) { Say("No se pudo guardar: " + ex.Message, false); }
        }
        void Import()
        {
            if (Busy() != null) { Say(Busy(), false); return; }
            Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
            if (d.ShowDialog(w) != true) return;
            try
            {
                if (new FileInfo(d.FileName).Length > 1 << 20) throw new InvalidDataException("El archivo no es una copia de ajustes de Tweakeroo.");
                Prefs.Import(dir, File.ReadAllText(d.FileName));
                Again();
            }
            catch (Exception ex) { Say("No se pudo importar: " + ex.Message, false); }
        }

        internal string Support()
        {
            Hw h = hw ?? quick ?? new Hw();
            Res r = null;
            try { r = Display.Saved(); } catch (InvalidOperationException) { }
            return string.Join("\r\n", new[] {
                "Tweakeroo " + Installer.Short + (L.En ? " en" : " es"),
                "Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " x64" : ""),
                "GPU: " + h.Gpu, "CPU: " + h.Cpu, "RAM: " + h.RamGb + " GB",
                "Screen: " + (r == null ? "?" : r.Name + " " + r.Hz + " Hz"),
                "Tweaks: " + rows.Count(x => x.State == State.On) + "/" + rows.Count,
                "Macros: " + macros.On.Count });
        }

        void Info(string title, string text)
        {
            F<TextBlock>("InfoTitle").Text = title;
            F<TextBlock>("InfoText").Text = text ?? "";
            F<TextBlock>("InfoText").Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            ItemsControl list = F<ItemsControl>("InfoVersions");
            list.Visibility = text == null ? Visibility.Visible : Visibility.Collapsed;
            if (text == null) list.ItemsSource = VersionRow.All();
            F<ScrollViewer>("InfoScroll").ScrollToTop();
            FrameworkElement box = F<FrameworkElement>("InfoBox");
            box.Visibility = Visibility.Visible;
            Fx.From(box, UIElement.OpacityProperty, 0, 1, 160, Fx.Out, 0);
            Fx.Pop(F<FrameworkElement>("InfoCard"), 0.94);
            F<Btn>("InfoClose").Focus();
            if (text != null) return;
            list.UpdateLayout();
            for (int i = 0; i < Math.Min(list.Items.Count, 8); i++)
            {
                FrameworkElement x = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (x != null) Fx.Enter(x, i, 18);
            }
        }

        // ---- Tray ----

        void ShowTray()
        {
            if (!Prefs.Now.Tray || !TrayIcon)
            {
                if (tray != null) tray.Dispose();
                tray = null;
                return;
            }
            if (tray != null) return;
            tray = new System.Windows.Forms.NotifyIcon { Text = "Tweakeroo", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Installer.Me), Visible = true };
            tray.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            tray.ContextMenuStrip.Items.Add(L.T("Abrir"), null, (s, e) => Reveal());
            tray.ContextMenuStrip.Items.Add(L.T("Salir"), null, (s, e) => { Reveal(); quit = true; w.Close(); if (w.IsLoaded) quit = false; });
            tray.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Reveal(); };
        }
        internal void Reveal()
        {
            w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
        }
        // true = the window hides by the clock instead of closing.
        internal bool HideOnClose()
        {
            if (quit || !Prefs.Now.Tray || !ready) return false;
            w.Dispatcher.BeginInvoke(new Action(() => { if (w.IsLoaded) w.Hide(); }));
            return true;
        }
        internal void Quitting() { quit = true; }
    }
}
