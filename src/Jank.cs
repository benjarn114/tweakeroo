#if SHOT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tablero
{
    // "tirones N [raton]": N seconds with the background forced to move. Notes the time between drawn frames and,
    // for each gap over 150 ms, what the UI thread did meanwhile. "raton" posts a mouse move to the window as soon as
    // the previous one is read (at most one per ms), like a gaming mouse moving over it.
    static class Jank
    {
        sealed class Snap { public double At, Busy, Cpu; public int Timers, Moves, Msgs, Idle, Frame, Gc0, Gc1, Gc2; }
        sealed class Op { public double At, Ms; public string Name; }
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr t, out long made, out long ended, out long kernel, out long user);
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly FieldInfo opMethod = typeof(DispatcherOperation).GetField("_method", Hidden), opArgs = typeof(DispatcherOperation).GetField("_args", Hidden);
        static readonly FieldInfo timerTick = typeof(DispatcherTimer).GetField("Tick", Hidden);
        static int waiting, posted;
        static volatile bool stop;

        static double Cpu() { long a, b, k, u; GetThreadTimes(GetCurrentThread(), out a, out b, out k, out u); return (k + u) / 10000.0; }

        static string Describe(Delegate d)
        {
            if (d == null) return "?";
            DispatcherTimer t = d.Target as DispatcherTimer;
            if (t != null)
            {
                Delegate h = timerTick == null ? null : timerTick.GetValue(t) as Delegate;
                return "timer " + t.Interval.TotalMilliseconds + " ms " + (h == null ? "?" : string.Join("+", h.GetInvocationList().Select(x => x.Method.DeclaringType.Name + "." + x.Method.Name)));
            }
            // An await continuation: the async method's state machine names it.
            FieldInfo f = d.Target == null ? null : d.Target.GetType().GetField("m_stateMachine", Hidden);
            object sm = f == null ? null : f.GetValue(d.Target);
            return sm != null ? sm.GetType().FullName.Replace("Tablero.", "") : d.Method.DeclaringType.Name + "." + d.Method.Name;
        }
        static string Describe(DispatcherOperation op)
        {
            Delegate m = opMethod == null ? null : opMethod.GetValue(op) as Delegate, a = opArgs == null ? null : opArgs.GetValue(op) as Delegate;
            return op.Priority + " " + Describe(m) + (a != null ? " <" + Describe(a) + ">" : "");
        }

        public static async Task<string> Measure(Window w, Ui ui, int secs, bool mouse)
        {
            WriteableBitmap bmp = ((Image)w.FindName("Scene")).Source as WriteableBitmap;
            if (bmp == null) return "Sin fondo animado: falta \"tema lluvia\" (o noche, arcade).";
            FieldInfo sceneField = typeof(Ui).GetField("scene", Hidden);
            Func<int> frame = () => { Scene s = (Scene)sceneField.GetValue(ui); return s == null ? 0 : s.Frame; };
            // Bytes allocated per frame by this scene alone (the whole domain counts, so it is approximate).
            AppDomain.MonitoringIsEnabled = true;
            Scene probe = Scene.Make(Prefs.Now.Theme);
            probe.Skip(60);
            long before = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            for (int i = 0; i < 300; i++) probe.Step();
            long perFrame = (AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - before) / 300;

            Stopwatch clock = Stopwatch.StartNew();
            List<Snap> snaps = new List<Snap>(4096);
            List<Op> ops = new List<Op>();
            Dictionary<DispatcherOperation, double> open = new Dictionary<DispatcherOperation, double>();
            double busy = 0;
            int timers = 0, moves = 0, msgs = 0, idle = 0;
            EventHandler drawn = (s, e) => snaps.Add(new Snap { At = clock.Elapsed.TotalMilliseconds, Busy = busy, Cpu = Cpu(), Timers = timers, Moves = moves, Msgs = msgs, Idle = idle, Frame = frame(), Gc0 = GC.CollectionCount(0), Gc1 = GC.CollectionCount(1), Gc2 = GC.CollectionCount(2) });
            // Only top-level operations: nested ones are inside their parent's time.
            DispatcherHookEventHandler started = (s, e) => { if (open.Count == 0) open[e.Operation] = clock.Elapsed.TotalMilliseconds; };
            DispatcherHookEventHandler done = (s, e) =>
            {
                double t0;
                if (!open.TryGetValue(e.Operation, out t0)) return;
                open.Remove(e.Operation);
                double ms = clock.Elapsed.TotalMilliseconds - t0;
                busy += ms;
                if (ms >= 3) ops.Add(new Op { At = t0, Ms = ms, Name = Describe(e.Operation) });
            };
            EventHandler inactive = (s, e) => idle++;
            ThreadMessageEventHandler pre = (ref MSG m, ref bool handled) =>
            {
                msgs++;
                if (m.message == 0x113) timers++;
                else if (m.message == 0x200) { moves++; Interlocked.Exchange(ref waiting, 0); }
            };
            Dispatcher d = w.Dispatcher;
            d.Hooks.OperationStarted += started; d.Hooks.OperationCompleted += done; d.Hooks.DispatcherInactive += inactive;
            ComponentDispatcher.ThreadPreprocessMessage += pre;
            bmp.Changed += drawn;
            stop = false; waiting = 0; posted = 0;
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            Thread mover = new Thread(() =>
            {
                Stopwatch pace = Stopwatch.StartNew();
                long next = 0;
                for (int i = 0; !stop; Thread.Yield())
                {
                    if (pace.ElapsedTicks < next || Interlocked.CompareExchange(ref waiting, 1, 0) != 0) continue;
                    next = pace.ElapsedTicks + Stopwatch.Frequency / 1000;
                    int x = 300 + (i * 7) % 600, y = 300 + (i * 3) % 300;
                    i++;
                    PostMessage(hwnd, 0x200, IntPtr.Zero, (IntPtr)((y << 16) | x));
                    posted++;
                }
            }) { IsBackground = true };
            if (mouse) mover.Start();

            bool was = Ui.Always;
            Ui.Always = true; ui.SceneNow();
            await Task.Delay(secs * 1000);
            Ui.Always = was; ui.SceneNow();
            stop = true;
            bmp.Changed -= drawn;
            ComponentDispatcher.ThreadPreprocessMessage -= pre;
            d.Hooks.OperationStarted -= started; d.Hooks.OperationCompleted -= done; d.Hooks.DispatcherInactive -= inactive;

            StringBuilder r = new StringBuilder();
            if (snaps.Count < 2) return "Sin fotogramas medidos (" + snaps.Count + ").";
            List<double> gaps = new List<double>();
            for (int i = 1; i < snaps.Count; i++) gaps.Add(snaps[i].At - snaps[i - 1].At);
            List<double> sorted = gaps.OrderBy(x => x).ToList();
            Func<double, double> pc = p => sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
            Snap first = snaps[0], last = snaps[snaps.Count - 1];
            double span = last.At - first.At;
            r.AppendLine("Temática " + Prefs.Now.Theme + ", " + secs + " s" + (mouse ? ", ratón simulado (" + posted + " movimientos, " + (posted / (double)secs).ToString("0") + "/s)" : "") + ".");
            r.AppendLine("Fotogramas dibujados: " + snaps.Count + " (" + (snaps.Count / (span / 1000)).ToString("0.0") + " por segundo).");
            r.AppendLine("Intervalo entre fotogramas, ms: p50 " + pc(0.5).ToString("0.0") + ", p90 " + pc(0.9).ToString("0.0") + ", p99 " + pc(0.99).ToString("0.0") + ", máximo " + sorted[sorted.Count - 1].ToString("0.0") + ".");
            r.AppendLine("Saltos > 150 ms: " + gaps.Count(g => g > 150) + "; > 100 ms: " + gaps.Count(g => g > 100) + ".");
            r.AppendLine("Pasos de la escena: " + (last.Frame - first.Frame) + " en " + (span / 1000).ToString("0.0") + " s (a 15 por segundo serían " + (span / (1000 / 15.0)).ToString("0") + ").");
            r.AppendLine("Hilo de la interfaz: CPU " + (last.Cpu - first.Cpu).ToString("0") + " ms, en operaciones del dispatcher " + (last.Busy - first.Busy).ToString("0") + " ms. WM_TIMER: " + (last.Timers - first.Timers) + ". GC: " + (last.Gc0 - first.Gc0) + "/" + (last.Gc1 - first.Gc1) + "/" + (last.Gc2 - first.Gc2) + " (gen 0/1/2). Asignado por paso de escena: ~" + perFrame + " bytes.");
            r.AppendLine();
            r.AppendLine("Lo más caro en el hilo de la interfaz (operaciones de 3 ms o más):");
            foreach (var g in ops.GroupBy(o => o.Name).OrderByDescending(g => g.Sum(o => o.Ms)).Take(10))
                r.AppendLine("  " + g.Sum(o => o.Ms).ToString("0").PadLeft(6) + " ms en " + g.Count() + " veces, máx " + g.Max(o => o.Ms).ToString("0") + " ms: " + g.Key);
            r.AppendLine();
            r.AppendLine("Saltos de más de 150 ms (segundo, hueco, CPU del hilo, en operaciones, WM_TIMER, movimientos, mensajes, inactivo, GC 0/1/2, pasos):");
            int listed = 0;
            for (int i = 1; i < snaps.Count && listed < 40; i++)
            {
                Snap a = snaps[i - 1], b = snaps[i];
                if (b.At - a.At <= 150) continue;
                listed++;
                string top = string.Join("; ", ops.Where(o => o.At >= a.At && o.At < b.At).OrderByDescending(o => o.Ms).Take(4).Select(o => o.Ms.ToString("0") + " ms " + o.Name));
                r.AppendLine("  " + (a.At / 1000).ToString("0.0").PadLeft(5) + " s  " + (b.At - a.At).ToString("0").PadLeft(5) + " ms  cpu " + (b.Cpu - a.Cpu).ToString("0") + "  ops " + (b.Busy - a.Busy).ToString("0") + "  timer " + (b.Timers - a.Timers) + "  mov " + (b.Moves - a.Moves) + "  msg " + (b.Msgs - a.Msgs) + "  inact " + (b.Idle - a.Idle) + "  gc " + (b.Gc0 - a.Gc0) + "/" + (b.Gc1 - a.Gc1) + "/" + (b.Gc2 - a.Gc2) + "  pasos " + (b.Frame - a.Frame) + (top == "" ? "" : "  | " + top));
            }
            return r.ToString();
        }
    }
}
#endif
