using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Input;

namespace Tablero
{
    public class MacroConfig
    {
        public bool Accepted { get; set; }         // accepted the risk warning
        public bool ConfirmOnRelease { get; set; } // same as in Fortnite settings
        public int Edit { get; set; }              // game keys, as Windows virtual key codes
        public int Select { get; set; }
        public int Reset { get; set; }
        public int Delay { get; set; }             // ms between actions
        public int Gap { get; set; }               // ms before the second action: the game takes a while to allow editing again
        public bool Manual { get; set; }           // keys set by hand instead of read from Fortnite
        public bool Hold { get; set; }             // hold to drag; otherwise one tap sends everything
        public Dictionary<string, int> Keys { get; set; } // key of each macro; 0 = no key
        public List<string> On { get; set; }
    }

    public struct Act { public int Vk; public bool Down; public Act(int vk, bool down) { Vk = vk; Down = down; } }

    // Edit macros. Only in Creative and with Fortnite in front; in any other mode they send nothing.
    public static class Macros
    {
        // The wheel has no Windows code: uses these, outside the key range.
        public const int WheelUp = 300, WheelDown = 301;
        public static bool Wheel(int vk) { return vk == WheelUp || vk == WheelDown; }
        public static readonly string[] Ids = { "edit", "doble", "reset", "editreset", "salir" };

        // ---- Saving ----
        public static MacroConfig Default() { return new MacroConfig { ConfirmOnRelease = true, Edit = 0x46, Select = 1, Reset = 2, Delay = 20, Gap = 80, Keys = new Dictionary<string, int>(), On = new List<string>() }; }
        static string FileOf(string dir) { return Path.Combine(dir, "macros.json"); }
        public static MacroConfig Load(string dir)
        {
            MacroConfig c = null;
            try { if (File.Exists(FileOf(dir))) c = new JavaScriptSerializer().Deserialize<MacroConfig>(File.ReadAllText(FileOf(dir))); }
            catch (Exception) { }
            return Clean(c);
        }
        // Invalid values are removed; if the essentials are missing, everything goes back to default.
        public static MacroConfig Clean(MacroConfig c)
        {
            if (c == null || c.Keys == null || c.On == null || !(Valid(c.Edit) || Wheel(c.Edit)) || !Valid(c.Select) || !(Valid(c.Reset) || Wheel(c.Reset))) return Default();
            c.Delay = Math.Max(5, Math.Min(200, c.Delay));
            c.Gap = c.Gap < 20 || c.Gap > 500 ? 80 : c.Gap;
            foreach (string k in c.Keys.Keys.ToList()) if (!Ids.Contains(k) || !Valid(c.Keys[k])) c.Keys.Remove(k);
            c.On.RemoveAll(x => !c.Keys.ContainsKey(x));
            return c;
        }
        public static void Save(string dir, MacroConfig c) { Engine.WriteFile(FileOf(dir), new JavaScriptSerializer().Serialize(c)); }
        // Sets the keys and the confirm-on-release that Fortnite has: if they do not match, the macro sends one edit too many and stays inside.
        public static bool Sync(MacroConfig c)
        {
            if (c.Manual) return false;
            Binds b = null;
            try { b = Binds.Load(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (b == null || b.Edit == 0) return false;
            c.Edit = b.Edit;
            if (b.Select != 0 && !Wheel(b.Select)) c.Select = b.Select;
            if (b.Reset != 0) c.Reset = b.Reset;
            if (b.HasConfirm) c.ConfirmOnRelease = b.Confirm;
            return true;
        }
        // A macro on the edit, select or reset key would trigger itself.
        static bool Clash(MacroConfig c, int vk) { return vk == c.Edit || vk == c.Select || vk == c.Reset; }

        static bool Valid(int vk) { return vk > 0 && vk < 255; }

        // Reason why that key is not valid for the macro, or null.
        public static string Refuse(MacroConfig c, string id, int vk)
        {
            if (Wheel(vk)) return "La rueda solo se puede usar en las teclas de Fortnite.";
            if (vk == 1 || vk == 2) return "Los clics izquierdo y derecho no se pueden usar como tecla de macro.";
            if (vk == c.Edit || vk == c.Select || vk == c.Reset) return "Esa tecla ya se usa en Fortnite para editar.";
            return c.Keys.Any(k => k.Key != id && k.Value == vk) ? "Esa tecla ya es de otra macro." : null;
        }

        public static string KeyName(int vk)
        {
            switch (vk)
            {
                case 0: return "Elegir tecla";
                case 1: return "Clic izquierdo";
                case 2: return "Clic derecho";
                case 4: return "Clic central";
                case 5: return "Botón lateral 1";
                case 6: return "Botón lateral 2";
                case WheelUp: return "Rueda arriba";
                case WheelDown: return "Rueda abajo";
            }
            string s = KeyInterop.KeyFromVirtualKey(vk).ToString();
            return s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1]) ? s.Substring(1) : s;
        }

        // ---- What each macro presses ----
        static void Tap(List<Act> a, int vk) { a.Add(new Act(vk, true)); a.Add(new Act(vk, false)); }
        // A wait goes in the list as a negative key.
        static Act Wait(int ms) { return new Act(-ms, false); }
        // With a tap everything fires on press; when held, it enters edit mode and you can drag until release.
        public static List<Act> Press(MacroConfig c, string id)
        {
            List<Act> a = Start(c, id);
            if (!c.Hold) a.AddRange(End(c, id));
            return a;
        }
        public static List<Act> Release(MacroConfig c, string id) { return c.Hold ? End(c, id) : new List<Act>(); }
        static List<Act> Start(MacroConfig c, string id)
        {
            List<Act> a = new List<Act>();
            Tap(a, c.Edit);
            if (id == "salir") return a;
            if (id == "reset") { Tap(a, c.Reset); if (!c.ConfirmOnRelease) Tap(a, c.Edit); }
            else a.Add(new Act(c.Select, true));
            return a;
        }
        // Confirms and, depending on the macro, does the second edit or the reset.
        static List<Act> End(MacroConfig c, string id)
        {
            List<Act> a = new List<Act>();
            if (id == "reset" || id == "salir") return a;
            a.Add(new Act(c.Select, false));
            if (!c.ConfirmOnRelease) Tap(a, c.Edit);
            if (id == "edit") return a;
            a.Add(Wait(c.Gap));
            Tap(a, c.Edit);
            Tap(a, id == "doble" ? c.Select : c.Reset);
            if (!c.ConfirmOnRelease) Tap(a, c.Edit);
            return a;
        }

        // ---- Game mode, live ----
        static string logFile { get { return Path.Combine(FpsLog.Folder, "FortniteGame.log"); } }
        static long at;
        static string rest = "";
        static bool creative;
        // Any map that is not Creative, or loading, turns it off.
        public static bool Mode(string line, bool was)
        {
            if (line.IndexOf("LogLoad: LoadMap:", StringComparison.Ordinal) == 30) return false;
            if (line.IndexOf("LogWorld: Bringing World ", StringComparison.Ordinal) != 30) return was;
            return line.IndexOf("Bringing World /VKPlay/", StringComparison.Ordinal) >= 0;
        }
        // Reads only what is new in the log. If it cannot be read, there are no macros.
        public static bool InCreative()
        {
            try
            {
                using (FileStream f = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (f.Length < at) { at = 0; rest = ""; creative = false; }
                    f.Position = at;
                    byte[] b = new byte[1 << 16];
                    int n;
                    while ((n = f.Read(b, 0, b.Length)) > 0)
                    {
                        at += n;
                        string[] l = (rest + Encoding.ASCII.GetString(b, 0, n)).Split('\n');
                        for (int i = 0; i < l.Length - 1; i++) creative = Mode(l[i], creative);
                        rest = l[l.Length - 1];
                    }
                }
                return creative;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            at = 0; rest = ""; creative = false;
            return false;
        }

        // ---- Thread ----
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr w, out int pid);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] static extern uint SendInput(uint n, Input[] i, int size);
        [DllImport("winmm.dll")] static extern int timeBeginPeriod(int ms);
        [DllImport("winmm.dll")] static extern int timeEndPeriod(int ms);
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct Input
        {
            [FieldOffset(0)] public uint Type; // 0 mouse, 1 keyboard
            [FieldOffset(8)] public ushort Vk;
            [FieldOffset(10)] public ushort Scan;
            [FieldOffset(12)] public uint KeyFlags;
            [FieldOffset(16)] public uint MouseData;
            [FieldOffset(20)] public uint MouseFlags;
        }

        static Input Make(Act a)
        {
            Input i = new Input();
            if (Wheel(a.Vk)) { i.MouseFlags = 0x800; i.MouseData = unchecked((uint)(a.Vk == WheelUp ? 120 : -120)); return i; }
            if (a.Vk <= 6)
            {
                i.MouseFlags = a.Vk == 1 ? (a.Down ? 0x2u : 0x4u) : a.Vk == 2 ? (a.Down ? 0x8u : 0x10u) : a.Vk == 4 ? (a.Down ? 0x20u : 0x40u) : (a.Down ? 0x80u : 0x100u);
                if (a.Vk >= 5) i.MouseData = (uint)a.Vk - 4;
                return i;
            }
            i.Type = 1;
            i.Scan = (ushort)MapVirtualKey((uint)a.Vk, 0);
            // Arrows, Insert, Delete, Home, End and Page keys use the extended code.
            i.KeyFlags = 8u | (a.Down ? 0u : 2u) | ((a.Vk >= 0x21 && a.Vk <= 0x28) || a.Vk == 0x2D || a.Vk == 0x2E ? 1u : 0u);
            return i;
        }

        // Replaceable in tests.
        public static Action<Act> Send = a => { if (a.Down || !Wheel(a.Vk)) SendInput(1, new[] { Make(a) }, 40); };
        public static Func<int, bool> IsDown = vk => (GetAsyncKeyState(vk) & 0x8000) != 0;
        public static Func<bool> Front = () =>
        {
            int pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            try { using (Process p = Process.GetProcessById(pid)) return p.ProcessName.StartsWith("FortniteClient", StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        };

        static volatile MacroConfig cfg;
        static volatile string state = "Apagadas";
        static volatile bool stop;
        static Thread thread;
        public static string Status { get { return state; } }

        public static void Use(MacroConfig c)
        {
            // Copy: the window keeps editing its own.
            JavaScriptSerializer js = new JavaScriptSerializer();
            cfg = js.Deserialize<MacroConfig>(js.Serialize(c));
            if (!c.Accepted || c.On.Count == 0) { Stop(); return; }
            if (thread != null) return;
            stop = false;
            at = 0; rest = ""; creative = false;
            thread = new Thread(Loop) { IsBackground = true, Name = "macros" };
            thread.Start();
        }
        public static void Stop()
        {
            stop = true;
            Thread t = thread;
            if (t != null) t.Join(2000);
            thread = null;
            state = "Apagadas";
        }

        static void Do(List<Act> list, int delay)
        {
            foreach (Act a in list)
            {
                if (a.Vk < 0) { Thread.Sleep(-a.Vk); continue; }
                Send(a);
                Thread.Sleep(delay);
            }
        }

        static void Loop()
        {
            HashSet<string> held = new HashSet<string>();
            bool active = false, fine = false;
            int check = 0;
            try
            {
                while (!stop)
                {
                    MacroConfig c = cfg;
                    int now = Environment.TickCount;
                    if (check == 0 || now - check >= 250)
                    {
                        check = now == 0 ? 1 : now;
                        bool cre = InCreative(), front = Front(), was = active;
                        active = cre && front;
                        if (active && !was) Sync(c); // in case they were changed in the game
                        state = active ? "Activas" : cre ? "En espera: Fortnite no está en primer plano" : "En espera: Fortnite no está en Creativo";
                    }
                    if (!active)
                    {
                        if (held.Count > 0) { if (c.Hold) Send(new Act(c.Select, false)); held.Clear(); }
                        if (fine) { timeEndPeriod(1); fine = false; }
                        Thread.Sleep(200);
                        continue;
                    }
                    if (!fine) { timeBeginPeriod(1); fine = true; }
                    foreach (string id in Ids)
                    {
                        int vk;
                        bool down = c.On.Contains(id) && c.Keys.TryGetValue(id, out vk) && !Clash(c, vk) && IsDown(vk);
                        if (down && held.Add(id)) Do(Press(c, id), c.Delay);
                        else if (!down && held.Remove(id)) Do(Release(c, id), c.Delay);
                    }
                    Thread.Sleep(1);
                }
            }
            finally
            {
                if (held.Count > 0 && cfg.Hold) Send(new Act(cfg.Select, false));
                if (fine) timeEndPeriod(1);
            }
        }
    }
}
