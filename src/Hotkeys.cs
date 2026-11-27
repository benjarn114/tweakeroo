// Global shortcuts with RegisterHotKey on a message-only window: no keyboard hook (Macros.cs keeps its own, apart from this).
// Use from the UI thread; the actions run on it too. The texts ("Ctrl+Shift+O") are the same in both languages.
// Saved in Prefs: OverlayKey (Ctrl+Shift+O, wired by the overlay) and MarkKey (Ctrl+Shift+M: Tracker holds it as "marca" while a game is open).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Tablero
{
    public static class Hotkeys
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
        const int WmHotkey = 0x312;
        const uint NoRepeat = 0x4000;
        static HwndSource win;
        static int next;
        static readonly Dictionary<string, int> ids = new Dictionary<string, int>();
        static readonly Dictionary<int, Action> acts = new Dictionary<int, Action>();

        // Replaces the shortcut of id. false if the key cannot be a shortcut or another program already holds the combination.
        public static bool Set(string id, ModifierKeys mods, Key key, Action act)
        {
            Clear(id);
            if (!Usable(key)) return false;
            if (win == null)
            {
                win = new HwndSource(new HwndSourceParameters("Tweakeroo-atajos") { ParentWindow = new IntPtr(-3), WindowStyle = 0 }); // HWND_MESSAGE
                win.AddHook(Hook);
            }
            int n = ++next;
            // ModifierKeys has the values of MOD_ALT, MOD_CONTROL, MOD_SHIFT and MOD_WIN.
            if (!RegisterHotKey(win.Handle, n, (uint)mods | NoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key))) return false;
            ids[id] = n;
            acts[n] = act;
            return true;
        }

        public static void Clear(string id)
        {
            int n;
            if (!ids.TryGetValue(id, out n)) return;
            UnregisterHotKey(win.Handle, n);
            ids.Remove(id);
            acts.Remove(n);
        }

        // From its text, as Prefs keeps it. "" = none: only clears it, and returns true.
        public static bool Bind(string id, string text, Action act)
        {
            ModifierKeys m;
            Key k;
            if (Parse(text, out m, out k)) return Set(id, m, k, act);
            Clear(id);
            return text == "";
        }

        // "Ctrl+Shift+O": modifiers in this order, digits without their D.
        public static string Text(ModifierKeys mods, Key key)
        {
            string s = "";
            if ((mods & ModifierKeys.Control) != 0) s += "Ctrl+";
            if ((mods & ModifierKeys.Shift) != 0) s += "Shift+";
            if ((mods & ModifierKeys.Alt) != 0) s += "Alt+";
            if ((mods & ModifierKeys.Windows) != 0) s += "Win+";
            return s + (key >= Key.D0 && key <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString());
        }

        // Back from Text (any case, spaces allowed). false if a part is not understood or the key is only a modifier.
        public static bool Parse(string text, out ModifierKeys mods, out Key key)
        {
            mods = ModifierKeys.None;
            key = Key.None;
            string[] p = (text ?? "").Split('+').Select(x => x.Trim()).ToArray();
            for (int i = 0; i < p.Length - 1; i++)
                switch (p[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                    case "shift": mods |= ModifierKeys.Shift; break;
                    case "alt": mods |= ModifierKeys.Alt; break;
                    case "win": case "windows": mods |= ModifierKeys.Windows; break;
                    default: return false;
                }
            string k = p[p.Length - 1];
            if (k.Length == 1 && char.IsDigit(k[0])) key = Key.D0 + (k[0] - '0');
            // A number is not a key name (Enum.TryParse would take it as a value).
            else if (k.Length == 0 || char.IsDigit(k[0]) || k[0] == '-' || !Enum.TryParse(k, true, out key) || !Enum.IsDefined(typeof(Key), key)) { key = Key.None; return false; }
            return Usable(key);
        }

        // For Prefs: written the standard way; "" (none) stays; anything not understood, or missing, gives def.
        public static string Clean(string text, string def)
        {
            ModifierKeys m;
            Key k;
            return text == "" ? "" : Parse(text, out m, out k) ? Text(m, k) : def;
        }

        static bool Usable(Key k)
        {
            return k != Key.None && !(k >= Key.LeftShift && k <= Key.RightAlt) && k != Key.LWin && k != Key.RWin && k != Key.System && KeyInterop.VirtualKeyFromKey(k) != 0;
        }

        static IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            Action a;
            if (msg == WmHotkey && acts.TryGetValue(w.ToInt32(), out a))
            {
                handled = true;
                try { a(); } catch (Exception) { } // a shortcut must never break the app
            }
            return IntPtr.Zero;
        }
    }
}
