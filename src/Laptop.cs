using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tablero
{
    // Laptop profile: on battery, Balanced; plugged in, the plan that was active before unplugging.
    // Listens to SystemEvents.PowerModeChanged, no polling.
    static class Laptop
    {
        public static readonly Guid Balanced = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        public static readonly Guid High = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static bool Force { get; set; } // SHOT: shows the option on a desktop PC

        public static bool Has()
        {
            if (Force) return true;
            try { return SystemInformation.PowerStatus.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery; }
            catch (Exception) { return false; }
        }

        // Pure decision. battery: true on battery, false plugged in, null unknown. saved: plan to go back to, Guid.Empty = none.
        // Returns the plan to activate (null = nothing) and the new saved plan.
        public static Guid? Decide(bool on, bool? battery, Guid active, Guid saved, ICollection<Guid> plans, out Guid keep)
        {
            keep = saved;
            if (!on || battery == null) return null;
            if (battery == true)
            {
                if (active == Balanced || !plans.Contains(Balanced)) return null;
                keep = active;
                return Balanced;
            }
            keep = Guid.Empty;
            if (saved == Guid.Empty || saved == active) return null;
            // Cortex and similar recreate their plan with a new GUID: if the saved one is gone, High performance.
            if (plans.Contains(saved)) return saved;
            return plans.Contains(High) && active != High ? High : (Guid?)null;
        }

        static bool? OnBattery()
        {
            try
            {
                PowerLineStatus s = SystemInformation.PowerStatus.PowerLineStatus;
                return s == PowerLineStatus.Offline ? true : s == PowerLineStatus.Online ? false : (bool?)null;
            }
            catch (Exception) { return null; }
        }

        // Applies the decision to the real plan. Returns the message for the toast, or null if nothing changed.
        public static string Apply(string dir)
        {
            bool? battery = OnBattery();
            Guid saved;
            if (!Guid.TryParse(Prefs.Now.LaptopPlan ?? "", out saved)) saved = Guid.Empty;
            try
            {
                Guid keep;
                Guid? to = Decide(Prefs.Now.LaptopMode, battery, Power.Active(), saved, Power.Schemes(), out keep);
                if (to != null) Power.Activate(to.Value);
                string k = keep == Guid.Empty ? null : keep.ToString();
                if (k != Prefs.Now.LaptopPlan) { Prefs.Now.LaptopPlan = k; try { Prefs.Save(dir); } catch (Exception) { } }
                if (to == null) return null;
                return battery == true ? "Con batería: plan de ahorro." : "Enchufado: plan de rendimiento.";
            }
            catch (Exception) { return null; } // a plan that cannot be read or set: nothing changes
        }

        static bool? last;
        static PowerModeChangedEventHandler handler;
        // say runs on the UI thread with the message when the plan changed by unplugging or plugging in.
        public static void Start(string dir, System.Windows.Threading.Dispatcher ui, Action<string> say)
        {
            if (handler != null || Force || !Has()) return; // the capture build never touches the real plan
            last = OnBattery();
            Apply(dir); // back from a session closed on battery: the saved plan comes back
            handler = (s, e) =>
            {
                if (e.Mode != PowerModes.StatusChange) return;
                bool? now = OnBattery();
                if (now == last) return; // battery percentage changes also arrive here
                last = now;
                ui.BeginInvoke(new Action(() => { string m = Apply(dir); if (m != null) say(m); }));
            };
            SystemEvents.PowerModeChanged += handler;
        }
        public static void Stop()
        {
            if (handler != null) SystemEvents.PowerModeChanged -= handler;
            handler = null;
        }
    }
}
