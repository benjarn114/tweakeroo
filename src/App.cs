using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: AssemblyTitle("Tweakeroo")]
[assembly: AssemblyProduct("Tweakeroo")]
[assembly: AssemblyVersion("3.5.0.0")]

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
            // Tras instalar o actualizar, espera a que cierre la copia anterior.
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
                    // La copia abierta se enseña, aunque este escondida junto al reloj.
                    EventWaitHandle other;
                    if (EventWaitHandle.TryOpenExisting(mutex + "_mostrar", out other)) using (other) other.Set();
                    else MessageBox.Show(L.T("Tweakeroo ya está abierto."), "Tweakeroo");
                    return 0;
                }
                EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, mutex + "_mostrar");
                Application app = new Application();
#if !SHOT
                app.DispatcherUnhandledException += (s, e) => { MessageBox.Show("Algo falló: " + e.Exception.Message, "Tweakeroo", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
#endif
#if SHOT
                app.DispatcherUnhandledException += (s, e) => { File.WriteAllText(args[0] + ".err", e.Exception.ToString()); Environment.Exit(1); };
#endif
                string data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(data, "Tweakeroo");
#if !SHOT
                // Antes se llamaba Tablero: se conservan sus guardados.
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
                // Solo para revisar el diseno: carpeta propia, fuera de pantalla y sin robar el foco.
                dir = Path.Combine(Path.GetTempPath(), "tablero-shot");
                w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -4000; w.ShowActivated = false; w.ShowInTaskbar = false;
                w.Width = int.Parse(args[1]); w.Height = int.Parse(args[2]);
                if (args[3] == "click") ClickTest.Prepare(ref dir);
                Ui.TrayIcon = false;
                Prefs.Load(dir);
#endif
#if !SHOT
                // Una descarga suelta instala; si ya hay una igual o mas nueva, abre esa.
                bool remove = args.Length > 0 && args[0] == "--desinstalar";
                if (!remove && !Installer.RunningInstalled && Installer.InstalledIsEnough() && Installer.TermsOk(dir)) { Installer.Start(Installer.Exe); return 0; }
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
                    // "instalar-N" y "actualizar" solo dibujan esas pantallas.
                    if (args[3] == "carga") ui.ShowSkeletons("SkelPage", "SkelNav", "SkelSaves");
                    if (args[3].StartsWith("Setup")) new SetupUi(w, dir).Show(args[3]);
                    if (args[3] == "actualizar") update.Show(new Release { Version = "3.1.0", Notes = new System.Collections.Generic.List<string> { "Stretched más rápido al abrir Fortnite.", "Dos tweaks nuevos para gráficas AMD.", "Arreglado un parpadeo blanco en Guardados." } });
                    bool still = args[3] == "carga" || args[3].StartsWith("Setup") || args[3] == "actualizar";
#endif
#if SHOT
                    // "carga" captura los skeletons antes de leer nada.
                    if (!still)
#endif
                    await ui.Start();
#if SHOT
                    if (args[3] == "click") { string res; try { res = await ClickTest.Run(w, ui); } catch (Exception ex) { res = ex.ToString(); } File.WriteAllText(args[0], res); app.Shutdown(); return; }
                    // args: salida ancho alto seccion paleta [scan]
                    ((ListBox)w.FindName("Palettes")).SelectedIndex = int.Parse(args[4]);
                    if (Array.IndexOf(args, "scan") >= 0)
                    {
                        Btn scan = (Btn)w.FindName("Scan");
                        scan.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        while (!scan.IsEnabled) await Task.Delay(100);
                    }
                    if (!still) ui.Open(args[3]);
                    if (Array.IndexOf(args, "abierto") >= 0) { ((Btn)w.FindName("ResOpen")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); ((ScrollViewer)w.FindName("UpgradePage")).ScrollToEnd(); }
                    if (Array.IndexOf(args, "versiones") >= 0) ((Btn)w.FindName("ShowVersions")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    if (Array.IndexOf(args, "fin") >= 0) ((ScrollViewer)w.FindName("SettingsPage")).ScrollToEnd();
                    await Task.Delay(1800);
                    FrameworkElement root = (FrameworkElement)w.Content;
                    RenderTargetBitmap bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(root);
                    PngBitmapEncoder enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (FileStream f = File.Create(args[0])) enc.Save(f);
                    app.Shutdown();
#endif
                };
                return app.Run(w);
            }
        }
    }
}
