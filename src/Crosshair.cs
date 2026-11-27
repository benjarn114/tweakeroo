using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Tablero
{
    // Crosshair model. All lengths are pixels at the game's render resolution.
    // Offset = distance from the crosshair centre to the inner end of each line (Valorant's meaning).
    // Valorant draws fixed pixels: a code looks smaller at 1440p than at 1080p, which is why resolution converters exist.
    // Source (fixed pixels, converters scale by hand): https://github.com/sciencejiho/valorant-crosshair
    public class XLines
    {
        public bool Show = true;
        public double Length = 6, Thickness = 2, Offset = 3, Opacity = 0.8;
        public XLines Clone() { return (XLines)MemberwiseClone(); }
    }

    public class Crosshair
    {
        public string Name = "Crosshair";
        public uint Color = 0xFFFFFFFF;            // ARGB, alpha ignored (use the opacity fields)
        public bool Outline = true; public double OutlineThickness = 1, OutlineOpacity = 0.5;
        public bool Dot = false; public double DotThickness = 2, DotOpacity = 1;
        public bool TStyle = false;                // CS2 "T" crosshair: no top line. Valorant has no such option.
        // Valorant defaults for the primary profile, the values its code omits.
        // Source: https://github.com/valapi/node-valapi/blob/main/packages/@valapi/crosshair/src/index.ts
        public XLines Inner = new XLines();
        public XLines Outer = new XLines { Length = 2, Thickness = 2, Offset = 10, Opacity = 0.35 };

        public Crosshair Clone() { Crosshair c = (Crosshair)MemberwiseClone(); c.Inner = Inner.Clone(); c.Outer = Outer.Clone(); return c; }

        static FormatException Bad() { return new FormatException("Código de crosshair no reconocido."); }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static Crosshair FromCode(string code)
        {
            string s = (code ?? "").Trim();
            if (s.StartsWith("CS")) return FromCs2(s, Display.Current().H);
            return FromValorant(s);
        }

        // ---------- Valorant ----------
        // Preset colours c;0..7 (8 = custom from u;RRGGBBAA).
        // Source: https://github.com/valapi/node-valapi/blob/main/packages/@valapi/lib/src/resources/CrosshairHexColor.ts
        static readonly uint[] Presets = { 0xFFFFFF, 0x00FF00, 0x7FFF00, 0xDFFF00, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0xFF0000 };

        // Code: "0" (general), then sections P (primary), A (ADS), S (sniper), each a list of key;value.
        // Only P is read. Movement/firing error keys (m, f, s, 0m, 0f, 0s, 0e, 1m, 1f, 1s, 1e) and 0v/0g (separate
        // vertical length) are accepted and ignored: an overlay cannot move and the model has one length per line set.
        public static Crosshair FromValorant(string code)
        {
            string[] t = (code ?? "").Trim().TrimEnd(';').Split(';');
            if (t[0].Trim() != "0") throw Bad();
            Dictionary<string, string> p = new Dictionary<string, string>();
            string sec = "0";
            for (int i = 1; i < t.Length; i++)
            {
                string k = t[i].Trim();
                if (k == "P" || k == "A" || k == "S") { sec = k; continue; }
                if (!Regex.IsMatch(k, "^[01]?[a-z]$") || i + 1 >= t.Length) throw Bad();
                string v = t[++i].Trim();
                if (v.Length == 0) throw Bad();
                if (sec == "P") p[k] = v;
            }
            Crosshair x = new Crosshair();
            foreach (string k in p.Keys.Where(k => k != "u")) Num(p, k, 0);   // every other value must be a number
            int c = (int)Num(p, "c", 0);
            if (c < 0 || c > 8 || c != Num(p, "c", 0)) throw Bad();
            if (c < 8 && Num(p, "b", 0) == 0) x.Color = 0xFF000000 | Presets[c];
            else
            {
                string u = p.ContainsKey("u") ? p["u"] : "FFFFFFFF";
                uint rgb;
                if ((u.Length != 6 && u.Length != 8) || !uint.TryParse(u.Substring(0, 6), NumberStyles.HexNumber, Inv, out rgb)) throw Bad();
                x.Color = 0xFF000000 | rgb;
            }
            x.Outline = Num(p, "h", 1) != 0; x.OutlineThickness = Num(p, "t", x.OutlineThickness); x.OutlineOpacity = Num(p, "o", x.OutlineOpacity);
            x.Dot = Num(p, "d", 0) != 0; x.DotThickness = Num(p, "z", x.DotThickness); x.DotOpacity = Num(p, "a", x.DotOpacity);
            Lines(p, "0", x.Inner); Lines(p, "1", x.Outer);
            return x;
        }
        static void Lines(Dictionary<string, string> p, string n, XLines l)
        {
            l.Show = Num(p, n + "b", 1) != 0; l.Thickness = Num(p, n + "t", l.Thickness); l.Length = Num(p, n + "l", l.Length);
            l.Offset = Num(p, n + "o", l.Offset); l.Opacity = Num(p, n + "a", l.Opacity);
        }
        static double Num(Dictionary<string, string> p, string k, double def)
        {
            string v; double d;
            if (!p.TryGetValue(k, out v)) return def;
            if (!double.TryParse(v, NumberStyles.Float, Inv, out d) || double.IsNaN(d) || double.IsInfinity(d)) throw Bad();
            return d;
        }

        // Writes only what differs from the defaults, in Valorant's key order.
        public string ToValorantCode()
        {
            Crosshair d = new Crosshair();
            List<string> o = new List<string>();
            int i = Array.IndexOf(Presets, Color & 0xFFFFFF);
            if (i < 0) o.AddRange(new[] { "c", "8", "u", (Color & 0xFFFFFF).ToString("X6") + "FF" });
            else if (i > 0) o.AddRange(new[] { "c", i.ToString(Inv) });
            Add(o, "h", Outline, d.Outline); Add(o, "t", OutlineThickness, d.OutlineThickness); Add(o, "o", OutlineOpacity, d.OutlineOpacity);
            Add(o, "d", Dot, d.Dot); Add(o, "z", DotThickness, d.DotThickness); Add(o, "a", DotOpacity, d.DotOpacity);
            foreach (string n in new[] { "0", "1" })
            {
                XLines l = n == "0" ? Inner : Outer, ld = n == "0" ? d.Inner : d.Outer;
                Add(o, n + "b", l.Show, ld.Show); Add(o, n + "t", l.Thickness, ld.Thickness); Add(o, n + "l", l.Length, ld.Length);
                Add(o, n + "o", l.Offset, ld.Offset); Add(o, n + "a", l.Opacity, ld.Opacity);
            }
            return o.Count == 0 ? "0" : "0;P;" + string.Join(";", o);
        }
        static void Add(List<string> o, string k, bool v, bool d) { if (v != d) { o.Add(k); o.Add(v ? "1" : "0"); } }
        static void Add(List<string> o, string k, double v, double d) { if (v != d) { o.Add(k); o.Add(v.ToString("R", Inv)); } }

        // ---------- CS2 ----------
        // Share codes: base 57, least significant character first, read as a big-endian byte array; byte 0 = sum(bytes[1..]) % 256.
        // Layouts: https://github.com/akiver/csgo-sharecode/blob/main/doc/code_layout.md
        //   "CSGO-xxxxx-xxxxx-xxxxx-xxxxx-xxxxx" = 18 bytes, version 1 (CS:GO era), 3 or 4 (CS2 Sept 2026).
        //   "CS" + 44 characters = 32 bytes, the code CS2 exports since 30/09/2026.
        // Pixels: version 1 stores CS:GO units, length and thickness = value × screenHeight / 480 (thickness at least 1),
        //   gap = 4 + value in pixels, not scaled. Source: https://github.com/spartytech/Crosshair-Bridge (README)
        //   The newer codes store pixels for the screen height inside the code; CS2 draws px × screenHeight / that height.
        //   Source: https://github.com/akiver/csgo-sharecode (README) and https://cs2crosshaircode.com/guides/cs2-crosshair-commands/
        // CS2's gap is measured from the edge of the line thickness, so Offset = gap + thickness / 2.
        const string A57 = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefhijkmnopqrstuvwxyz23456789";
        static readonly uint[] OldColors = { 0xFA3232, 0x32FA32, 0xFAFA32, 0x3232FA, 0x32FAFA };   // cl_crosshaircolor 0..4

        public static Crosshair FromCs2(string shareCode, int screenHeight)
        {
            string s = Regex.Replace(shareCode ?? "", @"\s", "");
            bool modern = s.Length == 46 && s.StartsWith("CS");
            if (!modern && !Regex.IsMatch(s, "^CSGO(-?[" + A57 + "]{5}){5}$")) throw Bad();
            byte[] b = Base57(modern ? s.Substring(2) : s.Substring(4).Replace("-", ""), modern ? 32 : 18);
            if (b.Skip(1).Sum(v => v) % 256 != b[0])
                throw new FormatException("El código de CS2 está mal copiado: no cuadra la suma de control.");
            int ver = b[1];
            if (screenHeight <= 0) screenHeight = 1080;
            Crosshair x = new Crosshair();
            x.Outer.Show = false;
            double alpha, thick, len, gap, outline, outlineAlpha = -1;
            uint rgb; int style = 0, outlineMode;
            if (!modern && ver == 1)
            {
                double k = screenHeight / 480.0;
                int flags = b[13] >> 4, idx = b[10] & 7;
                rgb = idx < 5 ? OldColors[idx] : (uint)(b[4] << 16 | b[5] << 8 | b[6]);
                alpha = (flags & 4) != 0 ? b[7] : 255;   // without cl_crosshairusealpha CS:GO blends additively: drawn opaque here
                thick = Math.Max(1, Math.Round(b[12] / 10.0 * k));
                len = Math.Round((((b[15] & 0x1f) << 8) + b[14]) / 10.0 * k);
                gap = Math.Round(4 + (sbyte)b[2] / 10.0);
                x.Dot = (flags & 1) != 0; x.TStyle = (flags & 8) != 0;
                outlineMode = (b[10] & 8) != 0 ? 1 : 0; outline = b[3] / 2.0;
            }
            else if (!modern && (ver == 3 || ver == 4) || modern && ver == 1)
            {
                uint f = modern ? (uint)(b[18] | b[19] << 8 | b[20] << 16 | b[21] << 24) : (uint)(b[10] | b[11] << 8 | b[12] << 16 | b[13] << 24);
                int made = modern ? b[2] | b[3] << 8 : b[14] | b[15] << 8, st = modern ? b[4] : b[2], o = modern ? 5 : 3;
                if (made == 0) throw Bad();
                double k = screenHeight / (double)Math.Max(240, made);
                style = st & (modern ? 0x1f : 15);
                x.Dot = (st & 0x40) != 0; x.TStyle = (st & 0x80) != 0;
                rgb = (uint)(b[o] << 16 | b[o + 1] << 8 | b[o + 2]);
                alpha = b[o + 3];
                thick = modern ? Math.Min(b[13] & 0x3f, 32) : (int)((f >> 23) & 31);
                len = modern ? b[16] : b[8];
                gap = modern ? Math.Max(-3840, Math.Min(3840, (int)(short)(b[14] | b[15] << 8))) : b[7];
                outlineMode = modern ? b[13] >> 6 : ver == 3 ? (st >> 5) & 1 : (int)(f >> 28) & 3;
                if (modern) outlineAlpha = b[12] / 255.0;
                thick = Math.Round(thick * k); len = Math.Round(len * k); gap = Math.Round(gap * k);
                outline = 1;   // full or half outline: 1 px either way
            }
            else throw new FormatException("Esta versión de código de CS2 aún no está soportada.");

            x.Color = 0xFF000000 | rgb;
            x.Inner.Show = style != 6;   // 6 = Dot Only; circles, squares and quads are drawn as a cross
            x.Inner.Thickness = thick; x.Inner.Length = len; x.Inner.Offset = gap + thick / 2; x.Inner.Opacity = alpha / 255;
            x.Dot = x.Dot || style == 6; x.DotThickness = thick; x.DotOpacity = x.Inner.Opacity;
            x.Outline = outlineMode != 0; x.OutlineThickness = outline; x.OutlineOpacity = outlineAlpha >= 0 ? outlineAlpha : x.Inner.Opacity;
            return x;
        }
        static byte[] Base57(string body, int n)
        {
            byte[] b = new byte[n];
            for (int i = body.Length - 1; i >= 0; i--)
            {
                int carry = A57.IndexOf(body[i]);
                if (carry < 0) throw Bad();
                for (int j = n - 1; j >= 0; j--) { int v = b[j] * 57 + carry; b[j] = (byte)v; carry = v >> 8; }
                if (carry != 0) throw Bad();
            }
            return b;
        }

        // ---------- Prefs ----------
        // One line: Valorant code | escaped name | T style. Missing or broken parts fall back to the defaults.
        public string Serialize() { return ToValorantCode() + "|" + Uri.EscapeDataString(Name ?? "") + "|" + (TStyle ? "1" : "0"); }
        public static Crosshair Deserialize(string s)
        {
            string[] p = (s ?? "").Split('|');
            Crosshair x;
            try { x = FromValorant(p[0]); } catch (FormatException) { x = new Crosshair(); }
            if (p.Length > 1) try { x.Name = Uri.UnescapeDataString(p[1]); } catch (UriFormatException) { }
            x.TStyle = p.Length > 2 && p[2] == "1";
            return x;
        }
    }
}
