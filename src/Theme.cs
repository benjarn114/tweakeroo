using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tablero
{
    // Cada paleta: dos bases que hacen casi todo, Cta solo para el boton principal y On solo para lo activado.
    public class Palette
    {
        public string Name { get; set; }
        public Dictionary<string, string> C;
        public Brush Base { get { return B("Bg"); } }
        public Brush Accent { get { return B("Cta"); } }
        public Brush Live { get { return B("On"); } }
        Brush B(string k) { SolidColorBrush b = new SolidColorBrush(Theme.Parse(C[k])); b.Freeze(); return b; }
    }

    public static class Theme
    {
        static readonly string[] Keys = { "Bg", "Rail", "Card", "CardHover", "Ink", "Ink2", "Cta", "CtaInk", "On", "OnInk", "Off", "Knob", "Btn", "BtnHover", "BtnInk", "Danger" };
        static Palette P(string name, params string[] v)
        {
            return new Palette { Name = name, C = Keys.Select((k, i) => new { k, i }).ToDictionary(x => x.k, x => "#" + v[x.i]) };
        }
        public static readonly List<Palette> All = new List<Palette>
        {
            //            Bg        Rail      Card      CardHover Ink       Ink2      Cta       CtaInk    On        OnInk     Off       Knob      Btn       BtnHover  BtnInk    Danger
            P("Glaciar", "005A78", "004560", "006F91", "007597", "FFFFFF", "E2F8FF", "FF8FB2", "2B0413", "8CFAFF", "03303C", "00344A", "FFFFFF", "002638", "001A28", "FFFFFF", "FFF3EE"),
            // Dos tonos vecinos (base y On) y el opuesto del circulo de color para Cta.
            P("Medianoche", "0F1B2D", "0A1421", "18293F", "1E3149", "F4F7FB", "AEBFD3", "F2A93B", "241500", "5CC8E8", "06222C", "07101B", "FFFFFF", "2A4464", "335277", "F4F7FB", "FFB4A8"),
            P("Grafito",    "1B1D21", "131518", "272A30", "2E3239", "F5F6F8", "B4BAC4", "FF7A4D", "2B0D02", "62BCEB", "06222F", "0D0E10", "FFFFFF", "3A3F48", "454B56", "F5F6F8", "FFB4A8"),
            P("Bosque",     "10241F", "0B1A16", "1A362F", "204138", "F3FAF7", "AFC9C0", "FF8A75", "330B04", "5FD3B5", "06291F", "07120F", "FFFFFF", "2B5247", "346254", "F3FAF7", "FFC0B5"),
            P("Marfil",     "F1F4F7", "E3E9EF", "FFFFFF", "F8FAFC", "14212E", "4C5D6E", "BF4E08", "FFFFFF", "0B7A94", "FFFFFF", "B4C2CF", "FFFFFF", "22384D", "2E4A64", "FFFFFF", "A81E15"),
            P("Ciruela",    "26141F", "1B0D16", "38202E", "422737", "FBF4F7", "CDB4C0", "8FD694", "0B2410", "E79AB8", "35101F", "130810", "FFFFFF", "553246", "643C53", "FBF4F7", "FFB9AC"),
        };

        public static Color Parse(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        public static void Apply(Window w, Palette p)
        {
            foreach (KeyValuePair<string, string> kv in p.C)
            {
                SolidColorBrush b = new SolidColorBrush(Parse(kv.Value));
                b.Freeze();
                w.Resources[kv.Key] = b;
            }
            IntPtr h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            Caption(h, 35, Parse(p.C["Rail"]));
            Caption(h, 36, Parse(p.C["Ink"]));
        }
        static void Caption(IntPtr h, int attr, Color c) { int v = c.R | c.G << 8 | c.B << 16; DwmSetWindowAttribute(h, attr, ref v, 4); }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        static double Lum(Color c)
        {
            Func<byte, double> f = x => { double s = x / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
        }
        public static double Contrast(Palette p, string a, string b)
        {
            double x = Lum(Parse(p.C[a])), y = Lum(Parse(p.C[b]));
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }
    }
}
