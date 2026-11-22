using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace Tablero
{
    public class Res
    {
        public int W { get; set; }
        public int H { get; set; }
        public int Hz { get; set; }
        public double Aspect { get { return W / (double)H; } }
        public string Name { get { return W + " × " + H; } }
        public string Note { get; set; }
        public bool Same(Res o) { return o != null && W == o.W && H == o.H; }
    }

    // Resolucion del monitor principal. Los cambios no se guardan en el registro: reiniciar los deshace.
    public static class Display
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
            public short SpecVersion, DriverVersion, Size, DriverExtra;
            public int Fields, X, Y, Orientation, FixedOutput;
            public short Color, Duplex, YRes, TTOption, Collate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Form;
            public short LogPixels;
            public int Bpp, Width, Height, Flags, Hz, P1, P2, P3, P4, P5, P6, P7, P8;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string device, int mode, ref DevMode dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string device, ref DevMode dm, IntPtr hwnd, int flags, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string device, IntPtr dm, IntPtr hwnd, int flags, IntPtr param);
        const int Now = -1, Registry = -2, Test = 2, Stretch = 1, Ok = 0;
        const int FieldWidth = 0x80000, FieldHeight = 0x100000, FieldHz = 0x400000, FieldFixed = 0x20000000;

        [StructLayout(LayoutKind.Sequential)] struct MemStatus { public int Size; public uint Load; public ulong Total, Free, A, B, C, D, E; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);
        public static double FreeRamGb() { MemStatus m = new MemStatus { Size = Marshal.SizeOf(typeof(MemStatus)) }; GlobalMemoryStatusEx(ref m); return m.Free / (double)(1L << 30); }

        static DevMode Blank() { DevMode d = new DevMode(); d.Size = (short)Marshal.SizeOf(typeof(DevMode)); return d; }
        static Res Get(int mode)
        {
            DevMode d = Blank();
            if (!EnumDisplaySettingsW(null, mode, ref d)) throw new InvalidOperationException("Windows no devolvió la resolución de la pantalla.");
            return new Res { W = d.Width, H = d.Height, Hz = d.Hz };
        }
        static List<Res> All()
        {
            List<Res> r = new List<Res>();
            DevMode d = Blank();
            for (int i = 0; EnumDisplaySettingsW(null, i, ref d); i++) r.Add(new Res { W = d.Width, H = d.Height, Hz = d.Hz });
            return r;
        }
        static int Change(Res r, int flags)
        {
            DevMode d = Blank();
            d.Width = r.W; d.Height = r.H; d.Hz = r.Hz; d.FixedOutput = Stretch;
            d.Fields = FieldWidth | FieldHeight | FieldHz | FieldFixed;
            return ChangeDisplaySettingsExW(null, ref d, IntPtr.Zero, flags, IntPtr.Zero);
        }
        public static Func<Res, bool> Accepts = r => Change(r, Test) == Ok;
        static void RealSet(Res r)
        {
            int e = Accepts(r) ? Change(r, 0) : -2;
            if (e != Ok) throw new InvalidOperationException("La pantalla no aceptó " + r.Name + " a " + r.Hz + " Hz (código " + e + ").");
        }
        static void RealReset()
        {
            int e = ChangeDisplaySettingsExW(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            if (e != Ok) throw new InvalidOperationException("No se pudo restaurar la resolución (código " + e + "). Se restaura al reiniciar el PC.");
        }

        // Sustituibles en las pruebas.
        public static Func<List<Res>> Modes = All;
        public static Func<Res> Current = () => Get(Now);
        public static Func<Res> Saved = () => Get(Registry);
        public static Action<Res> Set = RealSet;
        public static Action Reset = RealReset;

        static readonly string[] Popular = { "1720 × 1080", "1440 × 1080" };
        // Mas estrechas que la pantalla, con el refresco mas alto de cada una.
        public static List<Res> Stretched(IEnumerable<Res> modes, Res native)
        {
            return modes.Where(m => m.Aspect < native.Aspect * 0.97 && m.H >= 900 && m.H <= native.H && m.W >= 1200 && m.W < native.W)
                .GroupBy(m => m.Name).Select(g => g.OrderByDescending(m => m.Hz).First())
                .OrderBy(m => Array.IndexOf(Popular, m.Name) < 0 ? 9 : Array.IndexOf(Popular, m.Name)).ThenByDescending(m => m.W).ToList();
        }
        public static int MaxHz(Res at) { return Modes().Where(m => m.Same(at)).Select(m => m.Hz).DefaultIfEmpty(0).Max(); }
    }

    // Resoluciones nuevas con el controlador NVIDIA: primero a prueba, y solo se guardan si el usuario dice que se ven bien.
    public static class Custom
    {
        const int CdSize = 144, TimingIn = 32;
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Query(uint id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevicesW(string device, int i, ref Device d, int flags);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Device
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Text;
            public int Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FName([MarshalAs(UnmanagedType.LPStr)] string n, out uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FTiming(uint id, byte[] input, byte[] output);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FTry(ref uint id, uint count, byte[] cd);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FSave(ref uint id, uint count, uint output, uint monitor);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FRevert(ref uint id, uint count);
        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = Query(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("El controlador NVIDIA no ofrece esta función.");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        static void Check(int e) { if (e != 0) throw new InvalidOperationException("La gráfica no aceptó esa resolución (código " + e + ")."); }
        static uint Id()
        {
            Check(Fn<FInit>(0x0150E828)());
            Device d = new Device { Size = Marshal.SizeOf(typeof(Device)) };
            for (int i = 0; EnumDisplayDevicesW(null, i, ref d, 0); i++)
                if ((d.Flags & 4) != 0) { uint id; Check(Fn<FName>(0xAE457190)(d.Name, out id)); return id; }
            throw new InvalidOperationException("No se encontró la pantalla principal.");
        }
        static byte[] Build(uint id, Res r)
        {
            byte[] i = new byte[TimingIn], o = new byte[256], b = new byte[CdSize];
            BitConverter.GetBytes(TimingIn | (1 << 16)).CopyTo(i, 0);
            BitConverter.GetBytes(r.W).CopyTo(i, 4); BitConverter.GetBytes(r.H).CopyTo(i, 8); BitConverter.GetBytes((float)r.Hz).CopyTo(i, 12);
            Check(Fn<FTiming>(0x175167E9)(id, i, o));
            BitConverter.GetBytes(CdSize | (1 << 16)).CopyTo(b, 0);
            BitConverter.GetBytes(r.W).CopyTo(b, 4); BitConverter.GetBytes(r.H).CopyTo(b, 8);
            BitConverter.GetBytes(32).CopyTo(b, 12); BitConverter.GetBytes(21).CopyTo(b, 16); // 32 bits, A8R8G8B8
            foreach (int at in new[] { 28, 32, 36, 40 }) BitConverter.GetBytes(1f).CopyTo(b, at); // toda la imagen, sin escalar
            Buffer.BlockCopy(o, 0, b, 44, 96);
            return b;
        }
        static bool RealAvailable()
        {
            try { Id(); return true; }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        static void RealTry(Res r) { uint id = Id(); Check(Fn<FTry>(0x1F7DB630)(ref id, 1, Build(id, r))); }
        static void RealSave() { uint id = Id(); Check(Fn<FSave>(0x49882876)(ref id, 1, 1, 1)); }
        static void RealRevert() { uint id = Id(); Check(Fn<FRevert>(0xCBBD40F0)(ref id, 1)); }

        // Sustituibles en las pruebas.
        public static Func<bool> Available = RealAvailable;
        public static Action<Res> Try = RealTry;
        public static Action Save = RealSave, Revert = RealRevert;

        // Motivo por el que no vale, o null.
        public static string Invalid(string w, string h, Res native)
        {
            int x, y;
            if (!int.TryParse(w, out x) || !int.TryParse(h, out y)) return "El ancho y el alto deben ser números.";
            if (x < 800 || y < 600) return "Mínimo 800 × 600: por debajo no caben los menús de Fortnite.";
            if (x > native.W || y > native.H) return "Máximo: la resolución de la pantalla, " + native.Name + ".";
            return null;
        }
    }
    // Temporizador de Windows a 0,5 ms mientras se juega. Solo llega a Fortnite con GlobalTimerResolutionRequests (Windows 11, tras reiniciar).
    public static class Precise
    {
        public const string Key = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel", Name = "GlobalTimerResolutionRequests";
        const uint HalfMs = 5000; // en unidades de 100 ns
        [StructLayout(LayoutKind.Sequential)] struct Throttle { public uint Version, Control, State; }
        [DllImport("ntdll.dll")] static extern int NtSetTimerResolution(uint wanted, bool set, out uint now);
        [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process, int kind, ref Throttle t, int size);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        static bool on;
        public static bool Wanted { get { return RegOp.ReadValue(Key, Name) == "d:1"; } }
        // false = Windows no lo acepto. Al cerrar la app Windows lo suelta solo.
        public static bool Set(bool want)
        {
            if (on == want) return true;
            // Sin esto Windows 11 ignora la peticion con la ventana minimizada o tapada por el juego.
            Throttle t = new Throttle { Version = 1, Control = want ? 4u : 0u };
            bool ok = SetProcessInformation(GetCurrentProcess(), 4, ref t, Marshal.SizeOf(t));
            uint now;
            ok &= NtSetTimerResolution(HalfMs, want, out now) == 0;
            on = want;
            return ok;
        }
    }


    // Sesion de stretched: ajusta Fortnite y la pantalla, y despues devuelve las dos cosas.
    public class StretchSession
    {
        public class Data
        {
            public Res Res { get; set; }
            public Dictionary<string, string> Old { get; set; }
            public List<string> Confirmed { get; set; }
            public string Limit { get; set; }   // FrameRateLimit; vacio = el que tenga Fortnite
            public bool Minimize { get; set; }
            public bool Trial { get; set; }     // resolucion nueva sin guardar todavia
        }
        const string Section = "/Script/FortniteGame.FortGameUserSettings";
        readonly string file;
        Data data = new Data { Confirmed = new List<string>() };
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public StretchSession(string dir)
        {
            file = Path.Combine(dir, "stretched.json");
            if (!File.Exists(file)) return;
            data = Json.Deserialize<Data>(File.ReadAllText(file)) ?? data;
            if (data.Confirmed == null) data.Confirmed = new List<string>();
        }
        void Save() { Engine.WriteFile(file, Json.Serialize(data)); }

        public Res Last { get { return data.Res; } }
        public bool Pending { get { return data.Old != null; } }
        public bool Known(Res r) { return r.Same(Display.Saved()) || data.Confirmed.Contains(r.Name); }
        public string Limit { get { return data.Limit ?? ""; } set { data.Limit = value; Save(); } }
        public bool Minimize { get { return data.Minimize; } set { data.Minimize = value; Save(); } }
        public void Confirm(Res r)
        {
            if (data.Trial) { Custom.Save(); data.Trial = false; if (Display.Accepts(r)) Display.Set(r); }
            if (!Known(r)) data.Confirmed.Add(r.Name);
            Save();
        }

        static Dictionary<string, string> Target(Res r)
        {
            Dictionary<string, string> t = new Dictionary<string, string>();
            foreach (string p in new[] { "ResolutionSize", "LastUserConfirmedResolutionSize" }) { t[p + "X"] = "s:" + r.W; t[p + "Y"] = "s:" + r.H; }
            foreach (string p in new[] { "DesiredScreen", "LastUserConfirmedDesiredScreen" }) { t[p + "Width"] = "s:" + r.W; t[p + "Height"] = "s:" + r.H; }
            // 1 = pantalla completa en ventana: el juego usa la resolucion del escritorio, que ya esta estirada.
            foreach (string p in new[] { "FullscreenMode", "LastConfirmedFullscreenMode", "PreferredFullscreenMode" }) t[p] = "s:1";
            return t;
        }
        static IniOp Ini(string name) { return new IniOp(Section, name, "", ""); }

        public void Begin(Res r)
        {
            if (IniOp.GameRunning()) throw new InvalidOperationException("Fortnite está abierto. Ciérralo e inténtalo de nuevo.");
            if (!File.Exists(IniOp.File)) throw new InvalidOperationException("No se encontró la configuración de Fortnite. Abre el juego una vez e inténtalo de nuevo.");
            if (Pending) End();
            Dictionary<string, string> t = Target(r);
            if (Limit.Length > 0) t["FrameRateLimit"] = "s:" + Limit + ".000000";
            data.Trial = false;
            data.Res = r;
            data.Old = t.Keys.ToDictionary(k => k, k => Ini(k).Read(""));
            Save();
            try
            {
                foreach (KeyValuePair<string, string> p in t) Ini(p.Key).Write("", p.Value);
                bool exists = Display.Modes().Any(m => m.Same(r));
                if (!exists) { Custom.Try(r); data.Trial = true; Save(); }
                else if (!Display.Current().Same(r)) Display.Set(r);
                if (!Display.Current().Same(r)) throw new InvalidOperationException("Windows no aplicó " + r.Name + ".");
            }
            catch { End(); throw; }
        }

        // Con el juego abierto solo se devuelve la pantalla; el archivo del juego espera a que cierre.
        public void End()
        {
            if (!Pending) return;
            Exception screen = null;
            try { if (data.Trial) { Custom.Revert(); data.Trial = false; Save(); } if (Display.Current().Same(data.Res) && !Display.Saved().Same(data.Res)) Display.Reset(); }
            catch (Exception ex) { screen = ex; }
            if (!IniOp.GameRunning())
            {
                if (File.Exists(IniOp.File)) foreach (KeyValuePair<string, string> p in data.Old) Ini(p.Key).Write("", p.Value);
                // Si la pantalla fallo, la sesion sigue pendiente para reintentar.
                if (screen == null) { data.Old = null; Save(); }
            }
            if (screen != null) throw screen;
        }

        // Por el explorador, para que Epic no herede los permisos de administrador.
        public static Action<string> Open = url => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + url + "\"") { UseShellExecute = false });
        public const string Game = "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true";
    }
}
