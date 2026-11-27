// Closing heavy apps while playing. Off by default (Prefs.HeavyOn) and always asked unless the answer was remembered (Prefs.HeavyKeep).
// Never kills a process: CloseMainWindow (or the app's own quit command) and up to 5 s; what does not close stays open.
// Rides on the app's 2 s game check (GameUi.GameTick): nothing new watches outside a game.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Tablero
{
    public class HeavyApp
    {
        public string Proc;  // process name without ".exe"
        public string Name;  // shown name
        public long Mb;      // working set of all its processes
        public string Exe;   // to open it again; null = unknown
    }

    public static class Heavy
    {
        // Process, shown name, usual places (| between them), own quit command ("" = none).
        static readonly string[][] Known = {
            new[] { "chrome", "Chrome", @"%ProgramFiles%\Google\Chrome\Application\chrome.exe|%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe|%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe", "" },
            new[] { "msedge", "Edge", @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe|%ProgramFiles%\Microsoft\Edge\Application\msedge.exe", "" },
            new[] { "firefox", "Firefox", @"%ProgramFiles%\Mozilla Firefox\firefox.exe|%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe", "" },
            new[] { "opera", "Opera", @"%LOCALAPPDATA%\Programs\Opera\opera.exe|%LOCALAPPDATA%\Programs\Opera GX\opera.exe", "" },
            new[] { "Spotify", "Spotify", @"%APPDATA%\Spotify\Spotify.exe", "" },
            new[] { "steam", "Steam", @"%ProgramFiles(x86)%\Steam\steam.exe", "-shutdown" },
            new[] { "EpicGamesLauncher", "Epic Games", @"%ProgramFiles(x86)%\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe|%ProgramFiles%\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", "" },
            new[] { "OneDrive", "OneDrive", @"%LOCALAPPDATA%\Microsoft\OneDrive\OneDrive.exe|%ProgramFiles%\Microsoft OneDrive\OneDrive.exe", "/shutdown" },
            new[] { "ms-teams", "Teams", @"%LOCALAPPDATA%\Microsoft\WindowsApps\ms-teams.exe", "" },
            new[] { "Discord", "Discord", @"%LOCALAPPDATA%\Discord\Update.exe", "" },
        };
        // Discord is used to talk during the match: offered, not marked.
        public static List<string> Defaults { get { return Known.Select(k => k[0]).Where(p => p != "Discord").ToList(); } }
        public static List<string> Chosen(Prefs p) { return p.HeavyApps ?? Defaults; }
        public static string NameOf(string proc) { string[] k = Known.FirstOrDefault(x => x[0].Equals(proc, StringComparison.OrdinalIgnoreCase)); return k == null ? proc : k[1]; }
        static string QuitOf(string proc) { string[] k = Known.FirstOrDefault(x => x[0].Equals(proc, StringComparison.OrdinalIgnoreCase)); return k == null ? "" : k[3]; }

        // Steam is not offered with a Steam game open, nor Epic with Fortnite.
        public static bool Excluded(Game g, string proc)
        {
            return g == Game.Cs2 && proc.Equals("steam", StringComparison.OrdinalIgnoreCase) || g == Game.Fortnite && proc.Equals("EpicGamesLauncher", StringComparison.OrdinalIgnoreCase);
        }
        // The chosen apps to offer with this game, of those open now.
        public static List<string> Propose(Game g, IEnumerable<string> chosen, Func<string, bool> open)
        {
            return chosen.Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !Excluded(g, p) && open(p)).ToList();
        }

        // ---- Texts ----
        static CultureInfo Num { get { return L.En ? CultureInfo.InvariantCulture : new CultureInfo("es-ES"); } }
        public static string Size(long mb) { return mb >= 1024 ? (mb / 1024.0).ToString("0.0", Num) + " GB" : mb + " MB"; }
        static string Names(List<HeavyApp> apps) { return string.Join(", ", apps.Take(3).Select(a => a.Name)) + (apps.Count > 3 ? "…" : ""); }
        public static string AskText(List<HeavyApp> apps)
        {
            return "¿Cerrar " + Names(apps) + "? " + (apps.Count == 1 ? "Libera " : "Liberan ") + Size(apps.Sum(a => a.Mb)) + ".";
        }
        public static string ReopenText(List<HeavyApp> apps) { return "¿Volver a abrir " + Names(apps) + "?"; }
        // After closing: null = nothing to say.
        public static string Result(List<HeavyApp> asked, List<HeavyApp> closed, bool auto)
        {
            List<HeavyApp> left = asked.Where(a => !closed.Contains(a)).ToList();
            string done = auto && closed.Count > 0 ? "Cerrado sin preguntar: " + Names(closed) + "." : null, still = left.Count > 0 ? "Siguen abiertos: " + Names(left) + "." : null;
            return done != null && still != null ? done + " " + still : done ?? still;
        }
        public static string KeepText(int keep) { return keep == 1 ? "Se cierran sin preguntar." : keep == 2 ? "No se pregunta al jugar." : null; }
        public static void Remember(Prefs p, bool yes) { p.HeavyKeep = yes ? 1 : 2; }
        public static IEnumerable<string> Texts()
        {
            List<HeavyApp> two = new List<HeavyApp> { new HeavyApp { Name = "Chrome", Mb = 1500 }, new HeavyApp { Name = "Spotify", Mb = 650 } };
            return new[] { AskText(two), AskText(two.Take(1).ToList()), ReopenText(two), Result(two, two.Take(1).ToList(), true), Result(two, new List<HeavyApp>(), false), Result(two, two, true), KeepText(1), KeepText(2),
                "Sí", "No", "Recordar mi elección", "Volver a preguntar", "Ninguna encontrada en este PC.", "Cerrar apps pesadas al jugar", "Al abrir un juego se pregunta antes de cerrar nada; al cerrarlo se ofrece volver a abrirlas." };
        }

        // ---- This PC ----
        static string Where(string[] k)
        {
            foreach (string p in k[2].Split('|')) { string f = Environment.ExpandEnvironmentVariables(p); if (File.Exists(f)) return f; }
            if (k[0] == "steam")
            {
                object s = null;
                try { s = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null); } catch (Exception) { }
                if (s is string && File.Exists((string)s)) return (string)s;
            }
            return null;
        }
        // Can be closed gently: a window to close, or its own quit command.
        static bool Closable(string proc)
        {
            Process[] ps = Get(proc);
            bool ok = ps.Length > 0 && (QuitOf(proc) != "" || ps.Any(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { return false; } }));
            foreach (Process p in ps) p.Dispose();
            return ok;
        }
        static Process[] Get(string proc) { try { return Process.GetProcessesByName(proc); } catch (InvalidOperationException) { return new Process[0]; } }

        // Offered in Settings: the known apps on this PC, the ones open now with much memory, and the ones already chosen.
        // Process name and shown text. Off the UI thread: lists the processes.
        public static Func<List<KeyValuePair<string, string>>> Offer = () =>
        {
            List<string> procs = Known.Where(k => Where(k) != null).Select(k => k[0]).ToList();
            string[] skip = { "Tweakeroo", "explorer", "dwm", "Memory Compression", "FortniteClient-Win64-Shipping", "VALORANT-Win64-Shipping", "cs2", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "svchost", "MsMpEng", "TextInputHost", "ApplicationFrameHost" };
            Dictionary<string, long> mb = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                int session = Process.GetCurrentProcess().SessionId;
                foreach (Process p in Process.GetProcesses())
                {
                    using (p)
                    {
                        try { if (p.SessionId == session && !skip.Contains(p.ProcessName)) { long v; mb.TryGetValue(p.ProcessName, out v); mb[p.ProcessName] = v + p.WorkingSet64 / (1 << 20); } }
                        catch (InvalidOperationException) { }
                        catch (Win32Exception) { }
                    }
                }
            }
            catch (InvalidOperationException) { }
            procs.AddRange(mb.Where(x => x.Value >= 500 && Closable(x.Key)).OrderByDescending(x => x.Value).Select(x => x.Key));
            procs.AddRange(Chosen(Prefs.Now));
            return procs.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => { long v; return new KeyValuePair<string, string>(p, NameOf(p) + (mb.TryGetValue(p, out v) && v >= 100 ? " · " + Size(v) : "")); }).ToList();
        };

        // The chosen apps open now that can be closed gently, with this game. Off the UI thread.
        public static Func<Game, List<HeavyApp>> Running = g => Propose(g, Chosen(Prefs.Now), Closable).Select(proc =>
        {
            Process[] ps = Get(proc);
            HeavyApp a = new HeavyApp { Proc = proc, Name = NameOf(proc) };
            foreach (Process p in ps)
            {
                try { a.Mb += p.WorkingSet64 / (1 << 20); if (a.Exe == null) a.Exe = p.MainModule.FileName; }
                catch (Win32Exception) { }
                catch (InvalidOperationException) { }
                p.Dispose();
            }
            string[] k = Known.FirstOrDefault(x => x[0] == proc);
            if (k != null && (a.Exe == null || proc == "ms-teams")) a.Exe = Where(k); // Store apps open again through their alias
            return a;
        }).ToList();

        // Closes gently, all at once, and waits up to 5 s. Returns the ones that closed.
        public static Func<List<HeavyApp>, List<HeavyApp>> Close = apps =>
        {
            foreach (HeavyApp a in apps)
            {
                string quit = QuitOf(a.Proc);
                if (quit != "" && a.Exe != null) { try { Process.Start(new ProcessStartInfo(a.Exe, quit) { UseShellExecute = false }).Dispose(); } catch (Win32Exception) { } continue; }
                foreach (Process p in Get(a.Proc)) using (p) { try { p.CloseMainWindow(); } catch (InvalidOperationException) { } }
            }
            DateTime end = DateTime.UtcNow.AddSeconds(5);
            List<HeavyApp> left = apps.ToList();
            while (left.Count > 0 && DateTime.UtcNow < end)
            {
                Thread.Sleep(250);
                left.RemoveAll(a => { Process[] ps = Get(a.Proc); foreach (Process p in ps) p.Dispose(); return ps.Length == 0; });
            }
            return apps.Where(a => !left.Contains(a)).ToList();
        };

        // Without arguments, and without the app's administrator rights (through Explorer). Chrome restores its own session.
        public static Action<List<HeavyApp>> Reopen = apps =>
        {
            foreach (HeavyApp a in apps.Where(x => x.Exe != null)) { try { StretchSession.Open(a.Exe); } catch (Win32Exception) { } }
        };
    }

    // The question, in a small window at the bottom right of the main screen. Never takes the focus from the game.
    public class HeavyAsk : Window
    {
        const int ExStyle = -20, ToolWindow = 0x80, NoActivate = 0x8000000;
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        readonly DispatcherTimer timer = new DispatcherTimer();

        // done(yes, remember) on an answer; nothing when it closes by itself. Without done: only a notice.
        public HeavyAsk(Window main, string text, bool remember, Action<bool, bool> done)
        {
            Resources.MergedDictionaries.Add(main.Resources);
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
            Topmost = true; ShowInTaskbar = false; ShowActivated = false; SizeToContent = SizeToContent.WidthAndHeight; Title = "Tweakeroo";
            FontFamily = main.FontFamily; FontSize = main.FontSize; UseLayoutRounding = true;
            StackPanel s = new StackPanel();
            TextBlock t = new Tb { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, done == null ? 0 : 12) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            s.Children.Add(t);
            CheckBox keep = null;
            if (done != null)
            {
                if (remember) { keep = new CheckBox { Style = (Style)main.Resources["Check"], Content = L.T("Recordar mi elección"), Margin = new Thickness(0, -6, 0, 6) }; s.Children.Add(keep); }
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
                Btn yes = new Btn { Style = (Style)main.Resources["BtnCta"], Content = "Sí", Margin = new Thickness(0, 0, 10, 0) };
                Btn no = new Btn { Style = ((Btn)main.FindName("CheckNow")).Style /* the theme reuses the key "Btn" for a brush */, Content = "No" };
                yes.Click += (o, e) => { Close(); done(true, keep != null && keep.IsChecked == true); };
                no.Click += (o, e) => { Close(); done(false, keep != null && keep.IsChecked == true); };
                row.Children.Add(yes); row.Children.Add(no);
                s.Children.Add(row);
            }
            Border b = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(18, 14, 18, 14), Width = 380, Child = s };
            b.SetResourceReference(Border.BackgroundProperty, "Card");
            b.SetResourceReference(Border.BorderBrushProperty, "Line");
            b.BorderThickness = new Thickness(1);
            Content = b;
            // Unanswered in 30 s: closes doing nothing. A notice lasts 6 s.
            timer.Interval = TimeSpan.FromSeconds(done == null ? 6 : 30);
            timer.Tick += (o, e) => Close();
            Closed += (o, e) => timer.Stop();
            SourceInitialized += (o, e) => { IntPtr h = new WindowInteropHelper(this).Handle; SetWindowLong(h, ExStyle, GetWindowLong(h, ExStyle) | ToolWindow | NoActivate); };
            Loaded += (o, e) => { Rect wa = SystemParameters.WorkArea; Left = wa.Right - ActualWidth - 16; Top = wa.Bottom - ActualHeight - 16; timer.Start(); };
#if SHOT
            Loaded += (o, e) => Left = -4000; // test captures stay off screen
#endif
        }
    }

    partial class Ui
    {
        Game heavyGame;
        bool heavySeen;
        List<HeavyApp> heavyClosed = new List<HeavyApp>();
        HeavyAsk heavyAsk;

        void StartHeavy()
        {
            Toggle("SetHeavy", () => Prefs.Now.HeavyOn, v => { Prefs.Now.HeavyOn = v; HeavyShow(); });
            F<Btn>("SetHeavyAsk").Click += (s, e) => { Prefs.Now.HeavyKeep = 0; SavePrefs(); HeavyShow(); };
            refresh.Add(HeavyShow);
            F<FrameworkElement>("SettingsPage").IsVisibleChanged += (s, e) => { if ((bool)e.NewValue) HeavyList(); };
            HeavyShow();
        }
        void HeavyShow()
        {
            F<FrameworkElement>("SetHeavyBox").Visibility = Prefs.Now.HeavyOn ? Visibility.Visible : Visibility.Collapsed;
            string keep = Heavy.KeepText(Prefs.Now.HeavyKeep);
            F<FrameworkElement>("SetHeavyKeep").Visibility = keep == null ? Visibility.Collapsed : Visibility.Visible;
            F<TextBlock>("SetHeavyKeepText").Text = keep ?? "";
        }
        async void HeavyList()
        {
            List<KeyValuePair<string, string>> offer = null;
            await Task.Run(() => offer = Heavy.Offer());
            WrapPanel p = F<WrapPanel>("SetHeavyApps");
            p.Children.Clear();
            List<string> chosen = Heavy.Chosen(Prefs.Now);
            foreach (KeyValuePair<string, string> o in offer)
            {
                string proc = o.Key;
                CheckBox c = new CheckBox { Style = (Style)w.Resources["Check"], Content = o.Value, IsChecked = chosen.Contains(proc, StringComparer.OrdinalIgnoreCase), Margin = new Thickness(0, 0, 22, 0) };
                AutomationProperties.SetName(c, o.Value);
                c.Click += (s, e) =>
                {
                    List<string> now = Heavy.Chosen(Prefs.Now).Where(x => !x.Equals(proc, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (c.IsChecked == true) now.Add(proc);
                    Prefs.Now.HeavyApps = now;
                    SavePrefs();
                };
                p.Children.Add(c);
            }
            if (offer.Count == 0) p.Children.Add(new Tb { Text = "Ninguna encontrada en este PC.", Margin = new Thickness(0, 8, 0, 8) });
        }

        // From the game check, every 2 s.
        async void HeavyTick(Game open)
        {
            bool first = !heavySeen;
            heavySeen = true;
            if (open == heavyGame) return;
            Game was = heavyGame;
            heavyGame = open;
            if (first) return; // a game already open when the app starts: nothing is asked
            if (was != null && heavyClosed.Count > 0)
            {
                List<HeavyApp> back = heavyClosed;
                heavyClosed = new List<HeavyApp>();
                HeavyPop(Heavy.ReopenText(back), false, (yes, r) => { if (yes) Heavy.Reopen(back); });
            }
            if (open == null || !Prefs.Now.HeavyOn || Prefs.Now.HeavyKeep == 2) return;
            List<HeavyApp> apps = null;
            await Task.Run(() => apps = Heavy.Running(open));
            if (apps.Count == 0 || heavyGame != open) return;
            if (Prefs.Now.HeavyKeep == 1) { HeavyClose(apps, true); return; }
            HeavyPop(Heavy.AskText(apps), true, (yes, remember) =>
            {
                if (remember) { Heavy.Remember(Prefs.Now, yes); SavePrefs(); HeavyShow(); }
                if (yes) HeavyClose(apps, false);
            });
        }
        async void HeavyClose(List<HeavyApp> apps, bool auto)
        {
            List<HeavyApp> done = null;
            await Task.Run(() => done = Heavy.Close(apps));
            heavyClosed.AddRange(done);
            string msg = Heavy.Result(apps, done, auto);
            if (msg != null) HeavyPop(msg, false, null);
        }
        // One at a time: a new one replaces the one on screen.
        internal HeavyAsk HeavyPop(string text, bool remember, Action<bool, bool> done)
        {
            if (heavyAsk != null) heavyAsk.Close();
            heavyAsk = new HeavyAsk(w, text, remember, done);
            heavyAsk.Show();
            return heavyAsk;
        }
    }

    // Self-test: what is offered, remembering and the texts. No real process is listed or closed.
    static class HeavyTest
    {
        static void Is(bool ok, string what) { if (!ok) throw new Exception("FALLA: " + what); }
        public static void Run()
        {
            List<string> all = new List<string> { "chrome", "steam", "EpicGamesLauncher", "Spotify", "Discord" };
            Func<string, bool> open = x => x != "Spotify";
            List<string> cs = Heavy.Propose(Game.Cs2, all, open), fn = Heavy.Propose(Game.Fortnite, all, open), va = Heavy.Propose(Game.Valorant, all, open);
            Is(!cs.Contains("steam") && cs.Contains("EpicGamesLauncher") && cs.Contains("chrome") && !cs.Contains("Spotify"), "pesadas: Steam no con CS2");
            Is(!fn.Contains("EpicGamesLauncher") && fn.Contains("steam") && va.Contains("steam") && va.Contains("EpicGamesLauncher"), "pesadas: Epic no con Fortnite");
            Is(!Heavy.Defaults.Contains("Discord") && Heavy.Defaults.Contains("chrome") && Heavy.Chosen(new Prefs()).SequenceEqual(Heavy.Defaults), "pesadas: Discord sin marcar por defecto");

            Prefs p = Prefs.Default();
            Is(!p.HeavyOn && p.HeavyKeep == 0, "pesadas: apagado y preguntando por defecto");
            Heavy.Remember(p, true);
            Is(p.HeavyKeep == 1 && Prefs.Parse(p.Json()).HeavyKeep == 1, "pesadas: recordar sí");
            Heavy.Remember(p, false);
            Is(p.HeavyKeep == 2 && Prefs.Parse(p.Json()).HeavyKeep == 2, "pesadas: recordar no");
            p.HeavyKeep = 7; p.HeavyApps = new List<string> { "chrome", "chrome", "" };
            Prefs q = Prefs.Parse(p.Json());
            Is(q.HeavyKeep == 0 && q.HeavyApps.SequenceEqual(new[] { "chrome" }), "pesadas: ajustes raros se limpian");

            bool en = L.En;
            try
            {
                L.En = false;
                List<HeavyApp> two = new List<HeavyApp> { new HeavyApp { Name = "Chrome", Mb = 1500 }, new HeavyApp { Name = "Spotify", Mb = 650 } };
                Is(Heavy.AskText(two) == "¿Cerrar Chrome, Spotify? Liberan 2,1 GB.", "pesadas: texto con GB " + Heavy.AskText(two));
                Is(Heavy.AskText(two.Take(1).ToList()) == "¿Cerrar Chrome? Libera 1,5 GB." && Heavy.Size(800) == "800 MB", "pesadas: una sola y MB");
                List<HeavyApp> four = two.Concat(new[] { new HeavyApp { Name = "Edge", Mb = 10 }, new HeavyApp { Name = "Teams", Mb = 10 } }).ToList();
                Is(Heavy.ReopenText(four) == "¿Volver a abrir Chrome, Spotify, Edge…?", "pesadas: más de tres");
                Is(Heavy.Result(two, two, false) == null && Heavy.Result(two, two.Take(1).ToList(), true) == "Cerrado sin preguntar: Chrome. Siguen abiertos: Spotify.", "pesadas: resultado");
                L.En = true;
                Is(L.T(Heavy.AskText(two)) == "Close Chrome, Spotify? Frees 2.1 GB.", "pesadas: en inglés " + L.T(Heavy.AskText(two)));
            }
            finally { L.En = en; }
        }
    }
}
