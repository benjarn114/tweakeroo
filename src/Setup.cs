using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Tablero
{
    // Instala, registra en Aplicaciones de Windows y desinstala. Las rutas son sustituibles en las pruebas.
    public static class Installer
    {
        public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tweakeroo");
        public static string Menu = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        public static string Desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        public static RegistryHive Hive = RegistryHive.LocalMachine;
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Tweakeroo";
        public const string TermsVersion = "5";

        public static string Me { get { return Assembly.GetExecutingAssembly().Location; } }
        public static Version MyVersion { get { return Assembly.GetExecutingAssembly().GetName().Version; } }
        public static string Short { get { return MyVersion.ToString(3); } }
        public static string Exe { get { return Path.Combine(Dir, "Tweakeroo.exe"); } }
        public static bool RunningInstalled { get { return string.Equals(Path.GetFullPath(Me), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase); } }

        public static Version InstalledVersion()
        {
            Version v;
            return File.Exists(Exe) && Version.TryParse(FileVersionInfo.GetVersionInfo(Exe).FileVersion, out v) ? v : null;
        }
        // Abrir una descarga vieja lleva a la instalada, que es la que se actualiza sola.
        public static bool InstalledIsEnough()
        {
            Version v = InstalledVersion();
            return v != null && (v > MyVersion || (v == MyVersion && new FileInfo(Exe).Length == new FileInfo(Me).Length));
        }

        static string TermsFile(string data) { return Path.Combine(data, "terminos-aceptados.txt"); }
        public static bool TermsOk(string data)
        {
            try { return File.Exists(TermsFile(data)) && File.ReadAllText(TermsFile(data)).Split('|')[0] == TermsVersion; }
            catch (IOException) { return false; }
        }
        public static void AcceptTerms(string data) { Directory.CreateDirectory(data); File.WriteAllText(TermsFile(data), TermsVersion + "|" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); }

        static void Link(string folder, string exe)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic l = shell.CreateShortcut(Path.Combine(folder, "Tweakeroo.lnk"));
            l.TargetPath = exe; l.WorkingDirectory = Path.GetDirectoryName(exe); l.IconLocation = exe + ",0"; l.Description = "Reversible Windows and Fortnite tweaks";
            l.Save();
        }

        public static void Install(bool desktop)
        {
            Directory.CreateDirectory(Dir);
            string tmp = Exe + ".nuevo";
            File.Copy(Me, tmp, true);
            if (File.Exists(Exe)) { File.Replace(tmp, Exe, null); RefreshIcons(); } else File.Move(tmp, Exe);
            Link(Menu, Exe);
            if (desktop) Link(Desktop, Exe);
            Register();
        }

        public static void Register()
        {
            using (RegistryKey h = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64))
            using (RegistryKey k = h.CreateSubKey(Key))
            {
                k.SetValue("DisplayName", "Tweakeroo");
                k.SetValue("DisplayVersion", Short);
                k.SetValue("Publisher", "Tweakeroo");
                k.SetValue("DisplayIcon", Exe);
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + Exe + "\" --desinstalar");
                k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        // Tras una actualizacion: quita la version anterior y pone al dia el numero en Aplicaciones de Windows.
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int what, int flags, IntPtr a, IntPtr b);
        // Windows guarda los iconos en cache: sin este aviso la barra y los accesos directos siguen con el viejo.
        static void RefreshIcons() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }
        public static void AfterUpdate()
        {
            try
            {
                bool updated = File.Exists(Exe + ".viejo");
                if (updated) File.Delete(Exe + ".viejo");
                Register();
                if (updated) RefreshIcons();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Remove()
        {
            try { Autostart.Write(false); }
            catch (COMException) { }
            catch (UnauthorizedAccessException) { }
            foreach (string f in new[] { Menu, Desktop }) File.Delete(Path.Combine(f, "Tweakeroo.lnk"));
            using (RegistryKey h = RegistryKey.OpenBaseKey(Hive, RegistryView.Registry64)) h.DeleteSubKeyTree(Key, false);
        }
        // El .exe abierto no se puede borrar a si mismo: lo hace cmd dos segundos despues. Solo esos archivos y la carpeta si queda vacia.
        public static void RemoveFilesAfterExit()
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c ping 127.0.0.1 -n 3 >nul & del /q \"" + Exe + "\" \"" + Exe + ".viejo\" & rmdir \"" + Dir + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.SystemDirectory });
        }

        public static void Start(string exe) { Process.Start(new ProcessStartInfo(exe, "--espera " + Process.GetCurrentProcess().Id) { UseShellExecute = true }); }
    }

    // Pasos de instalacion dentro de la misma ventana.
    class SetupUi
    {
        static readonly string[] Pages = { "SetupWelcome", "SetupTerms", "SetupInstall", "SetupRemove" };
        readonly Window w;
        readonly string data;
        bool busy;
        T F<T>(string n) where T : class { return w.FindName(n) as T; }
        TaskCompletionSource<bool> accepted;

        public SetupUi(Window w, string data)
        {
            this.w = w; this.data = data;
            F<FrameworkElement>("Setup").Visibility = Visibility.Visible;
            F<TextBlock>("SetupVersion").Text = "Versión " + Installer.Short;
            FillTerms();
            w.Closing += (s, e) => { if (busy) e.Cancel = true; };
        }

        void FillTerms()
        {
            StackPanel p = F<StackPanel>("TermsText");
            foreach (string raw in L.Terms().Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                bool head = raw.StartsWith("# ");
                TextBlock t = new TextBlock { Text = head ? raw.Substring(2) : raw, TextWrapping = TextWrapping.Wrap, FontSize = head ? 17 : 14.5, FontWeight = head ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, head && p.Children.Count > 0 ? 18 : 0, 0, 4), LineHeight = head ? 22 : 20 };
                if (!head) t.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
                p.Children.Add(t);
            }
            CheckBox ok = F<CheckBox>("TermsCheck");
            ok.Click += (s, e) => F<Btn>("TermsOk").IsEnabled = ok.IsChecked == true;
        }

        internal void Show(string page)
        {
            foreach (string p in Pages) F<FrameworkElement>(p).Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
            int at = Array.IndexOf(Pages, page);
            for (int i = 0; i < 3; i++)
            {
                F<FrameworkElement>("StepOn" + (i + 1)).Opacity = i <= at && at < 3 ? 1 : 0;
                F<FrameworkElement>("SetupStep" + (i + 1)).Opacity = i == at ? 1 : 0.6;
            }
            F<FrameworkElement>("SetupSteps").Visibility = at < 3 ? Visibility.Visible : Visibility.Collapsed;
            if (at < 3 && F<FrameworkElement>("StepOn" + (at + 1)) != null) Fx.Pop(F<FrameworkElement>("StepOn" + (at + 1)), 0.4);
            Panel body = F<Panel>(page + "Body");
            for (int i = 0; i < body.Children.Count; i++) Fx.Enter((FrameworkElement)body.Children[i], i, 24);
        }

        // Instalada pero sin aceptar los terminos de esta version.
        public Task AskTerms()
        {
            accepted = new TaskCompletionSource<bool>();
            F<FrameworkElement>("SetupSteps").Visibility = Visibility.Collapsed;
            F<Btn>("TermsOk").Click += (s, e) =>
            {
                try { Installer.AcceptTerms(data); }
                catch (Exception ex) { MessageBox.Show(w, ex.Message, "Tweakeroo"); return; }
                F<FrameworkElement>("Setup").Visibility = Visibility.Collapsed;
                accepted.TrySetResult(true);
            };
            Show("SetupTerms");
            return accepted.Task;
        }

        public void Install()
        {
            Version old = Installer.InstalledVersion();
            foreach (bool en in new[] { false, true })
            {
                bool want = en;
                Btn b = F<Btn>(en ? "SetupEn" : "SetupEs");
                b.Opacity = en == L.En ? 1 : 0.6;
                b.Click += (s, e) =>
                {
                    if (want == L.En) return;
                    try { L.Save(data, want); Installer.Start(Installer.Me); Application.Current.Shutdown(); }
                    catch (Exception ex) { MessageBox.Show(w, ex.Message, "Tweakeroo"); }
                };
            }
            F<Btn>("SetupGo").Click += (s, e) => Show(Installer.TermsOk(data) ? "SetupInstall" : "SetupTerms");
            F<Btn>("TermsOk").Click += (s, e) => Show("SetupInstall");
            F<TextBlock>("InstallWhere").Text = Installer.Dir;
            Btn go = F<Btn>("InstallGo");
            go.Content = old == null ? "Instalar" : "Actualizar a " + Installer.Short;
            bool done = false;
            go.Click += async (s, e) =>
            {
                if (done)
                {
                    try { Installer.Start(Installer.Exe); Application.Current.Shutdown(); }
                    catch (Exception ex) { F<TextBlock>("InstallNote").Text = "No se pudo abrir: " + ex.Message; }
                    return;
                }
                busy = true; go.IsEnabled = false; go.Content = "Instalando…";
                bool desk = F<CheckBox>("InstallDesktop").IsChecked == true;
                string err = null;
                await Task.Run(() => { try { Installer.AcceptTerms(data); Installer.Install(desk); } catch (Exception ex) { err = ex.Message; } });
                busy = false; go.IsEnabled = true; done = err == null;
                go.Content = done ? "Abrir Tweakeroo" : "Reintentar";
                F<TextBlock>("InstallNote").Text = done ? "Instalación completada. Tweakeroo está en el menú Inicio. El archivo descargado se puede borrar." : "No se pudo instalar: " + err;
                F<FrameworkElement>("InstallOptions").Visibility = done ? Visibility.Collapsed : Visibility.Visible;
                Fx.Pop(go, 0.92);
            };
            Show("SetupWelcome");
        }

        public void Uninstall()
        {
            Btn go = F<Btn>("RemoveGo");
            bool done = false;
            w.Closed += (s, e) => { if (done && Installer.RunningInstalled) Installer.RemoveFilesAfterExit(); };
            go.Click += async (s, e) =>
            {
                if (done) { Application.Current.Shutdown(); return; }
                busy = true; go.IsEnabled = false; go.Content = "Desinstalando…";
                bool back = F<CheckBox>("RemoveRestore").IsChecked == true, wipe = F<CheckBox>("RemoveData").IsChecked == true;
                string err = null, warn = "";
                await Task.Run(() =>
                {
                    try
                    {
                        if (IniOp.GameRunning()) throw new InvalidOperationException("Cierra Fortnite y vuelve a intentarlo.");
                        new StretchSession(data).End();
                        if (back) warn = RestoreOriginal();
                        Installer.Remove();
                        // Los guardados no se borran si quedo algo sin restaurar.
                        if (wipe && warn.Length == 0 && Directory.Exists(data)) Directory.Delete(data, true);
                    }
                    catch (Exception ex) { err = ex.Message; }
                });
                busy = false; go.IsEnabled = true; done = err == null;
                go.Content = done ? "Cerrar" : "Reintentar";
                F<TextBlock>("RemoveNote").Text = done ? "Tweakeroo se ha desinstalado. Reinicia el PC para completar los cambios." + warn : "No se pudo: " + err;
                F<FrameworkElement>("RemoveOptions").Visibility = done ? Visibility.Collapsed : Visibility.Visible;
            };
            Show("SetupRemove");
        }

        string RestoreOriginal()
        {
            Engine e = new Engine(data, Ui.Source());
            Snapshot o = e.List().FirstOrDefault(x => x.Kind == "original");
            if (o == null) return "";
            int n;
            List<string> errs = e.Restore(o, out n);
            return errs.Count == 0 ? "" : (errs.Count == 1 ? " No se pudo restaurar 1 ajuste" : " No se pudieron restaurar " + errs.Count + " ajustes") + "; los guardados se conservan.";
        }
    }
}
