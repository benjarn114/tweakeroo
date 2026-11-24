using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tablero
{
    // Background music and sound effects. Tests replace Play and Music with functions that only note the calls.
    public static class Sound
    {
        public static readonly string[] Themes = { "noche", "arcade", "lluvia" };
        // The WAV files come already leveled; this is each effect's weight against the others.
        static readonly Dictionary<string, double> Gain = new Dictionary<string, double> { { "click", 1 }, { "hover", 0.5 }, { "switch", 0.5 }, { "toggle-on", 0.6 }, { "toggle-off", 0.6 }, { "scan", 0.7 }, { "done", 1 }, { "error", 1 } };
#if SHOT
        public static Action<string> Play = id => { };
        public static Action<string, double> Music = (id, volume) => { };
#else
        public static Action<string> Play = PlayNow;
        public static Action<string, double> Music = MusicNow;
#endif
        static string folder;
        static bool pending; // a Btn was pressed: its click waits so a more specific sound can replace it
        static bool started;

        // An effect, if the settings allow it. A specific sound during a button press replaces the click.
        public static void Effect(string id)
        {
            if (!Prefs.Now.Sfx) return;
            if (id == "click") { if (pending) return; pending = true; Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { if (pending) Play("click"); pending = false; })); return; }
            pending = false;
            Play(id);
        }

        public static void Start(string dir)
        {
            folder = Path.Combine(dir, "sonidos");
            if (started) return;
            started = true;
            EventManager.RegisterClassHandler(typeof(Btn), ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                Effect("click");
            }));
            // Click only comes from the person (mouse, keyboard, accessibility), never from code setting IsChecked.
            EventManager.RegisterClassHandler(typeof(CheckBox), ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                CheckBox c = (CheckBox)s;
                if (c.Style != null && c.Style == c.TryFindResource("Check")) Effect(c.IsChecked == true ? "toggle-on" : "toggle-off");
            }));
        }

        // Copies the sounds to the data folder, only those missing or changed.
        public static void Extract(string dir)
        {
            string to = Path.Combine(dir, "sonidos");
            Assembly me = Assembly.GetExecutingAssembly();
            foreach (string n in me.GetManifestResourceNames().Where(x => x.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using (Stream s = me.GetManifestResourceStream(n))
                    {
                        string f = Path.Combine(to, n);
                        if (File.Exists(f) && new FileInfo(f).Length == s.Length) continue;
                        Directory.CreateDirectory(to);
                        // Written aside first so a half-written file is never played.
                        using (FileStream o = File.Create(f + ".tmp")) s.CopyTo(o);
                        if (File.Exists(f)) File.Delete(f);
                        File.Move(f + ".tmp", f);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        static string FileOf(string name)
        {
            string f = folder == null ? null : Path.Combine(folder, name + ".wav");
            return f != null && File.Exists(f) ? f : null;
        }

        // ---- Effects: a player per sound, closed when it ends ----
        static readonly HashSet<MediaPlayer> live = new HashSet<MediaPlayer>();
        static void PlayNow(string id)
        {
            double g;
            string f = FileOf("sfx-" + id);
            if (!Gain.TryGetValue(id, out g) || f == null || live.Count >= 8) return;
            MediaPlayer p = new MediaPlayer { Volume = g * Prefs.Now.SfxVolume / 100.0 };
            Action done = () => { p.Close(); live.Remove(p); };
            p.MediaEnded += (s, e) => done();
            p.MediaFailed += (s, e) => done();
            live.Add(p);
            p.Open(new Uri(f));
            p.Play();
        }

        // ---- Music: one player, loops, and fades in and out in 400 ms ----
        const int FadeMs = 400, TickMs = 20;
        static MediaPlayer music;
        static string playing = "", want = "";
        static double level;
        static bool held; // music is paused at its position, volume 0
        static DispatcherTimer fade;
        static void MusicNow(string id, double volume)
        {
            id = id ?? "";
            if (id != want && id != "" && FileOf("musica-" + id) == null) id = ""; // missing file: silence; asked again later
            if (id == want && volume == level) return;
            want = id;
            level = volume;
            if (fade == null)
            {
                fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
                fade.Tick += (s, e) => Fade();
            }
            fade.Start();
        }
        static void Fade()
        {
            double step = Math.Max(level, 0.05) * TickMs / FadeMs;
            if (music != null && playing != want)
            {
                if (!held)
                {
                    music.Volume = Math.Max(0, music.Volume - step);
                    if (music.Volume > 0) return;
                }
                // Silence (app in the background, music off) only pauses, so the track resumes where it was.
                if (want == "") { if (!held) music.Pause(); held = true; fade.Stop(); return; }
                music.Close();
                music = null;
                playing = "";
                held = false;
                return;
            }
            if (music == null)
            {
                string f = want == "" ? null : FileOf("musica-" + want);
                if (f == null) { fade.Stop(); return; }
                MediaPlayer p = new MediaPlayer { Volume = 0 };
                p.MediaEnded += (s, e) => { p.Position = TimeSpan.Zero; p.Play(); };
                p.Open(new Uri(f));
                p.Play();
                music = p;
                playing = want;
                return;
            }
            if (held) { music.Play(); held = false; }
            double v = music.Volume;
            music.Volume = v < level ? Math.Min(level, v + step) : Math.Max(level, v - step);
            if (Math.Abs(music.Volume - level) < 0.001) fade.Stop();
        }
    }
}
