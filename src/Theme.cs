using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tablero
{
    // Each palette: two bases that do almost everything, Cta only for the main button and On only for what is enabled.
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
        static readonly string[] Keys = { "Bg", "Rail", "Card", "CardHover", "Ink", "Ink2", "Cta", "CtaInk", "On", "OnInk", "Off", "Knob", "Btn", "BtnHover", "BtnInk", "Danger", "Line" };
        static Palette P(string name, params string[] v)
        {
            return new Palette { Name = name, C = Keys.Select((k, i) => new { k, i }).ToDictionary(x => x.k, x => "#" + v[x.i]) };
        }
        public static readonly List<Palette> All = new List<Palette>
        {
            //            Bg        Rail      Card      CardHover Ink       Ink2      Cta       CtaInk    On        OnInk     Off       Knob      Btn       BtnHover  BtnInk    Danger    Line
            P("Glaciar", "005A78", "004560", "006F91", "007597", "FFFFFF", "E2F8FF", "FF8FB2", "2B0413", "8CFAFF", "03303C", "00344A", "FFFFFF", "002638", "001A28", "FFFFFF", "FFF3EE", "71B4C8"),
            // Two neighboring hues (base and On) and the opposite on the color wheel for Cta.
            P("Medianoche", "0F1B2D", "0A1421", "18293F", "1E3149", "F4F7FB", "AEBFD3", "F2A93B", "241500", "5CC8E8", "06222C", "07101B", "FFFFFF", "2A4464", "335277", "F4F7FB", "FFB4A8", "637489"),
            P("Grafito",    "1B1D21", "131518", "272A30", "2E3239", "F5F6F8", "B4BAC4", "FF7A4D", "2B0D02", "62BCEB", "06222F", "0D0E10", "FFFFFF", "3A3F48", "454B56", "F5F6F8", "FFB4A8", "6E727A"),
            P("Bosque",     "10241F", "0B1A16", "1A362F", "204138", "F3FAF7", "AFC9C0", "FF8A75", "330B04", "5FD3B5", "06291F", "07120F", "FFFFFF", "2B5247", "346254", "F3FAF7", "FFC0B5", "648078"),
            P("Marfil",     "F1F4F7", "E3E9EF", "FFFFFF", "F8FAFC", "14212E", "4C5D6E", "BF4E08", "FFFFFF", "0B7A94", "FFFFFF", "B4C2CF", "FFFFFF", "22384D", "2E4A64", "FFFFFF", "A81E15", "798692"),
            P("Ciruela",    "26141F", "1B0D16", "38202E", "422737", "FBF4F7", "CDB4C0", "8FD694", "0B2410", "E79AB8", "35101F", "130810", "FFFFFF", "553246", "643C53", "FBF4F7", "FFB9AC", "826A77"),
            P("Carmesí",    "2A1519", "1B0D10", "3E2127", "4A2930", "FFF6F3", "E3C6C2", "FF6B6B", "330608", "FFD3B0", "3A1B08", "140709", "FFFFFF", "5F333B", "703E47", "FFF6F3", "FFC2B4", "907474"),
            // Counter-Strike: bluish graphite, CS amber for Cta and muted CT blue for On.
            P("CS2",        "11151C", "0B0E13", "1A2029", "212834", "F4F6F8", "AEB8C6", "E8A23A", "241500", "5D7FB0", "050B16", "07090D", "FFFFFF", "2B3A50", "34465F", "F4F6F8", "FFB4A8", "5F6B7C"),
        };

        // Themes: animated background, its music and a palette that goes with it. Simple keeps the colors per game, without a background.
        public static readonly List<Look> Looks = new List<Look>
        {
            new Look { Id = "noche", Name = "Noche", Pal = Named("Medianoche") },
            new Look { Id = "lluvia", Name = "Lluvia", Pal = Named("Grafito") },
            new Look { Id = "arcade", Name = "Arcade", Pal = Named("Ciruela") },
            new Look { Id = "simple", Name = "Simple", Simple = true },
        };
        public static Look LookOf(string id) { return Looks.FirstOrDefault(x => x.Id == id) ?? Looks.Last(); }
        // Each theme brings its own music; Simple keeps the one chosen.
        public static string MusicOf(Look l, string now) { return l.Simple ? now : l.Id; }
        public static Palette Named(string n) { return All.First(p => p.Name == n); }
        // How much of the page and of the side panels covers the background; cards stay solid.
        // The page takes 52 % (the mockup had 46 %): with less, secondary text loses 4.5:1 over the Arcade sun and floor.
        public const double BgOver = 0.52, RailOver = 0.78;

        public static Color Parse(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        public static void Apply(Window w, Palette p, bool scene = false)
        {
            foreach (KeyValuePair<string, string> kv in p.C)
            {
                SolidColorBrush b = new SolidColorBrush(Parse(kv.Value));
                b.Freeze();
                w.Resources[kv.Key] = b;
            }
            foreach (string k in new[] { "Bg", "Rail" })
            {
                Color c = Parse(p.C[k]);
                c.A = (byte)Math.Round(255 * (!scene ? 1 : k == "Bg" ? BgOver : RailOver));
                SolidColorBrush b = new SolidColorBrush(c);
                b.Freeze();
                w.Resources[k + "A"] = b;
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
        // Text over a background frame seen through key at alpha. share = brightest part of the frame left out (the sun, lamps).
        public static double OverScene(Palette p, string ink, string key, double alpha, int[] frame, double share)
        {
            Color b = Parse(p.C[key]);
            double[] l = frame.Select(x => Lum(Color.FromRgb(Mix(x >> 16, b.R, alpha), Mix(x >> 8, b.G, alpha), Mix(x, b.B, alpha)))).OrderBy(x => x).ToArray();
            double back = l[(int)((l.Length - 1) * (1 - share))], i = Lum(Parse(p.C[ink]));
            return (Math.Max(i, back) + 0.05) / (Math.Min(i, back) + 0.05);
        }
        static byte Mix(int scene, byte over, double alpha) { return (byte)Math.Round((scene & 255) * (1 - alpha) + over * alpha); }
    }

    public class Look : Vm
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get { return L.T(Name); } }
        public bool Simple { get; set; }
        ImageSource thumb;
        Palette pal;
        public ImageSource Thumb { get { return thumb; } set { thumb = value; Raise("Thumb"); } }
        // Its palette; Simple shows the one of the game on screen.
        public Palette Pal { get { return pal; } set { pal = value; Raise("Pal"); } }
    }

    // Small section labels: WPF has no text-transform, so they are uppercased here (translated first, for bound texts).
    public class Upper : System.Windows.Data.IValueConverter
    {
        public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c) { string s = v as string; return s == null ? v : L.T(s).ToUpper(System.Globalization.CultureInfo.CurrentCulture); }
        public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) { throw new NotSupportedException(); }
    }
}
