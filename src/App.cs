using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("Tweakeroo")]
[assembly: AssemblyProduct("Tweakeroo")]
[assembly: AssemblyVersion("1.3.0.0")]

namespace Tablero
{
    static class Program
    {
        static Window Load()
        {
            Assembly me = Assembly.GetExecutingAssembly();
            using (StreamReader r = new StreamReader(me.GetManifestResourceStream("Window.xaml")))
                return (Window)XamlReader.Parse(L.Xaml(r.ReadToEnd()).Replace("{{ASM}}", me.GetName().Name));
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--selftest")
            {
                string r; int code = 0;
                try { r = SelfTest.Run(); } catch (Exception ex) { r = ex.ToString(); code = 1; }
                File.WriteAllText(args[1], r);
                return code;
            }
#if SHOT
            // "--escenas folder": each scene at a few frames, 4x without smoothing, and its cost per frame.
            if (args.Length == 2 && args[0] == "--escenas")
            {
                string txt = "";
                foreach (string id in Scene.Ids)
                {
                    foreach (int at in new[] { 0, 300, 900 })
                    {
                        Scene s = Scene.Make(id);
                        s.Skip(at);
                        int[] big = new int[Scene.W * 4 * Scene.H * 4];
                        for (int y = 0; y < Scene.H * 4; y++) for (int x = 0; x < Scene.W * 4; x++) big[y * Scene.W * 4 + x] = s.Fb[(y / 4) * Scene.W + x / 4];
                        PngBitmapEncoder png = new PngBitmapEncoder();
                        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Scene.W * 4, Scene.H * 4, 96, 96, PixelFormats.Bgra32, null, big, Scene.W * 16)));
                        using (FileStream f = File.Create(Path.Combine(args[1], id + "-" + at + ".png"))) png.Save(f);
                    }
                    txt += id + " " + Scene.Cost(id, 600).ToString("0.00") + " ms\r\n";                }
                File.WriteAllText(Path.Combine(args[1], "escenas.txt"), txt);
                return 0;
            }
#endif
            // After installing or updating, wait for the previous copy to close.
            if (args.Length == 2 && args[0] == "--espera")
            {
                int pid;
                try { if (int.TryParse(args[1], out pid)) using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(pid)) p.WaitForExit(15000); }
                catch (ArgumentException) { }
            }
            bool mine, boot = args.Length > 0 && args[0] == "--inicio";
#if SHOT
            const string mutex = "Tablero_prueba";
#else
            const string mutex = "Tablero_unica_instancia";
#endif
            using (Mutex m = new Mutex(true, mutex, out mine))
            {
                if (!mine)
                {
                    if (boot) return 0;
                    // The open copy is shown, even if it is hidden by the clock.
                    EventWaitHandle other;
                    if (EventWaitHandle.TryOpenExisting(mutex + "_mostrar", out other)) using (other) other.Set();
                    else MessageBox.Show(L.T("Tweakeroo ya está abierto."), "Tweakeroo");
                    return 0;
                }
                EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, mutex + "_mostrar");
                Application app = new Application();
#if !SHOT
                app.DispatcherUnhandledException += (s, e) =>
                {
                    // Full trace to a file so the user can send where it failed; capped so it never grows big.
                    try
                    {
                        string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tweakeroo", "errores.txt");
                        if (File.Exists(log) && new FileInfo(log).Length > 256 * 1024) File.Delete(log);
                        File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + Installer.Short + "\r\n" + e.Exception + "\r\n\r\n");
                    }
                    catch (Exception) { }
                    e.Handled = true;
                    // A failed animation is only cosmetic: logged, but it must not block the app with a dialog.
                    string st = e.Exception.StackTrace ?? "";
                    if (e.Exception is InvalidOperationException && (st.Contains("BeginAnimation") || st.Contains("Animat"))) return;
                    MessageBox.Show("Algo falló: " + e.Exception.Message, "Tweakeroo", MessageBoxButton.OK, MessageBoxImage.Error);
                };
#endif
#if SHOT
                app.DispatcherUnhandledException += (s, e) => { File.WriteAllText(args[0] + ".err", e.Exception.ToString()); Environment.Exit(1); };
#endif
                string data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(data, "Tweakeroo");
#if !SHOT
                // It used to be called Tablero: its saved data is kept.
                string old = Path.Combine(data, "Tablero");
                if (Directory.Exists(old) && !Directory.Exists(dir))
                {
                    try { Directory.Move(old, dir); }
                    catch (IOException) { dir = old; }
                    catch (UnauthorizedAccessException) { dir = old; }
                }
#endif
                L.Load(dir);
                Prefs.Load(dir);
#if SHOT
                L.En = Array.IndexOf(args, "en") >= 0;
#endif
                Window w = Load();
                w.Title = "Tweakeroo " + Installer.Short;
#if SHOT
                // Only for reviewing the design: own folder, off screen and without stealing focus.
                dir = Path.Combine(Path.GetTempPath(), "tablero-shot");
                w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -4000; w.ShowActivated = false; w.ShowInTaskbar = false;
                w.Width = int.Parse(args[1]); w.Height = int.Parse(args[2]);
                if (args[3] == "click") ClickTest.Prepare(ref dir);
                Ui.TrayIcon = false;
                Prefs.Load(dir);
                // No saved scan: with one, the button would apply the recommended tweaks on the real PC.
                if (Array.IndexOf(args, "scan") >= 0) Scanner.Delete(dir);
                if (Array.IndexOf(args, "fortnite") >= 0) Prefs.Now.Game = "fortnite";
                if (Array.IndexOf(args, "valorant") >= 0 && Array.IndexOf(args, "medio") < 0) Prefs.Now.Game = "valorant";
                if (Array.IndexOf(args, "cs2") >= 0) Prefs.Now.Game = "cs2";
                if (Array.IndexOf(args, "fijar") >= 0) Prefs.Now.Pinned = new System.Collections.Generic.List<string> { "fortnite", "cs2" };
                if (Array.IndexOf(args, "noinst") >= 0) Game.Valorant.Installed = () => false;
                // "tema noche|lluvia|arcade|simple"; without it, what the test folder has saved.
                int theme = Array.IndexOf(args, "tema");
                if (theme >= 0) Prefs.Now.Theme = Theme.LookOf(args[theme + 1]).Id;
#endif
#if !SHOT
                // A standalone download installs; if an equal or newer one is already installed, opens that one.
                bool remove = args.Length > 0 && args[0] == "--desinstalar";
                if (!remove && !Installer.RunningInstalled && Installer.InstalledIsEnough() && Installer.TermsOk(dir)) { Installer.Start(Installer.Exe); return 0; }
                // Running as administrator, Windows does not pass PrtScn or Win+Shift+S from this window to the Snipping Tool, which is not elevated:
                // it is opened from here, through Explorer so it does not inherit the rights. The keys are not swallowed.
                Action snip = () => { try { StretchSession.Open("ms-screenclip:"); } catch (System.ComponentModel.Win32Exception) { } };
                w.PreviewKeyDown += (s, e) => { if (e.Key == Key.S && !e.IsRepeat && Keyboard.Modifiers == (ModifierKeys.Windows | ModifierKeys.Shift)) snip(); };
                w.PreviewKeyUp += (s, e) =>
                {
                    if (e.Key != Key.Snapshot) return; // PrtScn only arrives as key up
                    // With the option off, Windows copies the screen by itself. Without the value, Windows 11 has it on.
                    object on = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Keyboard", "PrintScreenKeyForSnippingEnabled", null);
                    if (on is int ? (int)on != 0 : Environment.OSVersion.Version.Build >= 22000) snip();
                };
#endif
                Ui ui = new Ui(w, dir);
                ui.LoadPalette();
                UpdateUi update = new UpdateUi(w, ui.Busy);
                ui.CheckUpdates = update.Check;
                ThreadPool.RegisterWaitForSingleObject(show, (o, late) => w.Dispatcher.BeginInvoke(new Action(ui.Reveal)), null, -1, false);
                bool quiet = boot && Prefs.Now.StartMin && Installer.TermsOk(dir);
                if (quiet) w.WindowState = WindowState.Minimized;
                w.Loaded += async (s, e) =>
                {
#if !SHOT
                    if (remove) { new SetupUi(w, dir).Uninstall(); return; }
                    if (!Installer.RunningInstalled) { new SetupUi(w, dir).Install(); return; }
                    Installer.AfterUpdate();
                    if (!Installer.TermsOk(dir)) await new SetupUi(w, dir).AskTerms();
                    if (quiet && Prefs.Now.Tray) w.Hide();
                    if (Prefs.Now.AutoCheck) { Task check = update.Check(); }
#else
                    // "instalar-N" and "actualizar" only draw those screens.
                    if (args[3] == "carga") ui.ShowSkeletons("SkelPage", "SkelNav", "SkelSaves");
                    if (args[3].StartsWith("Setup")) new SetupUi(w, dir).Show(args[3]);
                    if (args[3] == "actualizar") update.Show(new Release { Version = "3.1.0", Notes = new System.Collections.Generic.List<string> { "Stretched más rápido al abrir Fortnite.", "Dos tweaks nuevos para gráficas AMD.", "Arreglado un parpadeo blanco en Guardados." } });
                    bool still = args[3] == "carga" || args[3].StartsWith("Setup") || args[3] == "actualizar";
#endif
#if SHOT
                    // "carga" captures the skeletons before reading anything.
                    if (!still)
#endif
                    await ui.Start();
#if SHOT
                    if (args[3] == "click") { string res; try { res = await ClickTest.Run(w, ui); } catch (Exception ex) { res = ex.ToString(); } File.WriteAllText(args[0], res); app.Shutdown(); return; }
                    // args: output width height section palette [scan]
                    int half = Array.IndexOf(args, "medio");
                    if (half >= 0) { ui.SwitchGame(Game.Fortnite, true); await Task.Delay(2500); ui.SwitchGame(Game.Valorant, true); await Task.Delay(int.Parse(args[half + 1])); }
                    if (Array.IndexOf(args, "estirar") >= 0) { CheckBox vo = (CheckBox)w.FindName("ValOn"); vo.IsChecked = true; vo.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
                    if (args[4] != "-") ((ListBox)w.FindName("Palettes")).SelectedIndex = int.Parse(args[4]);
                    if (Array.IndexOf(args, "scan") >= 0)
                    {
                        Btn scan = (Btn)w.FindName("Scan");
                        scan.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        while (!scan.IsEnabled) await Task.Delay(100);
                    }
                    if (!still) ui.Open(args[3]);
                    if (Array.IndexOf(args, "juegos") >= 0) ((Btn)w.FindName("AllGames")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    if (Array.IndexOf(args, "abierto") >= 0) { ((ScrollViewer)w.FindName("UpgradePage")).ScrollToEnd(); }
                    if (Array.IndexOf(args, "versiones") >= 0) ((Btn)w.FindName("ShowVersions")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    if (Array.IndexOf(args, "fin") >= 0) foreach (string n in new[] { "SettingsPage", "MacroPage", "NetPage", "ResPage", "BoostPage" }) ((ScrollViewer)w.FindName(n)).ScrollToEnd();
                    if (Array.IndexOf(args, "medir") >= 0) { Btn run = (Btn)w.FindName("NetRun"); run.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); while (!run.IsEnabled) await Task.Delay(200); }
                    if (Array.IndexOf(args, "vivo") >= 0) ((ListBox)w.FindName("ColorProfiles")).SelectedIndex = 3;
                    // "preguntar texto": Help with one answer (Assistant.cs).
                    int ask = Array.IndexOf(args, "preguntar");
                    if (ask >= 0) await ui.Ask(args[ask + 1]);
                    // "reporte": and then "No se solucionó", with the report form open.
                    if (Array.IndexOf(args, "reporte") >= 0) ui.Unsolved();
                    // "fotograma N": the background as it is N frames later (the test window is never active, so it does not move).
                    int fr = Array.IndexOf(args, "fotograma");
                    if (fr >= 0) ui.SkipScene(int.Parse(args[fr + 1]));
                    // "animar N": process CPU for N seconds with the background still and N more with it moving.
                    int an = Array.IndexOf(args, "animar");
                    if (an >= 0)
                    {
                        int secs = int.Parse(args[an + 1]);
                        System.Diagnostics.Process me = System.Diagnostics.Process.GetCurrentProcess();
                        Func<Task<double>> cpu = async () => { me.Refresh(); TimeSpan t0 = me.TotalProcessorTime; await Task.Delay(secs * 1000); me.Refresh(); return (me.TotalProcessorTime - t0).TotalMilliseconds / (secs * 10.0); };
                        await Task.Delay(3000);
                        double idle = await cpu();
                        Ui.Always = true; ui.SceneNow();
                        double moving = await cpu();
                        Ui.Always = false; ui.SceneNow();
                        File.WriteAllText(args[0] + ".cpu.txt", "CPU de un núcleo: quieto " + idle.ToString("0.00") + " %, moviéndose " + moving.ToString("0.00") + " %. Núcleos: " + Environment.ProcessorCount + ". Del total: " + (moving / Environment.ProcessorCount).ToString("0.00") + " %.");
                    }
                    // "tirones N [raton]": time between background frames for N seconds, in output.tirones.txt (Jank.cs).
                    int jk = Array.IndexOf(args, "tirones");
                    if (jk >= 0) File.WriteAllText(args[0] + ".tirones.txt", await Jank.Measure(w, ui, int.Parse(args[jk + 1]), Array.IndexOf(args, "raton") >= 0));
                    if (Array.IndexOf(args, "medio") < 0) await Task.Delay(1800);
                    // "abrir Name" captures with that dropdown open (the popup is drawn over the window).
                    int dd = Array.IndexOf(args, "abrir");
                    if (dd >= 0) { Drop d = (Drop)w.FindName(args[dd + 1]); d.BringIntoView(); await Task.Delay(300); d.Open(); await Task.Delay(600); }
                    if (Array.IndexOf(args, "limpio") >= 0) ((UIElement)w.FindName("Toast")).Visibility = Visibility.Collapsed;
                    FrameworkElement root = (FrameworkElement)w.Content;
                    RenderTargetBitmap bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(root);
                    PngBitmapEncoder enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(Drop.Shown != null ? Drop.Shown.Over(root, bmp) : bmp));
                    using (FileStream f = File.Create(args[0])) enc.Save(f);
                    app.Shutdown();
#endif
                };
                return app.Run(w);
            }
        }
    }
}
