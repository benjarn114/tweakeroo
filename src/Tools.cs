using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tablero
{
    // Tools: eDPI, mouse test (raw input, only while a test runs) and reaction time.
    static class ToolMath
    {
        public static readonly string[] Games = { "Fortnite", "Valorant", "CS2" };
        // Approximate range of pro players' eDPI, as an orientation only.
        public static readonly int[][] ProRange = { new[] { 35, 65 }, new[] { 200, 400 }, new[] { 600, 1200 } };
        static readonly int[] Rates = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        // Fortnite shows the sensitivity as a percentage.
        public static double Edpi(string game, double sens, int dpi) { return game == "Fortnite" ? dpi * sens / 100 : dpi * sens; }
        // -1 below, 0 inside, 1 above.
        public static int Where(string game, double edpi)
        {
            int[] r = ProRange[Array.IndexOf(Games, game)];
            return edpi < r[0] ? -1 : edpi > r[1] ? 1 : 0;
        }
        // Nearest standard rate, by ratio (750 is as far from 500 as from 1000).
        public static int StdRate(double hz) { return Rates.OrderBy(r => Math.Abs(Math.Log(hz / r))).First(); }
        // Most events in any 0.5 s window, per second: a single burst barely moves it.
        public static double PollRate(List<double> secs)
        {
            int best = 0;
            for (int i = 0, j = 0; i < secs.Count; i++)
            {
                while (secs[i] - secs[j] >= 0.5) j++;
                best = Math.Max(best, i - j + 1);
            }
            return best / 0.5;
        }
        public static double Dpi(int counts, double cm) { return Math.Abs(counts) / (cm / 2.54); }
        public static bool Num(string s, out double v) { return double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0; }
    }

    partial class Ui
    {
        // Raw input: registered on the window only while a mouse test runs.
        [StructLayout(LayoutKind.Sequential)] struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(RawDevice[] d, uint n, uint size);
        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr raw, uint cmd, byte[] data, ref uint size, uint header);
        const int WmInput = 0x00FF;
        HwndSource rawSrc;
        Action<int, int> rawMove;
        readonly byte[] rawBuf = new byte[64];

        bool RawStart(Action<int, int> move)
        {
            RawStop();
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (!RegisterRawInputDevices(new[] { new RawDevice { Page = 1, Usage = 2, Target = h } }, 1, (uint)Marshal.SizeOf(typeof(RawDevice)))) return false;
            rawMove = move;
            rawSrc = HwndSource.FromHwnd(h);
            rawSrc.AddHook(RawHook);
            return true;
        }
        void RawStop()
        {
            if (rawSrc == null) return;
            rawSrc.RemoveHook(RawHook);
            rawSrc = null; rawMove = null;
            RegisterRawInputDevices(new[] { new RawDevice { Page = 1, Usage = 2, Flags = 0x1 /* RIDEV_REMOVE */ } }, 1, (uint)Marshal.SizeOf(typeof(RawDevice)));
        }
        IntPtr RawHook(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
        {
            if (msg != WmInput || rawMove == null) return IntPtr.Zero;
            uint head = (uint)(8 + 2 * IntPtr.Size), size = (uint)rawBuf.Length;
            if (GetRawInputData(lp, 0x10000003 /* RID_INPUT */, rawBuf, ref size, head) == uint.MaxValue || BitConverter.ToUInt32(rawBuf, 0) != 0 /* mouse */) return IntPtr.Zero;
            int o = (int)head;
            if ((BitConverter.ToUInt16(rawBuf, o) & 1) != 0) return IntPtr.Zero; // absolute (tablets, remote desktop)
            int x = BitConverter.ToInt32(rawBuf, o + 12), y = BitConverter.ToInt32(rawBuf, o + 16);
            if (x != 0 || y != 0) rawMove(x, y);
            return IntPtr.Zero;
        }

        int reactPhase;  // phase: 0 idle, 1 waiting, 2 color shown, 3 result, 4 too soon
        long reactAt;
        readonly List<double> reactTimes = new List<double>();
        DispatcherTimer reactWait;
        readonly Random reactRnd = new Random();

        void StartTools()
        {
            if (Prefs.Now.ToolGame == null) Prefs.Now.ToolGame = ToolMath.Games.FirstOrDefault(g => g.ToLowerInvariant() == Prefs.Now.Game) ?? "Fortnite";
            ListBox games = F<ListBox>("EdpiGames");
            games.SelectedIndex = Math.Max(0, Array.IndexOf(ToolMath.Games, Prefs.Now.ToolGame));
            F<Drop>("EdpiGame").Content = games.SelectedItem is ListBoxItem ? ((ListBoxItem)games.SelectedItem).Content : null;
            games.SelectionChanged += (s, e) => { F<Drop>("EdpiGame").Content = ((ListBoxItem)games.SelectedItem).Content; F<Drop>("EdpiGame").Close(); Prefs.Now.ToolGame = ToolMath.Games[games.SelectedIndex]; EdpiShow(true); };
            TextBox sens = F<TextBox>("EdpiSens"), dpi = F<TextBox>("EdpiDpi");
            if (Prefs.Now.ToolSens > 0) sens.Text = Prefs.Now.ToolSens.ToString(CultureInfo.CurrentCulture);
            dpi.Text = (Prefs.Now.ToolDpi > 0 ? Prefs.Now.ToolDpi : 800).ToString();
            F<TextBox>("DpiSaid").Text = dpi.Text;
            sens.TextChanged += (s, e) => EdpiShow(true);
            dpi.TextChanged += (s, e) => EdpiShow(true);
            EdpiShow(false);

            F<Btn>("PollRun").Click += (s, e) => Poll();
            Border pad = F<Border>("DpiPad");
            int counts = 0;
            pad.MouseLeftButtonDown += (s, e) =>
            {
                counts = 0;
                if (!RawStart((x, y) => counts += x)) { F<TextBlock>("DpiResult").Text = "Windows no dio acceso al ratón."; return; }
                pad.CaptureMouse();
                F<TextBlock>("DpiPadText").Text = "Arrastra la distancia y suelta";
                e.Handled = true;
            };
            pad.MouseLeftButtonUp += (s, e) => { if (pad.IsMouseCaptured) pad.ReleaseMouseCapture(); };
            pad.LostMouseCapture += (s, e) =>
            {
                if (rawSrc == null) return;
                RawStop();
                F<TextBlock>("DpiPadText").Text = "Mantén pulsado aquí y arrastra";
                DpiShow(counts);
            };

            Border react = F<Border>("ReactPad");
            reactWait = new DispatcherTimer();
            reactWait.Tick += (s, e) => { reactWait.Stop(); ReactGo(); };
            react.MouseLeftButtonDown += (s, e) => ReactClick();
            ReactBestShow(false);
        }

        void EdpiShow(bool save)
        {
            string g = ToolMath.Games[Math.Max(0, F<ListBox>("EdpiGames").SelectedIndex)];
            F<TextBlock>("EdpiSensLabel").Text = g == "Fortnite" ? "Sensibilidad (%)" : "Sensibilidad";
            int[] r = ToolMath.ProRange[Array.IndexOf(ToolMath.Games, g)];
            F<TextBlock>("EdpiRef").Text = "Referencia aproximada de jugadores profesionales: " + r[0] + "–" + r[1] + ".";
            double sens, dpi;
            bool ok = ToolMath.Num(F<TextBox>("EdpiSens").Text, out sens) & ToolMath.Num(F<TextBox>("EdpiDpi").Text, out dpi);
            if (save && ok) { Prefs.Now.ToolSens = sens; Prefs.Now.ToolDpi = (int)Math.Round(dpi); }
            if (save) SavePrefs();
            F<TextBlock>("EdpiValue").Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
            if (!ok) { F<TextBlock>("EdpiVerdict").Text = "Escribe la sensibilidad y el DPI."; return; }
            double v = ToolMath.Edpi(g, sens, (int)Math.Round(dpi));
            F<TextBlock>("EdpiValue").Text = v.ToString(v < 100 ? "0.#" : "0", CultureInfo.CurrentCulture);
            int where = ToolMath.Where(g, v);
            F<TextBlock>("EdpiVerdict").Text = where == 0 ? "Dentro del rango habitual" : where > 0 ? "Más alto que el rango habitual" : "Más bajo que el rango habitual";
        }

        void Poll()
        {
            Btn run = F<Btn>("PollRun");
            TextBlock res = F<TextBlock>("PollResult");
            List<double> at = new List<double>();
            Stopwatch sw = Stopwatch.StartNew();
            if (!RawStart((x, y) => at.Add(sw.Elapsed.TotalSeconds))) { res.Text = "Windows no dio acceso al ratón."; return; }
            run.IsEnabled = false;
            res.Text = "Midiendo… mueve el ratón en círculos rápidos";
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                RawStop();
                run.IsEnabled = true;
                double hz = ToolMath.PollRate(at);
                res.Text = hz < 100 ? "Poco movimiento. Prueba otra vez moviendo el ratón sin parar." : "Medido: " + Math.Round(hz) + " Hz (≈" + ToolMath.StdRate(hz) + " Hz)";
            };
            t.Start();
        }

        void DpiShow(int counts)
        {
            double cm, said;
            TextBlock res = F<TextBlock>("DpiResult"), diff = F<TextBlock>("DpiDiff");
            diff.Text = "";
            if (!ToolMath.Num(F<TextBox>("DpiCm").Text, out cm)) { res.Text = "Escribe la distancia en centímetros."; return; }
            if (Math.Abs(counts) < 50) { res.Text = "Muy poco movimiento. Arrastra toda la distancia en horizontal."; return; }
            double dpi = ToolMath.Dpi(counts, cm);
            res.Text = "DPI medido: " + Math.Round(dpi);
            if (ToolMath.Num(F<TextBox>("DpiSaid").Text, out said))
            {
                double d = (dpi - said) / said * 100;
                diff.Text = Math.Abs(d) < 0.5 ? "Igual que el declarado" : (d > 0 ? "+" : "−") + Math.Abs(d).ToString("0.#", CultureInfo.CurrentCulture) + " % frente al declarado";
            }
        }

        void ReactClick()
        {
            long now = Stopwatch.GetTimestamp();
            if (reactPhase == 1) { reactWait.Stop(); reactPhase = 4; ReactPadShow(false, "Demasiado pronto", "Clic para repetir el intento"); return; }
            if (reactPhase == 2)
            {
                if (reactAt == 0) return; // the color is not on screen yet
                double ms = (now - reactAt) * 1000.0 / Stopwatch.Frequency;
                reactTimes.Add(ms);
                reactPhase = 3;
                if (reactTimes.Count < 5) { ReactPadShow(false, Math.Round(ms) + " ms", "Intento " + reactTimes.Count + " de 5 · clic para seguir"); return; }
                int avg = (int)Math.Round(reactTimes.Average());
                reactTimes.Clear();
                bool best = Prefs.Now.ReactBest == 0 || avg < Prefs.Now.ReactBest;
                if (best) { Prefs.Now.ReactBest = avg; SavePrefs(); }
                ReactPadShow(false, "Media: " + avg + " ms", "Clic para otra serie");
                ReactBestShow(best);
                return;
            }
            // idle, result or too soon: next wait
            reactPhase = 1;
            ReactPadShow(false, "Espera al color…", "Intento " + (reactTimes.Count + 1) + " de 5");
            reactWait.Interval = TimeSpan.FromMilliseconds(1500 + reactRnd.Next(2500));
            reactWait.Start();
        }

        void ReactGo()
        {
            reactPhase = 2;
            reactAt = 0;
            ReactPadShow(true, "¡Clic!", "");
            // Time starts at the first frame drawn with the new color.
            EventHandler frame = null;
            frame = (s, e) => { CompositionTarget.Rendering -= frame; reactAt = Stopwatch.GetTimestamp(); };
            CompositionTarget.Rendering += frame;
        }

        void ReactPadShow(bool go, string main, string sub)
        {
            Border pad = F<Border>("ReactPad");
            pad.SetResourceReference(Border.BackgroundProperty, go ? "On" : "Rail");
            F<TextBlock>("ReactMain").SetResourceReference(TextBlock.ForegroundProperty, go ? "OnInk" : "Ink");
            F<TextBlock>("ReactMain").Text = main;
            F<TextBlock>("ReactSub").Text = sub;
            F<TextBlock>("ReactSub").Visibility = sub.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void ReactBestShow(bool fresh)
        {
            TextBlock t = F<TextBlock>("ReactBest");
            t.Text = Prefs.Now.ReactBest > 0 ? "Récord: " + Prefs.Now.ReactBest + " ms" : "Sin récord todavía";
            t.SetResourceReference(TextBlock.ForegroundProperty, fresh ? "Cta" : "Ink2");
            if (fresh) Fx.Pop(t, 0.6);
        }

        // Texts made in code, for the translation check.
        internal static IEnumerable<string> ToolWords()
        {
            return new[] { "Sensibilidad (%)", "Sensibilidad", "Referencia aproximada de jugadores profesionales: 35–65.", "Escribe la sensibilidad y el DPI.", "Dentro del rango habitual", "Más alto que el rango habitual", "Más bajo que el rango habitual",
                "Windows no dio acceso al ratón.", "Arrastra la distancia y suelta", "Mantén pulsado aquí y arrastra", "Midiendo… mueve el ratón en círculos rápidos", "Poco movimiento. Prueba otra vez moviendo el ratón sin parar.", "Medido: 987 Hz (≈1000 Hz)",
                "Escribe la distancia en centímetros.", "Muy poco movimiento. Arrastra toda la distancia en horizontal.", "DPI medido: 812", "Igual que el declarado", "+1,5 % frente al declarado", "−3 % frente al declarado",
                "Demasiado pronto", "Clic para repetir el intento", "214 ms", "Intento 2 de 5 · clic para seguir", "Media: 220 ms", "Clic para otra serie", "Espera al color…", "Intento 1 de 5", "¡Clic!", "Récord: 198 ms", "Sin récord todavía", "Clic para empezar" };
        }
    }
}
