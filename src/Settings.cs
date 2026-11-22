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
    // Ajustes, historial de versiones y bandeja.
    partial class Ui
    {
        const string Repo = "https://github.com/benjarn114/tweakeroo";
        public Func<Task<string>> CheckUpdates;
        public static bool TrayIcon = true; // las pruebas no ponen icono junto al reloj
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
            Choice("SetLang", new[] { "English", "Español" }, new[] { "en", "es" }, () => L.En ? "en" : "es", v => ChangeLanguage(v == "en"));
            Choice("SetPage", Prefs.PageNames, Prefs.PageNames, () => Prefs.Now.Page, v => Prefs.Now.Page = v);
            Choice("SetMotion", new[] { "Todas", "Sin rebotes", "Apagadas" }, new[] { "0", "1", "2" }, () => Prefs.Now.Motion.ToString(), v => { Prefs.Now.Motion = Fx.Level = int.Parse(v); });
            Choice("SetConfirm", Prefs.Seconds.Select(x => x + " s").ToArray(), Prefs.Seconds.Select(x => x.ToString()).ToArray(), () => Prefs.Now.Confirm.ToString(), v => { Prefs.Now.Confirm = int.Parse(v); ShowNote(); });
            ShowNote();

            // Las mismas opciones que en Jugar, siempre al dia en los dos sitios.
            if (stretch != null)
            {
                CheckBox min = F<CheckBox>("Minimize"), min2 = F<CheckBox>("SetMinimize");
                min2.IsChecked = stretch.Minimize;
                min2.Click += (s, e) => { Try(() => stretch.Minimize = min2.IsChecked == true); min.IsChecked = min2.IsChecked; };
                min.Click += (s, e) => min2.IsChecked = min.IsChecked;
                ListBox lim = F<ListBox>("Limits"), lim2 = F<ListBox>("SetLimits");
                lim2.ItemsSource = lim.ItemsSource;
                lim2.SelectedItem = lim.SelectedItem;
                lim2.SelectionChanged += (s, e) => { if (lim2.SelectedItem != null) lim.SelectedItem = lim2.SelectedItem; };
                lim.SelectionChanged += (s, e) => lim2.SelectedItem = lim.SelectedItem;
            }
            else F<FrameworkElement>("SetGame").Visibility = Visibility.Collapsed;

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
                Prefs.Now = Prefs.Default();
                Fx.Level = 0;
                Attempt(() => { if (Installer.RunningInstalled) Autostart.Write(false); Prefs.Save(dir); });
                ShowTray();
                foreach (Action a in refresh) a();
                boot.IsChecked = Autostart.Read();
                ShowNote();
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
            ListBox l = F<ListBox>(name);
            List<LimitOption> o = labels.Select((x, i) => new LimitOption { Name = x, Value = values[i] }).ToList();
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
        // Primer clic pide confirmacion en el mismo boton; a los 4 segundos se olvida.
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
            // La lista vuelve a marcar el idioma que sigue puesto.
            w.Dispatcher.BeginInvoke(new Action(() => { foreach (Action a in refresh) a(); }));
        }
        void Again() { quit = true; Installer.Start(Installer.Me); Application.Current.Shutdown(); }

        async void ClearFps()
        {
            await LoadFps();
            Attempt(() => FpsLog.Clear(dir));
            matches = new List<Match>();
            ShowFps();
            ShowPlay();
            Say("Historial de FPS borrado.", false);
        }

        void Export()
        {
            Microsoft.Win32.SaveFileDialog d = new Microsoft.Win32.SaveFileDialog { FileName = "Tweakeroo.json", Filter = "JSON|*.json" };
            if (d.ShowDialog(w) != true) return;
            try { File.WriteAllText(d.FileName, Prefs.Export(dir, ((Palette)F<ListBox>("Palettes").SelectedItem).Name)); Say("Ajustes exportados. No incluyen los tweaks.", false); }
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

        // ---- Bandeja ----

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
        // true = la ventana se esconde junto al reloj en vez de cerrarse.
        internal bool HideOnClose()
        {
            if (quit || !Prefs.Now.Tray || !ready) return false;
            w.Dispatcher.BeginInvoke(new Action(() => { if (w.IsLoaded) w.Hide(); }));
            return true;
        }
        internal void Quitting() { quit = true; }
    }
}
