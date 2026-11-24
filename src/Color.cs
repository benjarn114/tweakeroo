using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace Tablero
{
    // 50 is the default for everything except gamma, which is 1.
    public class ColorPick
    {
        public string Profile { get; set; }
        public int Vib { get; set; }
        public int Bright { get; set; }
        public int Contrast { get; set; }
        public double Gamma { get; set; }
        public ColorPick Copy() { return (ColorPick)MemberwiseClone(); }
        public bool Plain { get { return Bright == 50 && Contrast == 50 && Gamma == 1; } }
    }

    public class ColorProfile
    {
        public string Id;
        public string Name { get; set; }
        public string Note { get; set; }
        public ColorPick Pick;
    }

    // Main display color: NVIDIA digital vibrance and Windows brightness curve.
    public static class Vivid
    {
        public const string None = "nada", Mine = "propio";
        static ColorProfile P(string id, string name, string note, int vib, int bright, int contrast, double gamma)
        {
            return new ColorProfile { Id = id, Name = name, Note = note, Pick = new ColorPick { Profile = id, Vib = vib, Bright = bright, Contrast = contrast, Gamma = gamma } };
        }
        public static readonly List<ColorProfile> All = new List<ColorProfile> {
            P(None, "Sin cambios", "La pantalla como está", 50, 50, 50, 1),
            P("natural", "Natural", "Intensidad 50 %", 50, 50, 50, 1),
            P("competitivo", "Competitivo", "Más claro en zonas oscuras", 70, 55, 50, 1.1),
            P("vivo", "Vivo", "88 %, brillo 55 %", 88, 55, 50, 1),
            P("maximo", "Máximo", "Intensidad 100 %", 100, 50, 50, 1),
            P(Mine, "Personalizado", "Con los controles de abajo", 80, 50, 50, 1) };

        public static ColorPick Clean(ColorPick p)
        {
            if (p == null || !All.Any(x => x.Id == p.Profile)) return All[0].Pick.Copy();
            if (p.Profile != Mine) return All.First(x => x.Id == p.Profile).Pick.Copy();
            double g = double.IsNaN(p.Gamma) ? 1 : p.Gamma;
            return new ColorPick { Profile = Mine, Vib = Clamp(p.Vib, 0, 100), Bright = Clamp(p.Bright, 35, 70), Contrast = Clamp(p.Contrast, 35, 70), Gamma = Math.Round(Math.Max(0.7, Math.Min(1.6, g)), 2) };
        }
        static int Clamp(int v, int a, int b) { return Math.Max(a, Math.Min(b, v)); }

        public static ushort[] Ramp(ColorPick p)
        {
            ushort[] r = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                double v = (Math.Pow(i / 255.0, 1 / p.Gamma) - 0.5) * (1 + (p.Contrast - 50) / 50.0) + 0.5 + (p.Bright - 50) / 100.0;
                r[i] = r[i + 256] = r[i + 512] = (ushort)Math.Round(Math.Max(0, Math.Min(1, v)) * 65535);
            }
            return r;
        }

        // ---- NVIDIA ----
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Query(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FHandle([MarshalAs(UnmanagedType.LPStr)] string n, out IntPtr h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FDvc(IntPtr h, uint output, int[] info);
        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = Query(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("El controlador NVIDIA no ofrece esta función.");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        static string Screen() { return System.Windows.Forms.Screen.PrimaryScreen.DeviceName; }
        static IntPtr Handle()
        {
            IntPtr h;
            if (Fn<FInit>(0x0150E828)() != 0 || Fn<FHandle>(0x35C29134)(Screen(), out h) != 0) throw new InvalidOperationException("El controlador NVIDIA no ofrece esta función.");
            return h;
        }
        // version, current, min, max, default
        static int[] Info(IntPtr h)
        {
            int[] d = new int[5];
            d[0] = 20 | 1 << 16;
            if (Fn<FDvc>(0x0E45002D)(h, 0, d) != 0) throw new InvalidOperationException("El controlador NVIDIA no ofrece esta función.");
            return d;
        }
        static int? RealGet()
        {
            try { return Info(Handle())[1]; }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
        static void RealSet(int v)
        {
            IntPtr h = Handle();
            int[] d = Info(h);
            d[1] = Math.Max(d[2], Math.Min(d[3], v));
            int e = Fn<FDvc>(0x4A82C2B1)(h, 0, d);
            if (e != 0) throw new InvalidOperationException("La gráfica no aceptó el color (código " + e + ").");
        }

        // ---- Windows ----
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDCW(string driver, string device, string output, IntPtr init);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool GetDeviceGammaRamp(IntPtr dc, [Out] ushort[] r);
        [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr dc, [In] ushort[] r);
        static TR Dc<TR>(Func<IntPtr, TR> f)
        {
            IntPtr dc = CreateDCW(Screen(), null, null, IntPtr.Zero);
            try { return f(dc); }
            finally { DeleteDC(dc); }
        }
        // Replaceable in tests. null = this display does not support it.
        public static Func<int?> GetVib = RealGet;
        public static Action<int> SetVib = RealSet;
        public static Func<ushort[]> GetRamp = () => { ushort[] r = new ushort[768]; return Dc(dc => GetDeviceGammaRamp(dc, r)) ? r : null; };
        public static Func<ushort[], bool> SetRamp = r => Dc(dc => SetDeviceGammaRamp(dc, r));

        // ---- Preview over a photo ----
        static double Sat(int vib) { return vib >= 50 ? 1 + (vib - 50) / 50.0 : vib / 50.0; }
        // The photo already shows the display's current color: only the difference is added.
        public static void Paint(byte[] src, byte[] dst, ColorPick p, int now)
        {
            double s = Sat(p.Vib) / Math.Max(0.2, Sat(now));
            ushort[] ramp = Ramp(p);
            for (int i = 0; i + 3 < src.Length; i += 4)
            {
                double b = src[i], g = src[i + 1], r = src[i + 2], y = 0.114 * b + 0.587 * g + 0.299 * r;
                dst[i] = One(y + (b - y) * s, ramp); dst[i + 1] = One(y + (g - y) * s, ramp); dst[i + 2] = One(y + (r - y) * s, ramp); dst[i + 3] = src[i + 3];
            }
        }
        static byte One(double v, ushort[] ramp) { return (byte)(ramp[(int)Math.Max(0, Math.Min(255, v))] >> 8); }
    }

    // Saves the previous color, applies the profile's and can restore it even if the app closes halfway.
    public class ColorSession
    {
        public class Data
        {
            public int? Vib { get; set; }
            public ushort[] Ramp { get; set; }
        }
        readonly string file;
        Data old;
        ColorPick now;
        public ColorPick Now { get { return now; } }

        public ColorSession(string dir)
        {
            file = Path.Combine(dir, "color.json");
            try { if (File.Exists(file)) old = new JavaScriptSerializer().Deserialize<Data>(File.ReadAllText(file)); }
            catch (Exception) { }
            if (old != null && old.Ramp != null && old.Ramp.Length != 768) old.Ramp = null;
        }
        static bool Same(ushort[] a, ushort[] b) { return a != null && b != null && a.Zip(b, (x, y) => Math.Abs(x - y)).All(d => d < 300); }

        // false = the display did not accept the brightness.
        public bool Apply(ColorPick p)
        {
            if (p == null || p.Profile == Vivid.None) { Restore(); return true; }
            if (old == null)
            {
                old = new Data { Vib = Vivid.GetVib(), Ramp = Vivid.GetRamp() };
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                Engine.WriteFile(file, new JavaScriptSerializer().Serialize(old));
            }
            now = p;
            return Put();
        }
        bool Put()
        {
            if (old.Vib != null && Vivid.GetVib() != now.Vib) Vivid.SetVib(now.Vib);
            if (old.Ramp == null) return now.Plain;
            ushort[] want = now.Plain ? old.Ramp : Vivid.Ramp(now);
            return Same(Vivid.GetRamp(), want) || Vivid.SetRamp(want);
        }
        // Changing resolution clears the curve: it is applied again.
        public void Keep() { if (now != null) Put(); }
        public void Restore()
        {
            now = null;
            if (old == null) return;
            if (old.Vib != null) Vivid.SetVib(old.Vib.Value);
            if (old.Ramp != null) Vivid.SetRamp(old.Ramp);
            old = null;
            File.Delete(file);
        }
    }
}
