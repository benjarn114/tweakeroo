using System;
using System.Collections.Generic;
using System.Linq;

namespace Tablero
{
    // Seeded random numbers (mulberry32, as in the mockup): the same seed always draws the same city.
    public sealed class Rng
    {
        uint a;
        public Rng(uint seed) { a = seed; }
        public double Next()
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = (a ^ (a >> 15)) * (1 | a);
                t = (t + (t ^ (t >> 7)) * (61 | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    // A sprite or a wrapping layer: 0 = transparent, anything else is 0xFFRRGGBB.
    public sealed class Spr
    {
        public readonly int W, H;
        public readonly int[] D;
        public Spr(int w, int h) { W = w; H = h; D = new int[w * h]; }
        public Spr(string[] rows, Dictionary<char, int> pal) : this(rows.Max(r => r.Length), rows.Length)
        {
            for (int y = 0; y < H; y++) for (int x = 0; x < rows[y].Length; x++) { int c; if (pal.TryGetValue(rows[y][x], out c)) D[y * W + x] = c; }
        }
    }

    // Pixel-art backgrounds: 320 × 180 in an int buffer (Bgra32 in memory), scaled up without smoothing by the window.
    // Ported from siguiente-sesion\tematicas\tematicas.html; Update moves things once per frame, Draw paints Fb.
    public abstract class Scene
    {
        public const int W = 320, H = 180;
        public readonly int[] Fb = new int[W * H];
        public int Frame;
        // Scene columns not covered by the side panels: the Arcade HUD stays inside them.
        public static int SafeL, SafeR = W;
        protected Rng R;
        protected abstract void Update(int f);
        protected abstract void Draw(int f);
        public void Step() { Update(Frame++); Draw(Frame); }
        public void Skip(int n) { for (int i = 0; i < n; i++) Update(Frame++); Draw(Frame); }
        // Average milliseconds per frame (update and draw) over n frames, after a warm-up.
        public static double Cost(string id, int n)
        {
            Scene s = Make(id);
            s.Skip(30);
            System.Diagnostics.Stopwatch t = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) s.Step();
            return t.Elapsed.TotalMilliseconds / n;
        }

        public static readonly string[] Ids = { "noche", "lluvia", "arcade" };
        public static Scene Make(string id)
        {
            if (id == "noche") return new Night();
            if (id == "lluvia") return new Rain();
            if (id == "arcade") return new Arcade();
            return null;
        }

        // ---- color ----
        public static int Col(string hex) { return unchecked((int)(0xFF000000u | Convert.ToUInt32(hex.Substring(1), 16))); }
        protected static int[] Cols(params string[] h) { return h.Select(Col).ToArray(); }
        public static int Lerp(int a, int b, double t)
        {
            int ar = (a >> 16) & 255, ag = (a >> 8) & 255, ab = a & 255, br = (b >> 16) & 255, bg = (b >> 8) & 255, bb = b & 255;
            return unchecked((int)0xFF000000) | (int)(ar + (br - ar) * t) << 16 | (int)(ag + (bg - ag) * t) << 8 | (int)(ab + (bb - ab) * t);
        }
        protected static Dictionary<char, int> Pal(string keys, params int[] c) { Dictionary<char, int> d = new Dictionary<char, int>(); for (int i = 0; i < keys.Length; i++) d[keys[i]] = c[i]; return d; }

        // ---- numbers (JS rounding and flooring) ----
        protected static int Rd(double v) { return (int)Math.Floor(v + 0.5); }
        protected static int Fl(double v) { return (int)Math.Floor(v); }
        protected static double Hypot(double x, double y) { return Math.Sqrt(x * x + y * y); }
        protected int Ri(int a, int b) { return a + Fl(R.Next() * (b - a + 1)); }
        protected static double Hash2(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return h / 4294967296.0;
            }
        }
        static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        protected static bool Dith(int x, int y, double t) { return t * 16 > Bayer[((y & 3) << 2) | (x & 3)] + 0.5; }
        protected static double BayerAt(int x, int y) { return Bayer[((y & 3) << 2) | (x & 3)]; }

        // ---- framebuffer ----
        protected void Px(int x, int y, int c) { if ((uint)x < W && (uint)y < H) Fb[y * W + x] = c; }
        protected void Blend(int x, int y, int c, double a)
        {
            if (a <= 0 || (uint)x >= W || (uint)y >= H) return;
            int i = y * W + x;
            Fb[i] = Lerp(Fb[i], c, a > 1 ? 1 : a);
        }
        protected void Rect(int x, int y, int w, int h, int c)
        {
            int x0 = Math.Max(0, x), x1 = Math.Min(W, x + w), y1 = Math.Min(H, y + h);
            for (int j = Math.Max(0, y); j < y1; j++) for (int i = x0; i < x1; i++) Fb[j * W + i] = c;
        }
        // Soft round light, for lamps, neon and headlights.
        protected void Glow(double cx, double cy, double r, int c, double a, double sy = 1)
        {
            for (int y = Fl(cy - r * sy); y <= cy + r * sy; y++)
                for (int x = Fl(cx - r); x <= cx + r; x++)
                {
                    double dx = (x - cx) / r, dy = (y - cy) / (r * sy), d = dx * dx + dy * dy;
                    if (d < 1) { double q = 1 - Math.Sqrt(d); Blend(x, y, c, a * q * q); }
                }
        }
        protected void Line(int x0, int y0, int x1, int y1, int c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, e = dx + dy;
            for (;;)
            {
                Px(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * e;
                if (e2 >= dy) { e += dy; x0 += sx; }
                if (e2 <= dx) { e += dx; y0 += sy; }
            }
        }
        protected void Put(Spr s, int x, int y, bool flip = false)
        {
            for (int j = 0; j < s.H; j++) for (int i = 0; i < s.W; i++) { int c = s.D[j * s.W + (flip ? s.W - 1 - i : i)]; if (c != 0) Px(x + i, y + j, c); }
        }
        // Upside-down copy under a baseline, faded and rippled: wet-ground reflections.
        protected void PutReflect(Spr s, int x, int baseY, bool flip, double a, int f)
        {
            for (int j = 0; j < s.H; j++)
            {
                int ry = baseY + j, dx = (ry + (f >> 1)) % 4 == 0 ? 1 : 0;
                double k = 1 - (double)j / (s.H + 2);
                for (int i = 0; i < s.W; i++) { int c = s.D[(s.H - 1 - j) * s.W + (flip ? s.W - 1 - i : i)]; if (c != 0) Blend(x + i + dx, ry, c, a * k); }
            }
        }

        // ---- wrapping layers ----
        protected static void LPx(Spr L, int x, int y, int c) { x = ((x % L.W) + L.W) % L.W; if (y >= 0 && y < L.H) L.D[y * L.W + x] = c; }
        protected static void LRect(Spr L, int x, int y, int w, int h, int c) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) LPx(L, x + i, y + j, c); }
        protected static void LLine(Spr L, int x0, int y0, int x1, int y1, int c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, e = dx + dy;
            for (;;)
            {
                LPx(L, x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * e;
                if (e2 >= dy) { e += dy; x0 += sx; }
                if (e2 <= dx) { e += dx; y0 += sy; }
            }
        }
        protected static void LSpr(Spr L, Spr s, int x, int y) { for (int j = 0; j < s.H; j++) for (int i = 0; i < s.W; i++) { int c = s.D[j * s.W + i]; if (c != 0) LPx(L, x + i, y + j, c); } }
        // A layer scrolled by off layer pixels (parallax); layers are always wider than the screen.
        protected void Blit(Spr L, int off, int oy = 0)
        {
            int o = ((off % L.W) + L.W) % L.W;
            for (int y = 0; y < L.H; y++)
            {
                int sy = y + oy;
                if (sy < 0 || sy >= H) continue;
                int row = y * L.W, srow = sy * W;
                for (int x = 0; x < W; x++) { int lx = x + o; if (lx >= L.W) lx -= L.W; int c = L.D[row + lx]; if (c != 0) Fb[srow + x] = c; }
            }
        }
        // Lights the top edge of every silhouette (back-light from the sky).
        protected static void Rim(Spr L, int c)
        {
            for (int y = 0; y < L.H; y++) for (int x = 0; x < L.W; x++) { int i = y * L.W + x; if (L.D[i] != 0 && (y == 0 || L.D[i - L.W] == 0)) L.D[i] = c; }
        }
        // Layer x to screen x for a layer scrolled by off (handles the wrap seam).
        protected static int Sx(int wx, int off, int lw) { int s = (((wx - off) % lw) + lw) % lw; return s >= W ? s - lw : s; }

        // Bands of color: solid for the first part, dithered into the next.
        protected static void BakeBands(int[] buf, int[] ys, int[] cs, double solid)
        {
            for (int y = 0; y < H; y++)
            {
                int i = 0;
                while (i < ys.Length - 2 && y >= ys[i + 1]) i++;
                double t = (double)(y - ys[i]) / (ys[i + 1] - ys[i]);
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double tt = t < solid ? 0 : (t - solid) / (1 - solid);
                for (int x = 0; x < W; x++) buf[y * W + x] = Dith(x, y, tt) ? cs[i + 1] : cs[i];
            }
        }

        // ---- city ----
        protected sealed class Bld { public int X, Wd, Top; }
        protected sealed class Win { public int X, Y, Wd, Hd, C, K; public bool On, Glass; public double P; }
        protected sealed class WinPal { public int[] Lit, Hi, Tv; public int Dim, Curtain, Sil, Plant, Off; }
        protected sealed class Light { public int X, Y, Ph; }
        protected sealed class Sign { public int X, Y, Stut; public int[] On; }

        protected List<Bld> GenBuildings(int lw, int wMin, int wMax, int tMin, int tMax, double gapP, int gMin, int gMax)
        {
            List<Bld> o = new List<Bld>();
            int x = 0;
            for (;;)
            {
                int w = Ri(wMin, wMax);
                bool last = x + w > lw - wMin;
                if (last) w = lw - x;
                o.Add(new Bld { X = x, Wd = w, Top = Ri(tMin, tMax) });
                if (last) break;
                x += w;
                if (R.Next() < gapP) x += Ri(gMin, gMax);
                if (x > lw - wMin) { o[o.Count - 1].Wd += lw - x; break; }
            }
            return o;
        }
        // Window grid on a facade; each building keeps its own share of lit windows.
        protected void AddWindows(List<Win> list, Bld b, int mx, int my, int ww, int wh, int gx, int gy, double lit, int bottom = H)
        {
            int cols = Fl((double)(b.Wd - 2 * mx + gx) / (ww + gx));
            if (cols < 1) return;
            int used = cols * (ww + gx) - gx, x0 = b.X + Fl((b.Wd - used) / 2.0);
            double p = Math.Min(0.9, lit * (0.25 + R.Next() * 1.5));
            for (int y = b.Top + my; y + wh <= bottom; y += wh + gy)
                for (int c = 0; c < cols; c++)
                {
                    double r = R.Next();
                    list.Add(new Win { X = x0 + c * (ww + gx), Y = y, Wd = ww, Hd = wh, On = R.Next() < p, P = p, C = Ri(0, 7), Glass = R.Next() < 0.55, K = r < 0.46 ? 0 : r < 0.6 ? 1 : r < 0.7 ? 2 : r < 0.8 ? 3 : r < 0.9 ? 4 : 5 });
                }
        }
        protected void Toggle(List<Win> list, double q) { foreach (Win w in list) if (R.Next() < q) w.On = R.Next() < w.P; }
        protected static void ClearWins(List<Win> list, int x0, int y0, int x1, int y1) { list.RemoveAll(w => w.X + w.Wd > x0 && w.X < x1 && w.Y + w.Hd > y0 && w.Y < y1); }
        // Window looks: 0 plain, 1 curtain, 2 person, 3 blinds, 4 tv, 5 plant.
        protected void DrawWindow(Win wn, int x, int y, WinPal P, int f)
        {
            int w = wn.Wd, h = wn.Hd;
            if (!wn.On) { if (wn.Glass && P.Off != 0) Rect(x, y, w, h, P.Off); return; }
            int c = P.Lit[wn.C % P.Lit.Length];
            if (wn.K == 4 && w * h >= 6)
            {
                c = P.Tv[(int)(Hash2(wn.X * 7 + wn.Y, f / 3) * P.Tv.Length)];
                Rect(x, y, w, h, P.Dim); Rect(x, y + 1, w, h - 1, c);
                return;
            }
            Rect(x, y, w, h, c);
            if (w < 3 || h < 3) return;
            if (wn.K == 1) { Rect(x, y, 1, h, P.Curtain); Rect(x + w - 1, y, 1, h, P.Curtain); }
            else if (wn.K == 2) { Px(x + 1, y + h - 2, P.Sil); Rect(x, y + h - 1, w, 1, P.Sil); }
            else if (wn.K == 3) { for (int j = 1; j < h; j += 2) Rect(x, y + j, w, 1, P.Dim); }
            else if (wn.K == 5) { Px(x, y + h - 1, P.Plant); Px(x + 1, y + h - 1, P.Plant); Px(x + 1, y + h - 2, P.Plant); Px(x + w - 1, y + h - 1, P.Plant); }
            else Rect(x, y, w, 1, P.Hi[wn.C % P.Hi.Length]);
        }
        protected void DrawWindows(List<Win> list, int off, int lw, WinPal P, int f)
        {
            foreach (Win wn in list) { int x = Sx(wn.X, off, lw); if (x > -6 && x < W) DrawWindow(wn, x, wn.Y, P, f); }
        }
        protected static Spr WaterTower(int k, int r)
        {
            return new Spr(new[] { ".....k.....", "....kkk....", "...kkkkk...", "..kkkkkkk..", ".rrrrrrrrr.", ".kkkkkkkkk.", ".kkkkkkkkk.", ".rrrrrrrrr.",
                ".kkkkkkkkk.", ".kkkkkkkkk.", ".rrrrrrrrr.", "..k.....k..", "..kk...kk..", "..k.k.k.k..", "..k..k..k..", "..k.k.k.k..", "..k.....k.." }, Pal("kr", k, r));
        }
        protected static void Antenna(Spr L, int x, int roofY, int h, int c)
        {
            LLine(L, x, roofY - 1, x, roofY - h, c);
            LLine(L, x - 3, roofY - h + 1, x + 3, roofY - h + 1, c);
            LLine(L, x - 2, roofY - h + 3, x + 2, roofY - h + 3, c);
            LLine(L, x - 1, roofY - h + 5, x + 1, roofY - h + 5, c);
        }

        // ---- 3 × 5 pixel font ----
        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]> {
            { 'A', new[] { "111", "101", "111", "101", "101" } }, { 'B', new[] { "110", "101", "110", "101", "110" } }, { 'C', new[] { "111", "100", "100", "100", "111" } },
            { 'D', new[] { "110", "101", "101", "101", "110" } }, { 'E', new[] { "111", "100", "110", "100", "111" } }, { 'F', new[] { "111", "100", "110", "100", "100" } },
            { 'G', new[] { "111", "100", "101", "101", "111" } }, { 'H', new[] { "101", "101", "111", "101", "101" } }, { 'I', new[] { "111", "010", "010", "010", "111" } },
            { 'L', new[] { "100", "100", "100", "100", "111" } }, { 'M', new[] { "10001", "11011", "10101", "10001", "10001" } }, { 'N', new[] { "1001", "1101", "1011", "1001", "1001" } },
            { 'O', new[] { "111", "101", "101", "101", "111" } }, { 'P', new[] { "111", "101", "111", "100", "100" } }, { 'R', new[] { "110", "101", "110", "101", "101" } },
            { 'S', new[] { "111", "100", "111", "001", "111" } }, { 'T', new[] { "111", "010", "010", "010", "010" } }, { 'U', new[] { "101", "101", "101", "101", "111" } },
            { '0', new[] { "111", "101", "101", "101", "111" } }, { '1', new[] { "010", "110", "010", "010", "111" } }, { '2', new[] { "111", "001", "111", "100", "111" } },
            { '3', new[] { "111", "001", "011", "001", "111" } }, { '4', new[] { "101", "101", "111", "001", "001" } }, { '5', new[] { "111", "100", "111", "001", "111" } },
            { '6', new[] { "111", "100", "111", "101", "111" } }, { '7', new[] { "111", "001", "010", "010", "010" } }, { '8', new[] { "111", "101", "111", "101", "111" } },
            { '9', new[] { "111", "101", "111", "001", "111" } }, { ' ', new[] { "00", "00", "00", "00", "00" } }, { '-', new[] { "000", "000", "111", "000", "000" } } };
        static string[] Glyph(char ch) { string[] g; return Font.TryGetValue(ch, out g) ? g : Font[' ']; }
        protected static int TextW(string s) { int w = 0; foreach (char ch in s) w += Glyph(ch)[0].Length + 1; return w - 1; }
        protected void Text(string s, int x, int y, int c)
        {
            foreach (char ch in s)
            {
                string[] g = Glyph(ch);
                for (int j = 0; j < g.Length; j++) for (int i = 0; i < g[j].Length; i++) if (g[j][i] == '1') Px(x + i, y + j, c);
                x += g[0].Length + 1;
            }
        }
        // Neon sign: lit letters with a soft glow; a dark letter is a flicker.
        protected void Neon(string str, int x, int y, bool vertical, bool[] on, int lit, int dark, int glowC)
        {
            List<int[]> pts = new List<int[]>();
            int cx = x, cy = y;
            for (int k = 0; k < str.Length; k++)
            {
                string[] g = Glyph(str[k]);
                for (int j = 0; j < g.Length; j++) for (int i = 0; i < g[j].Length; i++) if (g[j][i] == '1') pts.Add(new[] { cx + i, cy + j, on[k] ? 1 : 0 });
                if (vertical) cy += 6; else cx += g[0].Length + 1;
            }
            foreach (int[] p in pts)
                if (p[2] != 0)
                    for (int j = -2; j <= 2; j++) for (int i = -2; i <= 2; i++) { int d = Math.Abs(i) + Math.Abs(j); if (d != 0) Blend(p[0] + i, p[1] + j, glowC, d == 1 ? 0.16 : d == 2 ? 0.08 : 0.04); }
            foreach (int[] p in pts) Px(p[0], p[1], p[2] != 0 ? lit : dark);
        }
        protected static bool[] Lit(Sign n, int f) { return n.On.Select(v => v > 0 && !(n.Stut > 0 && (f & 1) != 0)).ToArray(); }
        protected void Flicker(Sign n, int max, double stutter, int s0, int s1)
        {
            for (int k = 0; k < n.On.Length; k++) { int v = n.On[k]; n.On[k] = v < 1 ? v + 1 : (R.Next() < 0.003 ? -Ri(1, max) : 1); }
            if (n.Stut > 0) n.Stut--; else if (R.Next() < stutter) n.Stut = Ri(s0, s1);
        }
    }

    // ===== NOCHE: sunset fading into night, dark city, rooftop cats. =====
    sealed class Night : Scene
    {
        const int NW = 640;
        readonly int near = Col("#0a0612"), nearRim = Col("#2a1a36"), nearDetail = Col("#1a1124"), board = Col("#120914"), mid = Col("#190f2a"), midRim = Col("#2f1d43"),
            far = Col("#2c1b41"), farRim = Col("#3e2854"), cat = Col("#050209"), eye = Col("#d8f65c"), eyeAmber = Col("#ffcf4a"), red = Col("#ff4040"), redDim = Col("#6a1c2a"),
            white = Col("#f6f2ff"), pink = Col("#ff63b1"), pinkOff = Col("#3d172f"), pinkGlow = Col("#ff3d95"), cyan = Col("#67f2ff"), cyanOff = Col("#15343e"), cyanGlow = Col("#2fc6ff"),
            bulbOff = Col("#3a2430"), bird = Col("#150c1f"), planeC = Col("#2b2342"), door = Col("#e0954a"), doorHi = Col("#ffc070");
        readonly int[] star = Cols("#3d3b72", "#7a77b6", "#cbc8f4", "#ffffff"), bulbs = Cols("#ffd47a", "#ff8e5c", "#fff1ba", "#ffb05c"), steamC = Cols("#62507a", "#4a3c62", "#382c4e", "#2a2140");
        readonly WinPal win = new WinPal { Lit = Cols("#ffd27a", "#ffbe63", "#ff9f57", "#ffe3a0", "#ffc970", "#f6a85a", "#ffd88e", "#ffb060"), Hi = Cols("#ffeab8", "#ffdc96", "#ffc27f"), Dim = Col("#5a3420"), Curtain = Col("#c4683a"), Sil = Col("#2a1410"), Plant = Col("#1a2414"), Tv = Cols("#4d86e8", "#7fb0ff", "#3a62c4", "#a2c8ff"), Off = Col("#140c1e") };
        readonly WinPal midWin = new WinPal { Lit = Cols("#c88848", "#e0a356", "#a86c3e", "#d49450", "#7d9bd6"), Hi = Cols("#e8b468"), Dim = Col("#3a2418"), Curtain = Col("#8a5030"), Sil = Col("#2a1410"), Plant = Col("#1a2010"), Tv = Cols("#4a72c0", "#6a92da") };
        readonly WinPal farWin = new WinPal { Lit = Cols("#6a4060", "#a06650", "#c88a5a"), Hi = Cols("#c88a5a") };
        readonly int[] sky = new int[W * H];

        sealed class Star { public int X, Y, Per, Fade; public double B, Ph, Fph; public bool Big; }
        sealed class Item { public Spr S; public int X, Y; }
        sealed class Bank { public readonly List<Item> Items = new List<Item>(); public int Lw; }
        sealed class Ledge { public int X0, X1, Y; }
        sealed class Cloth { public int X, Len, Y; public List<int[]> Items; }
        sealed class Bulbs { public int X0, X1, Y; }
        sealed class Cat { public string St = "gone"; public int T, Li, Dir = 1, Blink = 50, Step, Look, Age, Jd, Jt, Nli; public double X, Y, Jx0, Jy0, Jx1, Jy1; public bool Amber; }
        sealed class WinCat { public Win W; public int T, Look, Blink = 40; public bool On; }
        sealed class Mover { public bool On; public int T, Dir = 1, N = 4; public double X, Y; }
        sealed class Puff { public double X, Y, Vx; public int L, Life; }

        readonly List<Star> stars = new List<Star>();
        readonly Bank cloudHi, cloudMid, streaks;
        readonly Spr Lf = new Spr(480, H), Lm = new Spr(560, H), Ln = new Spr(NW, H), Lfront = new Spr(NW, H);
        readonly List<Light> farLights = new List<Light>(), midLights = new List<Light>(), vents = new List<Light>();
        readonly List<Bulbs> strings = new List<Bulbs>();
        readonly List<Win> farWins = new List<Win>(), midWins = new List<Win>(), nearWins = new List<Win>(), doors = new List<Win>();
        List<Ledge> ledges = new List<Ledge>();
        readonly List<Cloth> clothes = new List<Cloth>();
        readonly Sign hotel, bar;
        readonly List<Spr> catWalk = new List<Spr>(), catSit = new List<Spr>();
        readonly Spr catJump;
        readonly Cat[] cats = new Cat[3];
        readonly WinCat[] winCats = new WinCat[2];
        readonly Mover plane = new Mover { T = 120 }, flock = new Mover { T = 40, Dir = -1 };
        readonly Spr[] birdS;
        readonly List<Puff> steam = new List<Puff>();

        public Night()
        {
            R = new Rng(112358);
            BakeBands(sky, new[] { 0, 20, 37, 52, 65, 76, 86, 95, 103, 110, 116, 122, 128, 136 },
                Cols("#09091d", "#0e0f2a", "#161536", "#201944", "#2e1c4f", "#421f56", "#5b2557", "#792e55", "#9a3a50", "#b94f48", "#d46a41", "#e88b40", "#f3ad4f", "#f9c862"), 0.55);
            BakeSun();
            for (int k = 0; k < 130; k++)
                stars.Add(new Star { X = Ri(0, W - 1), Y = Fl(Math.Pow(R.Next(), 1.6) * 84), B = R.Next(), Per = Ri(16, 80), Ph = R.Next() * 6.28, Big = R.Next() < 0.07, Fade = R.Next() < 0.35 ? Ri(240, 700) : 0, Fph = R.Next() });
            cloudHi = MakeBank(5, 56, 96, 15, 21, 4, 28, Cols("#16142f", "#1e1a3c", "#2c2148", "#46295a"), false);
            cloudMid = MakeBank(6, 40, 74, 11, 16, 40, 68, Cols("#2c1c48", "#3b2153", "#64295a", "#b44d5e"), false);
            streaks = MakeBank(10, 50, 130, 0, 0, 86, 112, Cols("#5e2850", "#7e3157", "#b34c55", "#f4a05c"), true);

            // far skyline
            List<Bld> fbl = GenBuildings(480, 9, 24, 94, 128, 0.22, 1, 3);
            for (int i = 0; i < fbl.Count; i++)
            {
                Bld b = fbl[i];
                if (i % 7 == 3) b.Top = Ri(64, 74);
                LRect(Lf, b.X, b.Top, b.Wd, H - b.Top, far);
                if (b.Top < 78)
                {
                    LRect(Lf, b.X + 2, b.Top - 3, b.Wd - 4, 3, far); LRect(Lf, b.X + 4, b.Top - 6, Math.Max(1, b.Wd - 8), 3, far);
                    int ax = b.X + (b.Wd >> 1);
                    LLine(Lf, ax, b.Top - 6, ax, b.Top - 18, far);
                    farLights.Add(new Light { X = ax, Y = b.Top - 19, Ph = Ri(0, 40) });
                }
                else if (R.Next() < 0.3) { int ax = b.X + Ri(2, b.Wd - 3); LLine(Lf, ax, b.Top - 1, ax, b.Top - Ri(4, 9), far); }
                else if (R.Next() < 0.3) LRect(Lf, b.X + 2, b.Top - 3, b.Wd - 4, 3, far);
                for (int y = b.Top + 3; y < H; y += 3) for (int x = b.X + 2; x < b.X + b.Wd - 2; x += 3) if (R.Next() < 0.09) farWins.Add(new Win { X = x, Y = y, Wd = 1, Hd = 1, On = R.Next() < 0.7, P = 0.7, C = Ri(0, 2) });
            }
            Rim(Lf, farRim);

            // mid skyline
            Spr tank = new Spr(new[] { "...k...", "..kkk..", ".kkkkk.", ".kkkkk.", ".kkkkk.", ".kkkkk.", ".k...k.", ".kk.kk.", ".k.k.k." }, Pal("k", mid));
            foreach (Bld b in GenBuildings(560, 12, 30, 102, 138, 0.3, 1, 4))
            {
                LRect(Lm, b.X, b.Top, b.Wd, H - b.Top, mid);
                double r = R.Next();
                if (r < 0.25 && b.Wd > 9) LSpr(Lm, tank, b.X + Ri(1, b.Wd - 8), b.Top - 9);
                else if (r < 0.45) { int ax = b.X + Ri(2, b.Wd - 3), h = Ri(7, 14); Antenna(Lm, ax, b.Top, h, mid); if (h > 11) midLights.Add(new Light { X = ax, Y = b.Top - h - 1, Ph = Ri(0, 50) }); }
                else if (r < 0.6) { LRect(Lm, b.X + 2, b.Top - 3, 4, 3, mid); if (b.Wd > 12) LRect(Lm, b.X + 8, b.Top - 2, 3, 2, mid); }
                else if (r < 0.75 && b.Wd > 14) LRect(Lm, b.X + 3, b.Top - 5, b.Wd - 6, 5, mid);
                AddWindows(midWins, b, 2, 4, 2, 2, 2, 3, 0.22);
            }
            Rim(Lm, midRim);

            // near skyline: rooftops full of things
            Spr tower = WaterTower(near, nearDetail);
            Spr dish = new Spr(new[] { "kk....", "kkk...", ".kkk.k", "..kkk.", "...kk.", "...k..", "..kkk." }, Pal("k", near));
            string[] feats = { "tower", "clothes", "lights", "vent", "stairs", "tower", "dish", "clothes", "antenna", "lights", "stairs", "vent" };
            List<Bld> nb = GenBuildings(NW, 28, 56, 118, 150, 0.5, 3, 9);
            for (int i = 0; i < nb.Count; i++)
            {
                Bld b = nb[i];
                LRect(Ln, b.X, b.Top, b.Wd, H - b.Top, near);
                LRect(Ln, b.X - 1, b.Top + 2, b.Wd + 2, 1, near);
                LPx(Ln, b.X, b.Top - 1, near); LPx(Ln, b.X + b.Wd - 1, b.Top - 1, near);
                ledges.Add(new Ledge { X0 = b.X + 1, X1 = b.X + b.Wd - 2, Y = b.Top - 1 });
                string ft = feats[i % feats.Length];
                int t = b.Top;
                if (ft == "tower") LSpr(Ln, tower, b.X + Ri(2, b.Wd - 13), t - tower.H);
                else if (ft == "clothes" && b.Wd >= 22)
                {
                    int x0 = b.X + 3, len = Math.Min(b.Wd - 7, 24);
                    LLine(Ln, x0, t - 1, x0, t - 9, near); LLine(Ln, x0 + len, t - 1, x0 + len, t - 9, near);
                    List<int[]> items = new List<int[]>();
                    int[] step = { 5, 4, 4, 2 };
                    for (int x = 3; x < len - 2;) { int k = Ri(0, 3); items.Add(new[] { x, k }); x += step[k] + Ri(1, 2); }
                    clothes.Add(new Cloth { X = x0, Len = len, Y = t - 8, Items = items });
                }
                else if (ft == "lights" && b.Wd >= 20)
                {
                    int x0 = b.X + 2, x1 = b.X + b.Wd - 3;
                    LLine(Ln, x0, t - 1, x0, t - 10, near); LLine(Ln, x1, t - 1, x1, t - 10, near);
                    for (int k = 0; k <= x1 - x0; k++) { double u = (double)k / (x1 - x0); LPx(Ln, x0 + k, t - 10 + Rd(16 * u * (1 - u)), near); }
                    strings.Add(new Bulbs { X0 = x0, X1 = x1, Y = t - 10 });
                    LRect(Ln, b.X + 6, t - 3, 3, 3, near); LRect(Ln, b.X + 12, t - 2, 5, 2, near);
                }
                else if (ft == "vent")
                {
                    int x = b.X + Ri(4, b.Wd - 8);
                    LRect(Ln, x, t - 6, 2, 6, near); LRect(Ln, x - 1, t - 7, 4, 1, near); vents.Add(new Light { X = x + 1, Y = t - 8 });
                    LRect(Ln, x + 5, t - 3, 5, 3, near);
                }
                else if (ft == "stairs")
                {
                    int x = b.X + Ri(2, b.Wd - 12);
                    LRect(Ln, x, t - 7, 9, 7, near); LRect(Ln, x - 1, t - 8, 11, 1, near);
                    doors.Add(new Win { X = x + 5, Y = t - 6, Wd = 2, Hd = 5, On = R.Next() < 0.8, P = 0.8 });
                }
                else if (ft == "dish") { LSpr(Ln, dish, b.X + Ri(2, b.Wd - 9), t - 7); LRect(Ln, b.X + b.Wd - 9, t - 3, 5, 3, near); }
                else Antenna(Ln, b.X + Ri(4, b.Wd - 5), t, Ri(9, 15), near);
                if (b.Wd > 40 && ft != "lights" && ft != "clothes") Antenna(Ln, b.X + b.Wd - 4, t, Ri(6, 11), near);
                double st = R.Next();
                if (st < 0.5) AddWindows(nearWins, b, 3, 6, 3, 4, 3, 4, 0.36);
                else if (st < 0.8) AddWindows(nearWins, b, 3, 6, 5, 3, 3, 5, 0.34);
                else AddWindows(nearWins, b, 3, 6, 2, 5, 2, 4, 0.3);
            }
            // neon signs: a vertical HOTEL and a small BAR, with no windows behind them
            Func<int, int, int, Bld> pick = (minW, maxTop, skip) => nb.Where((b, i) => i > skip && b.Wd >= minW && b.Top <= maxTop).FirstOrDefault() ?? nb[skip + 1];
            Bld bh = pick(36, 132, 1), bb = pick(30, 140, nb.IndexOf(bh) + 1);
            hotel = new Sign { X = bh.X + bh.Wd - 8, Y = bh.Top + 9, On = new[] { 1, 1, 1, 1, 1 } };
            bar = new Sign { X = bb.X + ((bb.Wd - 13) >> 1), Y = bb.Top + 22, On = new[] { 1, 1, 1 } };
            ClearWins(nearWins, hotel.X - 3, hotel.Y - 3, hotel.X + 8, hotel.Y + 34);
            ClearWins(nearWins, bar.X - 3, bar.Y - 3, bar.X + 16, bar.Y + 10);
            LRect(Ln, hotel.X, hotel.Y - 1, 5, 31, board); LRect(Ln, hotel.X + 5, hotel.Y + 2, 2, 1, nearDetail); LRect(Ln, hotel.X + 5, hotel.Y + 26, 2, 1, nearDetail);
            LRect(Ln, bar.X, bar.Y, 13, 7, board);
            foreach (Win w in nearWins) if (w.Wd >= 3 && R.Next() < 0.06) LRect(Lfront, w.X, w.Y + w.Hd + 1, 3, 2, nearDetail); // wall AC boxes
            Rim(Ln, nearRim);

            // rooftop cats: only the eyes really show
            ledges = ledges.OrderBy(l => l.X0).ToList();
            string[] walkBody = { ".........k.k", "k........kkk", ".k.kkkkkkkkk", "..kkkkkkkkk.", "..kkkkkkkk.." };
            foreach (string legs in new[] { "..k.k...k.k.", "...kk....kk.", "..k..k..k..k", "...kk....kk." })
            {
                catWalk.Add(new Spr(walkBody.Concat(new[] { legs }).ToArray(), Pal("k", cat)));
                catWalk.Add(new Spr(new[] { "k........k.k", ".k.......kkk", ".k.kkkkkkkkk", "..kkkkkkkkk.", "..kkkkkkkk..", legs }, Pal("k", cat)));
            }
            string[] sitBody = { "k...k..", "kk.kk..", "kkkkk..", "kkkkk..", ".kkk...", ".kkkk..", "kkkkk..", "kkkkk.." };
            int[][] tails = { new[] { 5, 7, 6, 7, 6, 6 }, new[] { 5, 7, 6, 6, 6, 5 }, new[] { 5, 7, 6, 6, 6, 5, 6, 4 }, new[] { 5, 7, 6, 6, 6, 5 } };
            foreach (int[] tail in tails)
            {
                char[][] rows = sitBody.Select(r => r.ToCharArray()).ToArray();
                for (int k = 0; k < tail.Length; k += 2) rows[tail[k + 1]][tail[k]] = 'k';
                catSit.Add(new Spr(rows.Select(r => new string(r)).ToArray(), Pal("k", cat)));
            }
            catJump = new Spr(new[] { "..........k.k", "k.........kkk", ".kkkkkkkkkkkk", "..kkkkkkkkkk.", ".k.........k." }, Pal("k", cat));
            for (int k = 0; k < 3; k++) cats[k] = new Cat { T = 10 + k * 40, Amber = k == 1 };
            for (int k = 0; k < 2; k++) winCats[k] = new WinCat { T = 60 + k * 150 };
            birdS = new[] { new Spr(new[] { "k...k", ".k.k.", "..k.." }, Pal("k", bird)), new Spr(new[] { ".....", "kk.kk", "..k.." }, Pal("k", bird)), new Spr(new[] { "..k..", ".k.k.", "k...k" }, Pal("k", bird)) };
        }

        void BakeSun()
        {
            int hi = Col("#fff7d6"), md = Col("#ffe49e"), lo = Col("#ffc46a"), gl = Col("#ffb45a"), ring = Col("#ffd98a");
            const int SX = 230, SY = 108, SR = 25, G = SR + 34;
            for (int y = SY - G; y <= SY + G; y++)
                for (int x = SX - G; x <= SX + G; x++)
                {
                    if (x < 0 || x >= W || y < 0 || y >= H) continue;
                    double dx = x - SX, dy = (y - SY) * 1.25, d = Math.Sqrt(dx * dx + dy * dy), ds = Hypot(x - SX, y - SY);
                    int i = y * W + x;
                    if (ds <= SR + 0.4)
                    {
                        double v = (y - (SY - SR)) / (2.0 * SR);
                        sky[i] = v < 0.3 ? hi : v < 0.44 ? (Dith(x, y, (v - 0.3) / 0.14) ? md : hi) : v < 0.64 ? md : v < 0.78 ? (Dith(x, y, (v - 0.64) / 0.14) ? lo : md) : lo;
                    }
                    else if (ds <= SR + 2.5) sky[i] = Lerp(sky[i], ring, Dith(x, y, 0.5) ? 0.75 : 0.45);
                    else { double t = (d - SR) / (G - SR); if (t < 1) sky[i] = Lerp(sky[i], gl, 0.5 * (1 - t) * (1 - t)); }
                }
            // thin crescent moon high on the left, with a cold halo
            const int mx = 54, my = 24, mr = 6;
            int moon = Col("#f3ecd6"), moonLo = Col("#cfc6b4"), mg = Col("#8c86c8");
            for (int y = my - 20; y <= my + 20; y++)
                for (int x = mx - 20; x <= mx + 20; x++)
                {
                    double d = Hypot(x - mx, y - my), d2 = Hypot(x - mx - 3, y - my + 2);
                    int i = y * W + x;
                    if (d <= mr + 0.3 && d2 > mr - 0.2) sky[i] = (x - mx) < -3 || (y - my) > 3 ? moonLo : moon;
                    else if (d > mr && d < 20) sky[i] = Lerp(sky[i], mg, 0.16 * (1 - d / 20) * (1 - d / 20));
                }
        }

        // Cumulus made of round puffs, lit from below by the low sun (dark tops, glowing bellies).
        Spr MakeCloud(int w, int h, int[] pal)
        {
            List<double[]> main = new List<double[]>(), small = new List<double[]>();
            int n = 3 + w / 22;
            for (int k = 0; k < n; k++)
            {
                double u = (k + 0.5) / n, env = 0.55 + 0.45 * Math.Sin(Math.PI * u);
                double r = h * (0.42 + R.Next() * 0.3) * env;
                double cx = u * w + (R.Next() - 0.5) * ((double)w / n) * 0.5;
                main.Add(new[] { cx, h - 1 - r * (0.55 + R.Next() * 0.2), r });
            }
            for (int k = 0; k < 2 + Ri(0, 2); k++)
            {
                double r = h * (0.22 + R.Next() * 0.14);
                small.Add(new[] { (k & 1) != 0 ? w - r - R.Next() * w * 0.2 : r + R.Next() * w * 0.2, h - 1 - r * 0.5, r });
            }
            for (int k = 0; k < main.Count; k++) { int j = Ri(0, main.Count - 1); double[] t = main[k]; main[k] = main[j]; main[j] = t; }
            List<double[]> puffs = main.Concat(small).ToList();
            short[] own = new short[w * h];
            for (int i = 0; i < own.Length; i++) own[i] = -1;
            for (int k = 0; k < puffs.Count; k++)
            {
                double[] p = puffs[k];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { double dx = x - p[0], dy = (y - p[1]) * 1.1; if (dx * dx + dy * dy <= p[2] * p[2]) own[y * w + x] = (short)k; }
            }
            Spr s = new Spr(w, h);
            double[] th = { -0.42, 0.12, 0.52 };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int k = own[y * w + x];
                    if (k < 0) continue;
                    double[] p = puffs[k];
                    double l = (y - p[1]) / p[2] * 0.9 + (x - p[0]) / p[2] * 0.32, j = ((x + y) & 1) != 0 ? 0.06 : -0.06;
                    int t = 0;
                    foreach (double q in th) if (l + j > q) t++;                    // checker dither only along tone borders
                    int up = y > 0 ? own[(y - 1) * w + x] : -1;
                    if (up >= 0 && up != k) t = Math.Max(0, t - 1);                  // seam where a front puff overlaps
                    if (up < 0 && t > 1) t = 1;                                      // tops stay in shadow
                    if (y + 1 >= h || own[(y + 1) * w + x] < 0) t = 3;               // lit underside
                    else if (y + 2 >= h || own[(y + 2) * w + x] < 0) t = Math.Max(t, 2);
                    s.D[y * w + x] = pal[t];
                }
            return s;
        }
        // Long thin streaks near the horizon; they slide across the sun.
        Spr MakeStreak(int w, int[] pal)
        {
            const int h = 6;
            Spr s = new Spr(w, h);
            double a = R.Next() * 6, b = R.Next() * 6;
            for (int x = 0; x < w; x++)
            {
                double env = Math.Pow(Math.Sin(Math.PI * x / (w - 1)), 0.6);
                int t = Math.Max(0, Rd(env * (2.4 + 1.4 * Math.Sin(x * 0.09 + a) + 0.9 * Math.Sin(x * 0.27 + b))));
                for (int j = 0; j < Math.Min(t, h); j++)
                {
                    int c = j == 0 ? pal[3] : j == 1 ? (Dith(x, j, 0.5) ? pal[3] : pal[2]) : j == 2 ? pal[2] : pal[1];
                    if (j == t - 1 && j > 0) c = j > 2 ? pal[0] : pal[1];
                    s.D[(h - 1 - j) * w + x] = c;
                }
            }
            return s;
        }
        Bank MakeBank(int n, int w0, int w1, int h0, int h1, int y0, int y1, int[] pal, bool streak)
        {
            Bank bk = new Bank();
            int x = Ri(0, 30);
            for (int k = 0; k < n; k++)
            {
                int w = Ri(w0, w1), h = Ri(h0, h1);
                Spr s = streak ? MakeStreak(w, pal) : MakeCloud(w, h, pal);
                bk.Items.Add(new Item { S = s, X = x, Y = Ri(y0, y1) });
                x += w + Ri(14, 70);
            }
            bk.Lw = Math.Max(x, 680);
            return bk;
        }
        void DrawBank(Bank bk, int off) { foreach (Item it in bk.Items) { int x = Sx(it.X, off, bk.Lw); if (x < W && x + it.S.W > 0) Put(it.S, x, it.Y); } }

        List<int> LedgesOnScreen(int off) { List<int> r = new List<int>(); for (int i = 0; i < ledges.Count; i++) { int x = Sx(ledges[i].X0, off, NW); if (x > 10 && x < W - 40) r.Add(i); } return r; }
        void CatUpdate(Cat c, int off)
        {
            c.Age++;
            if (--c.Blink < -2) c.Blink = Ri(40, 170);
            Ledge lg = ledges[c.Li];
            if (c.St == "gone")
            {
                if (--c.T <= 0)
                {
                    List<int> vis = LedgesOnScreen(off);
                    if (vis.Count > 0) { c.Li = vis[Ri(0, vis.Count - 1)]; Ledge l = ledges[c.Li]; c.X = Ri(l.X0 + 3, Math.Max(l.X0 + 3, l.X1 - 8)); c.St = "sit"; c.T = Ri(70, 220); c.Age = 0; }
                    else c.T = 30;
                }
            }
            else if (c.St == "sit")
            {
                if (R.Next() < 0.012) c.Look = Ri(-1, 1);
                if (--c.T <= 0) { if (R.Next() < 0.22) { c.St = "gone"; c.T = Ri(90, 260); } else { c.St = "walk"; c.Dir = R.Next() < 0.5 ? -1 : 1; c.T = Ri(80, 320); } }
            }
            else if (c.St == "walk")
            {
                c.X += c.Dir * 0.5; c.Step++;
                if ((c.Dir > 0 && c.X >= lg.X1 - 11) || (c.Dir < 0 && c.X <= lg.X0))
                {
                    int ni = (c.Li + c.Dir + ledges.Count) % ledges.Count;
                    Ledge tg = ledges[ni];
                    int d = tg.X0 - lg.X0;
                    if (c.Dir > 0 && d < 0) d += NW;
                    if (c.Dir < 0 && d > 0) d -= NW;
                    int tx0 = lg.X0 + d, tx1 = tx0 + (tg.X1 - tg.X0), gap = c.Dir > 0 ? tx0 - (lg.X1 + 1) : lg.X0 - (tx1 + 1), dy = tg.Y - lg.Y;
                    if (gap >= 0 && gap <= 14 && Math.Abs(dy) <= 20 && R.Next() < 0.85)
                    { c.St = "jump"; c.Jx0 = c.X; c.Jy0 = lg.Y; c.Jx1 = c.Dir > 0 ? tx0 + 1 : tx1 - 12; c.Jy1 = tg.Y; c.Jd = 7 + (gap >> 1); c.Jt = 0; c.Nli = ni; }
                    else if (R.Next() < 0.5) c.Dir = -c.Dir;
                    else { c.St = "sit"; c.T = Ri(60, 200); }
                }
                else if (--c.T <= 0) { c.St = "sit"; c.T = Ri(60, 220); }
            }
            else if (c.St == "jump")
            {
                double k = (double)++c.Jt / c.Jd;
                c.X = c.Jx0 + (c.Jx1 - c.Jx0) * k;
                c.Y = c.Jy0 + (c.Jy1 - c.Jy0) * k - (5 + Math.Max(0, c.Jy0 - c.Jy1)) * 4 * k * (1 - k);
                if (k >= 1) { c.Li = c.Nli; c.X = (((int)c.Jx1 % NW) + NW) % NW; c.St = "walk"; c.T = Ri(30, 160); }
            }
        }
        void CatDraw(Cat c, int off, int f)
        {
            if (c.St == "gone") return;
            int y = c.St == "jump" ? Rd(c.Y) : ledges[c.Li].Y, x = Sx(Rd(c.X), off, NW);
            if (x < -14 || x > W) return;
            int eyeC = c.Amber ? eyeAmber : eye;
            bool open = c.Blink > 0 && !(c.Age < 8 && c.Age % 4 < 2);
            Spr s;
            if (c.St == "sit")
            {
                s = catSit[(f >> 3) % 4];
                Put(s, x, y - s.H + 1);
                if (open) { int lx = Math.Max(-1, Math.Min(1, c.Look)); Px(x + 1 + lx, y - 5, eyeC); Px(x + 3 + lx, y - 5, eyeC); }
                return;
            }
            int[] ex;
            if (c.St == "jump") { s = catJump; ex = new[] { 10, 12 }; }
            else { s = catWalk[((c.Step >> 2) % 4) * 2 + ((c.Step >> 4) & 1)]; ex = new[] { 9, 11 }; }
            bool fl = c.Dir < 0;
            Put(s, x, y - s.H + 1, fl);
            if (open) foreach (int e in ex) Px(x + (fl ? s.W - 1 - e : e), y - s.H + 2, eyeC);
        }
        // Cats peeking out of dark windows.
        void WinCatUpdate(WinCat c, int off)
        {
            if (--c.Blink < -2) c.Blink = Ri(30, 120);
            if (R.Next() < 0.02) c.Look = Ri(-1, 1);
            if (--c.T > 0) return;
            if (c.On) { c.On = false; c.T = Ri(200, 600); return; }
            List<Win> cand = nearWins.Where(w => !w.On && w.Wd >= 3 && w.Hd >= 3 && Sx(w.X, off, NW) > 20 && Sx(w.X, off, NW) < W - 30).ToList();
            if (cand.Count > 0) { c.W = cand[Ri(0, cand.Count - 1)]; c.On = true; c.T = Ri(120, 300); }
            else c.T = 60;
        }

        protected override void Update(int f)
        {
            int offN = f / 6;
            Toggle(nearWins, 0.0012); Toggle(midWins, 0.0015); Toggle(farWins, 0.002); Toggle(doors, 0.001);
            Flicker(hotel, 6, 0.0025, 4, 10);
            Flicker(bar, 6, 0.0025, 4, 10);
            foreach (Cat c in cats) CatUpdate(c, offN);
            foreach (WinCat c in winCats) WinCatUpdate(c, offN);
            if (!plane.On) { if (--plane.T <= 0) { plane.On = true; plane.Dir = R.Next() < 0.5 ? 1 : -1; plane.X = plane.Dir > 0 ? -8 : W + 8; plane.Y = Ri(10, 30); } }
            else { plane.X += plane.Dir * 0.4; if (plane.X < -10 || plane.X > W + 10) { plane.On = false; plane.T = Ri(500, 1000); } }
            if (!flock.On) { if (--flock.T <= 0) { flock.On = true; flock.Dir = R.Next() < 0.6 ? -1 : 1; flock.X = flock.Dir < 0 ? W + 6 : -6; flock.Y = Ri(44, 78); flock.N = Ri(3, 5); } }
            else { flock.X += flock.Dir * 0.9; flock.Y += Math.Sin(f * 0.08) * 0.15; if (flock.X < -40 || flock.X > W + 40) { flock.On = false; flock.T = Ri(350, 800); } }
            if (f % 4 == 0) foreach (Light v in vents) steam.Add(new Puff { X = v.X + R.Next() * 2 - 1, Y = v.Y, Life = Ri(34, 52), Vx = 0.06 + R.Next() * 0.08 });
            for (int k = steam.Count - 1; k >= 0; k--) { Puff s = steam[k]; s.L++; s.Y -= 0.3; s.X += s.Vx + Math.Sin((s.L + k) * 0.2) * 0.05; if (s.L > s.Life) steam.RemoveAt(k); }
        }

        static readonly int[][] FlockAt = { new[] { 0, 0 }, new[] { 7, -3 }, new[] { 7, 3 }, new[] { 14, -5 }, new[] { 14, 5 } };
        static readonly int[] Wing = { 0, 1, 2, 1 };
        protected override void Draw(int f)
        {
            Array.Copy(sky, Fb, Fb.Length);
            // stars: twinkle, some fade in and out over long cycles
            foreach (Star s in stars)
            {
                double v = s.B * 2.2 + Math.Sin(f * 6.283 / s.Per + s.Ph) * 1.1;
                if (s.Fade != 0)
                {
                    double cyc = ((double)f / s.Fade + s.Fph) % 1;
                    if (cyc > 0.75) continue;
                    if (cyc < 0.08 || cyc > 0.67) v = Math.Min(v, 1);
                }
                if (Math.Abs(s.X - 54) < 9 && Math.Abs(s.Y - 24) < 9) continue;
                double lim = s.Y < 34 ? 3 : s.Y < 52 ? 2 : s.Y < 70 ? 1 : 0.6;
                int lv = Math.Min(Rd(v), Fl(lim));
                if (lv <= 0) continue;
                Px(s.X, s.Y, star[lv]);
                if (s.Big && lv >= 3)
                {
                    int a = Math.Sin(f * 6.283 / s.Per + s.Ph) > 0.8 ? 2 : 1;
                    for (int k = 1; k <= a; k++) { int c = star[k == 1 ? 1 : 0]; Px(s.X - k, s.Y, c); Px(s.X + k, s.Y, c); Px(s.X, s.Y - k, c); Px(s.X, s.Y + k, c); }
                }
            }
            if (plane.On)
            {
                int x = Rd(plane.X), y = Rd(plane.Y);
                Rect(x - 2, y, 5, 1, planeC); Px(x, y - 1, planeC);
                if (f % 18 < 3) Px(x - 2 * plane.Dir, y, red);
                if (f % 26 == 0 || f % 26 == 3) Px(x + 2 * plane.Dir, y, white);
            }
            DrawBank(cloudHi, f / 36);
            DrawBank(cloudMid, f / 22);
            DrawBank(streaks, f / 15);
            if (flock.On)
                for (int k = 0; k < flock.N; k++) Put(birdS[Wing[((f >> 1) + k) % 4]], Rd(flock.X - flock.Dir * FlockAt[k][0]), Rd(flock.Y + FlockAt[k][1]));
            // far and mid city
            int offF = f / 30, offM = f / 14, offN = f / 6;
            Blit(Lf, offF);
            DrawWindows(farWins, offF, 480, farWin, f);
            foreach (Light l in farLights)
            {
                int x = Sx(l.X, offF, 480);
                if ((f + l.Ph) % 40 < 14) { Px(x, l.Y, red); Blend(x - 1, l.Y, red, 0.3); Blend(x + 1, l.Y, red, 0.3); }
                else Px(x, l.Y, redDim);
            }
            Blit(Lm, offM);
            DrawWindows(midWins, offM, 560, midWin, f);
            foreach (Light l in midLights) { int x = Sx(l.X, offM, 560); if ((f + l.Ph) % 50 < 16) { Px(x, l.Y, red); Glow(x, l.Y, 3, red, 0.35); } }
            // near city
            Blit(Ln, offN);
            DrawWindows(nearWins, offN, NW, win, f);
            foreach (Win d in doors) { int x = Sx(d.X, offN, NW); if (x > -4 && x < W && d.On) { Rect(x, d.Y, d.Wd, d.Hd, door); Px(x, d.Y, doorHi); Blend(x - 1, d.Y + d.Hd, door, 0.25); } }
            Blit(Lfront, offN);
            foreach (WinCat c in winCats) if (c.On && c.W != null && !c.W.On && c.Blink > 0) { int x = Sx(c.W.X, offN, NW) + 1 + c.Look; Px(x - 1, c.W.Y + 1, eye); Px(x + 1, c.W.Y + 1, eye); }
            // neon (a stutter makes the whole sign buzz)
            int hx = Sx(hotel.X, offN, NW);
            if (hx >= -20 && hx <= W + 4) Neon("HOTEL", hx + 1, hotel.Y, true, Lit(hotel, f), pink, pinkOff, pinkGlow);
            int bx = Sx(bar.X, offN, NW);
            if (bx >= -20 && bx <= W + 4) Neon("BAR", bx + 1, bar.Y + 1, false, Lit(bar, f), cyan, cyanOff, cyanGlow);
            // string lights on a roof terrace
            foreach (Bulbs s in strings)
            {
                int x0 = Sx(s.X0, offN, NW), len = s.X1 - s.X0;
                if (x0 > W || x0 + len < 0) continue;
                for (int k = 2; k < len - 1; k += 3)
                {
                    double u = (double)k / len;
                    int y = s.Y + Rd(16 * u * (1 - u)) + 1, ix = k / 3, c = bulbs[ix % 4];
                    if (((f >> 3) + ix * 3) % 11 != 0) { Px(x0 + k, y, c); Blend(x0 + k, y + 1, c, 0.3); Blend(x0 + k - 1, y, c, 0.2); Blend(x0 + k + 1, y, c, 0.2); }
                    else Px(x0 + k, y, bulbOff);
                }
            }
            // laundry swaying on the line
            foreach (Cloth cl in clothes)
            {
                int x0 = Sx(cl.X, offN, NW);
                if (x0 > W || x0 + cl.Len < 0) continue;
                for (int k = 0; k <= cl.Len; k++) { double u = (double)k / cl.Len; Px(x0 + k, cl.Y + Rd(8 * u * (1 - u)), near); }
                for (int n = 0; n < cl.Items.Count; n++)
                {
                    int[] it = cl.Items[n];
                    double u = (double)it[0] / cl.Len;
                    int y = cl.Y + Rd(8 * u * (1 - u)) + 1, x = x0 + it[0], sw = Rd(Math.Sin(f * 0.12 + n * 1.7) * 0.9), c = near;
                    if (it[1] == 0) { Rect(x, y, 5, 1, c); Rect(x + 1, y + 1, 3, 3, c); Px(x + 1 + sw, y + 4, c); Px(x + 3 + sw, y + 4, c); }                                  // shirt
                    else if (it[1] == 1) { Rect(x, y, 4, 2, c); Rect(x, y + 2, 1, 3, c); Rect(x + 3, y + 2, 1, 3, c); Px(x + sw, y + 5, c); Px(x + 3 + sw, y + 5, c); }      // trousers
                    else if (it[1] == 2) { Rect(x, y, 3, 4, c); Rect(x + sw, y + 4, 3, 1, c); }                                                                            // towel
                    else { Rect(x, y, 1, 2, c); Px(x + sw, y + 2, c); }                                                                                                    // sock
                    Px(x, y - 1, nearRim);
                }
            }
            // steam from rooftop vents
            foreach (Puff s in steam)
            {
                double t = (double)s.L / s.Life;
                int x = Sx(Rd(s.X), offN, NW), y = Rd(s.Y);
                if (!Dith(x, y, 1 - t * 0.85)) continue;
                int c = steamC[Math.Min(3, (int)(t * 4))];
                Px(x, y, c);
                if (t > 0.25) Px(x + 1, y, c);
                if (t > 0.5 && Dith(x, y + 1, 0.5)) Px(x, y - 1, c);
            }
            foreach (Cat c in cats) CatDraw(c, offN, f);
        }
    }

    // ===== LLUVIA: wet blue city, layered rain, puddles, cars, umbrellas. =====
    sealed class Rain : Scene
    {
        const int NW = 640, GY = 146, CURB = 154, ROAD = 155, GH = H - GY;   // sidewalk 146-153, curb 154, road 155-179
        readonly int near = Col("#0a1018"), nearRim = Col("#1c2938"), ledge = Col("#1d2a39"), ledgeHi = Col("#33475c"), detail = Col("#162130"), esc = Col("#1b2735"),
            mid = Col("#111b27"), midRim = Col("#1c2a3a"), far = Col("#1b2839"), farRim = Col("#24344a"),
            side = Col("#161f2a"), sideLine = Col("#111821"), curb = Col("#2b3a4b"), road = Col("#0c1219"), roadDk = Col("#080c12"), roadHi = Col("#111923"), lane = Col("#3a4756"), gutter = Col("#0e161f"),
            lit = Col("#f3dfa6"), lampHead = Col("#334050"), lamp = Col("#fff1c4"), lampGlow = Col("#f2c674"), lampGlow2 = Col("#ffdb8a"),
            shopC = Col("#eba65a"), shopHi = Col("#ffd38c"), shopDark = Col("#6a4024"), awnA = Col("#25423f"), awnB = Col("#6a6556"),
            neon = Col("#5ff3e4"), neonOff = Col("#163a38"), neonGlow = Col("#2fd8d0"), cross = Col("#52ff8e"), crossOff = Col("#123a22"), crossGlow = Col("#2be27a"),
            pink = Col("#ff5a82"), pinkOff = Col("#3a1622"), pinkGlow = Col("#ff2f63"), red = Col("#ff3b44"), splash = Col("#a4bfd6"), splash2 = Col("#6f8ba6"),
            flash = Col("#7088ac"), boltHi = Col("#dce8ff"), boltLo = Col("#8aa2c8"), fog = Col("#22324a"), mistC = Col("#1b2a3b"),
            drop = Col("#9fbad0"), dropHi = Col("#eaf4ff"), trailC = Col("#8fb0c8"), black = Col("#000000");
        readonly int[] rain = Cols("#22344a", "#3f5872", "#7e9bb7", "#b6cfe4");
        readonly WinPal win = new WinPal { Lit = Cols("#86b9d8", "#a9d6ec", "#6a9cc0", "#86b9d8", "#f2b45e", "#a9d6ec", "#e5964c", "#7fb0d2"), Hi = Cols("#c6e6f6", "#d6f0fc"), Dim = Col("#28405a"), Curtain = Col("#4a6f90"), Sil = Col("#13202c"), Plant = Col("#14261c"), Tv = Cols("#5a8cf0", "#86b0ff", "#3d66c8"), Off = Col("#101925") };
        readonly WinPal midWin = new WinPal { Lit = Cols("#4f7a98", "#6f9ebd", "#5a88a8", "#c08a4a"), Hi = Cols("#8cb8d4"), Dim = Col("#22384c"), Curtain = Col("#3a5a74"), Sil = Col("#101a24"), Plant = Col("#101a14"), Tv = Cols("#4a72c0") };
        readonly WinPal farWin = new WinPal { Lit = Cols("#2f4458", "#3e5a74", "#56728a"), Hi = Cols("#56728a") };
        readonly int[] sky = new int[W * H];

        sealed class Lamp { public int X, Hx, Hy, Sx; public bool Flick, On; }
        sealed class Span { public int X0, X1, Y; }
        sealed class Person { public bool On; public int T, Dir = 1, Step, Lane; public double X; public Spr[] Sp; }
        sealed class Car { public bool On; public int T = 60, Dir = 1, Y; public double X; }
        sealed class Bit { public double X, Y, Vx, Vy; public int L, Hit; }
        sealed class Drop { public double X, Y, V; public int Wait; public readonly List<double[]> Trail = new List<double[]>(); }
        sealed class Glint { public int X, Y, R; public double A; }

        readonly Spr cloudA, cloudB, Lf = new Spr(480, H), Lm = new Spr(560, H), Ln = new Spr(NW, H), Lfront = new Spr(NW, H), Lg = new Spr(NW, GH);
        readonly byte[] puddle = new byte[NW * GH];
        readonly List<Win> farWins = new List<Win>(), midWins = new List<Win>(), nearWins = new List<Win>();
        readonly List<Light> farLights = new List<Light>();
        readonly List<Span> sills = new List<Span>(), awnings = new List<Span>(), shops = new List<Span>();
        readonly List<Lamp> lamps = new List<Lamp>();
        readonly Sign cafe, sushi;
        readonly int crossX, crossY;
        readonly Spr[][] umbrellas;
        readonly Person[] people = new Person[2];
        readonly Spr carS;
        readonly Car car = new Car();
        readonly List<Bit> spray = new List<Bit>(), drips = new List<Bit>();
        readonly List<Drop> glass = new List<Drop>();
        int boltT, boltNext;
        List<int[]> boltPath = new List<int[]>();

        public Rain()
        {
            R = new Rng(4242);
            BakeBands(sky, new[] { 0, 16, 30, 44, 56, 68, 80 }, Cols("#06090f", "#080d15", "#0c121c", "#101823", "#141e2b", "#192535", "#1e2c3e"), 0.5);
            cloudA = NoiseTex(640, 62, 64, 3, Cols("#0d141e", "#111a26", "#16202e", "#1b2737", "#212f41"), 0.47, t => 1.05 - t * 0.35);
            cloudB = NoiseTex(640, 30, 32, 2, Cols("#131c29", "#18232f", "#1e2a39", "#26354a"), 0.52, t => 0.95 + t * 0.1);

            // far skyline in the haze
            List<Bld> fbl = GenBuildings(480, 8, 22, 44, 86, 0.15, 1, 3);
            for (int i = 0; i < fbl.Count; i++)
            {
                Bld b = fbl[i];
                if (i % 9 == 4) b.Top = Ri(28, 36);
                LRect(Lf, b.X, b.Top, b.Wd, H - b.Top, far);
                if (b.Top < 40)
                {
                    LRect(Lf, b.X + 2, b.Top - 4, b.Wd - 4, 4, far);
                    int ax = b.X + (b.Wd >> 1);
                    LLine(Lf, ax, b.Top - 4, ax, b.Top - 16, far);
                    farLights.Add(new Light { X = ax, Y = b.Top - 17, Ph = Ri(0, 40) });
                }
                else if (R.Next() < 0.3) LRect(Lf, b.X + 2, b.Top - 3, b.Wd - 4, 3, far);
                for (int y = b.Top + 3; y < GY; y += 4) for (int x = b.X + 2; x < b.X + b.Wd - 2; x += 3) if (R.Next() < 0.12) farWins.Add(new Win { X = x, Y = y, Wd = 1, Hd = 2, On = R.Next() < 0.6, P = 0.6, C = Ri(0, 2) });
            }
            Rim(Lf, farRim);

            // mid skyline
            Spr tank = new Spr(new[] { "...k...", "..kkk..", ".kkkkk.", ".kkkkk.", ".kkkkk.", ".kkkkk.", ".k...k.", ".kk.kk.", ".k.k.k." }, Pal("k", mid));
            foreach (Bld b in GenBuildings(560, 14, 30, 60, 106, 0.25, 1, 4))
            {
                LRect(Lm, b.X, b.Top, b.Wd, H - b.Top, mid);
                double r = R.Next();
                if (r < 0.3) LSpr(Lm, tank, b.X + Ri(1, b.Wd - 8), b.Top - 9);
                else if (r < 0.5) Antenna(Lm, b.X + Ri(3, b.Wd - 4), b.Top, Ri(7, 13), mid);
                else if (r < 0.65) LRect(Lm, b.X + 3, b.Top - 4, b.Wd - 6, 4, mid);
                AddWindows(midWins, b, 2, 4, 2, 3, 2, 3, 0.26, GY);
            }
            Rim(Lm, midRim);

            // near street: facades with ledges, fire escapes and shops
            Spr tower = WaterTower(near, detail);
            int[] goods1 = Cols("#c0603a", "#8a3a2a", "#e0c070", "#4a6a3a"), goods2 = Cols("#c0603a", "#e0c070", "#6a4a8a");
            List<Bld> nb = GenBuildings(NW, 34, 62, 66, 112, 0.35, 3, 8);
            for (int i = 0; i < nb.Count; i++)
            {
                Bld b = nb[i];
                LRect(Ln, b.X, b.Top, b.Wd, GY - b.Top, near);
                LRect(Ln, b.X - 1, b.Top + 2, b.Wd + 2, 1, near);
                LPx(Ln, b.X, b.Top - 1, near); LPx(Ln, b.X + b.Wd - 1, b.Top - 1, near);
                double r = R.Next();
                if (r < 0.3) LSpr(Ln, tower, b.X + Ri(2, b.Wd - 13), b.Top - tower.H);
                else if (r < 0.55) Antenna(Ln, b.X + Ri(4, b.Wd - 5), b.Top, Ri(8, 14), near);
                else if (r < 0.75) { LRect(Ln, b.X + 3, b.Top - 3, 5, 3, near); LRect(Ln, b.X + 10, b.Top - 2, 4, 2, near); }
                sills.Add(new Span { X0 = b.X, X1 = b.X + b.Wd - 1, Y = b.Top - 1 });
                // floors: a row of windows and a wet sill
                bool shop = i % 3 == 1;
                int floorsEnd = shop ? GY - 19 : GY - 4, ww = R.Next() < 0.6 ? 3 : 4, gx = 3;
                int cols = Fl((double)(b.Wd - 6 + gx) / (ww + gx)), x0 = b.X + Fl((b.Wd - (cols * (ww + gx) - gx)) / 2.0);
                double p = Math.Min(0.85, 0.32 * (0.3 + R.Next() * 1.4));
                for (int y = b.Top + 6; y + 8 <= floorsEnd; y += 10)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        double rr = R.Next();
                        nearWins.Add(new Win { X = x0 + c * (ww + gx), Y = y, Wd = ww, Hd = 5, On = R.Next() < p, P = p, C = Ri(0, 7), Glass = R.Next() < 0.7, K = rr < 0.5 ? 0 : rr < 0.62 ? 1 : rr < 0.72 ? 2 : rr < 0.84 ? 3 : rr < 0.93 ? 4 : 5 });
                    }
                    LRect(Lfront, b.X - 1, y + 6, b.Wd + 2, 1, ledge);
                    sills.Add(new Span { X0 = b.X, X1 = b.X + b.Wd - 1, Y = y + 5 });
                }
                if (i % 4 == 2 && cols >= 3)
                {
                    int ex0 = x0 - 1, ex1 = x0 + 2 * (ww + gx) - gx;
                    for (int y = b.Top + 14; y + 8 <= floorsEnd; y += 10)
                    {
                        LRect(Lfront, ex0, y + 2, ex1 - ex0 + 1, 1, esc); LRect(Lfront, ex0, y - 1, ex1 - ex0 + 1, 1, esc);
                        for (int x = ex0; x <= ex1; x += 3) LRect(Lfront, x, y - 1, 1, 3, esc);
                        bool up = (y / 10) % 2 != 0;
                        LLine(Lfront, up ? ex0 + 1 : ex1 - 1, y + 2, up ? ex1 - 2 : ex0 + 2, y - 7, esc);
                    }
                }
                if (shop)
                {
                    // shop front: warm window, awning and door
                    int sx0 = b.X + 3, sw = b.Wd - 12, sy = GY - 12;
                    LRect(Ln, sx0, sy, sw, 11, shopC); LRect(Ln, sx0, sy, sw, 2, shopHi);
                    for (int x = sx0; x < sx0 + sw; x++)
                    {
                        LPx(Ln, x, sy + 5, shopDark); LPx(Ln, x, sy + 8, shopDark);
                        if (R.Next() < 0.45) LPx(Ln, x, sy + 4, goods1[Ri(0, 3)]);
                        if (R.Next() < 0.45) LPx(Ln, x, sy + 7, goods2[Ri(0, 2)]);
                    }
                    for (int x = sx0 + 7; x < sx0 + sw; x += 8) LRect(Ln, x, sy, 1, 11, near);
                    if (R.Next() < 0.7) { int px = sx0 + Ri(2, sw - 5); LRect(Ln, px, sy + 5, 3, 6, Col("#2a1a12")); LRect(Ln, px + 1, sy + 3, 1, 2, Col("#2a1a12")); }
                    LRect(Ln, b.X + b.Wd - 7, GY - 12, 4, 12, Col("#1e2a36")); LRect(Ln, b.X + b.Wd - 6, GY - 11, 2, 5, Col("#c08a50"));
                    for (int x = sx0 - 2; x < sx0 + sw + 2; x++)
                    {
                        int st = (((x - sx0) >> 1) & 1) != 0 ? awnB : awnA;
                        LPx(Lfront, x, GY - 16, st); LPx(Lfront, x, GY - 15, st);
                        if ((x & 1) == 0) LPx(Lfront, x, GY - 14, st);
                    }
                    awnings.Add(new Span { X0 = sx0 - 2, X1 = sx0 + sw + 1, Y = GY - 13 });
                    shops.Add(new Span { X0 = sx0, X1 = sx0 + sw - 1 });
                }
                else
                {
                    // doorway with a weak light
                    int dx = b.X + Ri(4, b.Wd - 9);
                    LRect(Ln, dx, GY - 10, 5, 10, Col("#141e2a")); LRect(Ln, dx + 1, GY - 9, 3, 4, Col("#3a5468")); LRect(Lfront, dx - 1, GY - 11, 7, 1, ledge);
                }
            }
            // neon CAFE over the first shop, a green cross on another building and a vertical SUSHI sign
            Bld shopB = nb[1], crossB = nb[Math.Min(nb.Count - 1, 4)];
            cafe = new Sign { X = shopB.X + 4, Y = GY - 23, On = new[] { 1, 1, 1, 1 } };
            LRect(Ln, cafe.X - 1, cafe.Y - 1, 17, 7, Col("#0e1820"));
            crossX = crossB.X + crossB.Wd - 9; crossY = crossB.Top + 16;
            ClearWins(nearWins, crossX - 2, crossY - 2, crossX + 8, crossY + 8);
            LRect(Ln, crossX - 1, crossY - 1, 7, 7, Col("#0e1820"));
            Bld sushiB = nb.Where((b, i) => i >= 6 && i % 3 != 1 && b.Top < 90).FirstOrDefault() ?? nb[nb.Count - 2];
            sushi = new Sign { X = sushiB.X + 2, Y = sushiB.Top + 12, On = new[] { 1, 1, 1, 1, 1 } };
            ClearWins(nearWins, sushi.X - 2, sushi.Y - 3, sushi.X + 7, sushi.Y + 33);
            LRect(Ln, sushi.X - 1, sushi.Y - 2, 7, 33, Col("#0e1418")); LRect(Ln, sushi.X - 3, sushi.Y + 1, 2, 1, detail); LRect(Ln, sushi.X - 3, sushi.Y + 26, 2, 1, detail);
            foreach (Win w in nearWins) if (R.Next() < 0.07) LRect(Lfront, w.X, w.Y + w.Hd + 2, w.Wd, 2, detail);
            Rim(Ln, nearRim); Rim(Lfront, ledgeHi);
            // street lamps: posts baked, light drawn live
            for (int x = 30; x < NW - 20; x += Ri(110, 150))
            {
                lamps.Add(new Lamp { X = x, Hx = x + 4, Hy = 117, Flick = lamps.Count == 2 });
                LRect(Lfront, x, 117, 1, GY + 5 - 117, lampHead); LRect(Lfront, x - 1, GY + 2, 3, 3, lampHead); LRect(Lfront, x - 1, 132, 3, 1, lampHead);
                LPx(Lfront, x + 1, 115, lampHead); LPx(Lfront, x, 116, lampHead); LRect(Lfront, x + 2, 115, 5, 1, lampHead); LRect(Lfront, x + 3, 116, 4, 1, lampHead);
            }

            // ground: sidewalk, curb, road and puddles
            for (int y = 0; y < GH; y++)
                for (int x = 0; x < NW; x++)
                {
                    int yy = y + GY, c;
                    double n = Hash2(x, yy);
                    if (yy < CURB) c = (x % 12 == 0 || yy == GY + 4) ? sideLine : n < 0.1 ? sideLine : side;
                    else if (yy == CURB) c = x % 16 == 0 ? sideLine : curb;
                    else if (yy == ROAD) c = gutter;
                    else if (yy == 167 && x % 24 < 12) c = lane;
                    else c = n < 0.05 ? roadDk : n < 0.16 ? roadHi : road;
                    Lg.D[y * NW + x] = c;
                }
            for (int k = 0; k < 16; k++)
            {
                bool onRoad = k % 3 != 0;
                int cx = Ri(0, NW - 1), cy = onRoad ? Ri(ROAD + 3, H - 3) : Ri(GY + 2, CURB - 2);
                int rx = onRoad ? Ri(8, 22) : Ri(4, 10), ry = onRoad ? Ri(1, 3) : 1;
                for (int y = -ry; y <= ry; y++)
                    for (int x = -rx; x <= rx; x++)
                    {
                        double wob = Math.Sin((cx + x) * 0.4) * 0.25;
                        if ((double)(x * x) / (rx * rx) + (y * y) / ((ry + 0.5) * (ry + 0.5)) <= 1 + wob)
                        {
                            int gy = cy + y - GY;
                            if (gy >= 0 && gy < GH) puddle[gy * NW + ((cx + x + NW) % NW)] = 1;
                        }
                    }
            }
            for (int x = 0; x < NW; x++) puddle[(ROAD - GY) * NW + x] = 2;   // gutter water

            // actors
            string[][] umb = { new[] { "#8a2632", "#5e1823", "#b8434f" }, new[] { "#1f3c56", "#152a3d", "#3c6a8e" }, new[] { "#c99a3a", "#8a6a26", "#f0c860" }, new[] { "#2a2f38", "#1c2028", "#454c58" } };
            umbrellas = umb.Select(PersonSprites).ToArray();
            for (int k = 0; k < 2; k++) people[k] = new Person { T = 20 + k * 160, Sp = umbrellas[k] };
            carS = new Spr(new[] {
                "..........aaaaaaaaaa..........", ".........aGGGGGgGGGGGa........", "........aGgGGGGgGGGGGGa.......", "...aaaaaabbbbbbbbbbbbbbaaaaa..",
                "..abbbbbbbbbbbbbbbbbbbbbbbbbbL", "Tbbbbbbbbbbbdbbbbbbbbbbbbbbbbb", "Tbbbbbbbbbbbdbbbbbbbbbbbbbbbbb", ".dbbbdkkkdbbdbbbbbbbbdkkkdbbbd",
                ".....kkwwkk..........kkwwkk...", "......kwwk............kwwk....", ".......kk..............kk....." },
                Pal("abdGgLTkw", Col("#6a8aa0"), Col("#2f4558"), Col("#1f2f3e"), Col("#101c28"), Col("#4f7090"), Col("#fff2c6"), Col("#ff3a46"), Col("#05080b"), Col("#55606c")));
            for (int k = 0; k < 7; k++) glass.Add(new Drop { X = Ri(8, W - 8), Y = Ri(0, H - 10), Wait = Ri(20, 200) });
            boltNext = Ri(150, 300);
        }

        Spr[] PersonSprites(string[] u)
        {
            Dictionary<char, int> pal = Pal("Uuh|scdl", Col(u[0]), Col(u[1]), Col(u[2]), Col("#6a7480"), Col("#3b3a40"), Col("#2c3848"), Col("#1e2834"), Col("#151c26"));
            string[] top = { "....hhh....", "..hhUUUUu..", ".hUUUUUUUu.", "hUUUUUUUUUu", "u.u.u|u.u.u", ".....|.....", "...ss|.....", "...ss|.....", "...ccc|....", "..cccc.....", "..cccc.....", "..cdcc.....", "...ccc....." };
            string[][] legs = { new[] { "...l.l.....", "..l...l...." }, new[] { "...ll......", "...ll......" }, new[] { "....l.l....", "...l...l..." }, new[] { "...ll......", "...ll......" } };
            return legs.Select(l => new Spr(top.Concat(l).ToArray(), pal)).ToArray();
        }

        // Overcast clouds: value-noise textures, scrolled.
        Spr NoiseTex(int w, int h, int cell, int oct, int[] cs, double thr, Func<double, double> fall)
        {
            Spr d = new Spr(w, h);
            List<float[]> gs = new List<float[]>();
            List<int> cws = new List<int>(), ss = new List<int>();
            for (int o = 0; o < oct; o++)
            {
                int s = cell >> o, cw = w / s, ch = (int)Math.Ceiling((double)h / s) + 2;
                float[] g = new float[cw * ch];
                for (int k = 0; k < g.Length; k++) g[k] = (float)R.Next();
                gs.Add(g); cws.Add(cw); ss.Add(s);
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double v = 0, a = 0.55, sum = 0;
                    for (int o = 0; o < gs.Count; o++)
                    {
                        float[] g = gs[o];
                        int cw = cws[o], s = ss[o];
                        double fx = (double)x / s, fy = (double)y / s;
                        int ix = Fl(fx), iy = Fl(fy);
                        double tx = fx - ix, ty = fy - iy;
                        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                        double g00 = g[iy * cw + ix % cw], g10 = g[iy * cw + (ix + 1) % cw], g01 = g[(iy + 1) * cw + ix % cw], g11 = g[(iy + 1) * cw + (ix + 1) % cw];
                        v += a * ((g00 * (1 - tx) + g10 * tx) * (1 - ty) + (g01 * (1 - tx) + g11 * tx) * ty);
                        sum += a; a *= 0.5;
                    }
                    v = v / sum * fall((double)y / h) + (BayerAt(x, y) / 16 - 0.5) * 0.05;
                    if (v < thr) continue;
                    d.D[y * w + x] = cs[Math.Min(cs.Length - 1, Fl((v - thr) / 0.06))];
                }
            return d;
        }
        // Light reflected on wet asphalt: a wobbly vertical streak broken by ripples.
        void WetStreak(int x, int y0, int c, double a, int f)
        {
            for (int y = y0; y < H; y++)
            {
                double t = (double)(y - y0) / (H - y0), k = a * (1 - t) * (0.7 + 0.3 * Math.Sin(y * 2.3 + f * 0.9));
                int w = Rd(Math.Sin(y * 0.55 + f * 0.3 + x * 0.7) * 0.7);
                Blend(x + w, y, c, k); Blend(x + w + 1, y, c, k * 0.55); Blend(x + w - 1, y, c, k * 0.3);
                if ((y + (f >> 1)) % 3 == 0) for (int d = 2; d <= 3; d++) { Blend(x + w - d, y, c, k * 0.4); Blend(x + w + 1 + d, y, c, k * 0.4); }
            }
        }

        protected override void Update(int f)
        {
            int offN = f / 10;
            Toggle(nearWins, 0.0012); Toggle(midWins, 0.0015); Toggle(farWins, 0.002);
            Flicker(cafe, 5, 0.002, 4, 9);
            Flicker(sushi, 5, 0.002, 4, 9);
            foreach (Person p in people)
            {
                if (!p.On)
                {
                    if (--p.T <= 0) { p.On = true; p.Dir = R.Next() < 0.5 ? 1 : -1; p.X = p.Dir > 0 ? offN - 14 : offN + W + 2; p.Sp = umbrellas[Ri(0, 3)]; p.Lane = Ri(0, 2); }
                    continue;
                }
                p.X += p.Dir * 0.34; p.Step++;
                double s = p.X - offN;
                if (s < -20 || s > W + 20) { p.On = false; p.T = Ri(120, 420); }
            }
            if (!car.On) { if (--car.T <= 0) { car.On = true; car.Dir = R.Next() < 0.5 ? 1 : -1; car.X = car.Dir > 0 ? -40 : W + 10; car.Y = car.Dir > 0 ? 166 : 176; } }
            else
            {
                car.X += car.Dir * 2.6;
                foreach (int wx in new[] { 6, 22 }) { double bx = car.X + (car.Dir > 0 ? wx : 29 - wx); spray.Add(new Bit { X = bx - car.Dir * 2, Y = car.Y - 1, Vx = -car.Dir * (0.4 + R.Next() * 0.8), Vy = -0.4 - R.Next() * 0.8 }); }
                if (car.X < -50 || car.X > W + 50) { car.On = false; car.T = Ri(220, 520); }
            }
            for (int k = spray.Count - 1; k >= 0; k--) { Bit s = spray[k]; s.X += s.Vx; s.Y += s.Vy; s.Vy += 0.18; if (++s.L > 8) spray.RemoveAt(k); }
            // awning drips
            if (f % 3 == 0) foreach (Span a in awnings) { int x = Ri(a.X0, a.X1); if (R.Next() < 0.5) drips.Add(new Bit { X = x, Y = a.Y, Vy = 0.5 }); }
            for (int k = drips.Count - 1; k >= 0; k--)
            {
                Bit d = drips[k];
                d.Y += d.Vy; d.Vy = Math.Min(3, d.Vy + 0.35);
                if (d.Y >= GY + 3) { d.Hit++; d.Y = GY + 3; if (d.Hit > 3) drips.RemoveAt(k); }
            }
            // drops on the glass: stick, then slide down in jerks
            foreach (Drop g in glass)
            {
                if (g.Wait > 0) { g.Wait--; if (g.Wait == 0) g.V = 0.6 + R.Next() * 1.2; }
                else
                {
                    g.Trail.Add(new[] { g.X, g.Y, 0 });
                    g.Y += g.V; g.X += (R.Next() - 0.5) * 0.3;
                    if (R.Next() < 0.06) g.Wait = Ri(6, 40);
                    if (g.Y > H + 4) { g.Y = -4; g.X = Ri(8, W - 8); g.Wait = Ri(20, 160); }
                }
                foreach (double[] t in g.Trail) t[2]++;
                g.Trail.RemoveAll(t => t[2] >= 70);
            }
            // lightning: rare, soft and far away
            if (boltT > 0) boltT--;
            else if (--boltNext <= 0)
            {
                boltT = 14; boltNext = Ri(520, 1000);
                int x = Ri(40, W - 40), y = 18;
                boltPath = new List<int[]>();
                while (y < 92)
                {
                    int nx = x + Ri(-3, 3), ny = y + Ri(3, 6);
                    boltPath.Add(new[] { x, y, nx, ny });
                    if (R.Next() < 0.12) boltPath.Add(new[] { nx, ny, nx + Ri(-9, 9), ny + Ri(4, 10) });
                    x = nx; y = ny;
                }
            }
        }

        // Rain columns: computed from the frame number, no state needed.
        void RainLayer(int n, int len, int sp, int ci, int f, List<Glint> lights)
        {
            const int span = H + 20;
            for (int k = 0; k < n; k++)
            {
                double h1 = Hash2(k, n * 13 + len), h2 = Hash2(k * 7 + 3, len);
                double y0 = (h1 * span + f * sp) % span - 10, x0 = h2 * (W + 60) - 30;
                for (int j = 0; j < len; j++)
                {
                    int y = Rd(y0 + j), x = Rd(x0 + y * 0.3) % (W + 60) - 30;
                    if (y < 0 || y >= H || x < 0 || x >= W) continue;
                    int c = rain[ci + (j == len - 1 && ci == 2 ? 1 : 0)];
                    if (lights != null) foreach (Glint l in lights) { int dx = x - l.X, dy = y - l.Y; if (dx * dx + dy * dy < l.R * l.R) { c = Lerp(c, lit, l.A); break; } }
                    Fb[y * W + x] = c;
                }
            }
        }
        void Splash(int x, int y, int fr, int c)
        {
            if (fr == 0) Px(x, y, c);
            else if (fr == 1) { Px(x - 1, y - 1, c); Px(x + 1, y - 1, c); Px(x, y, c); }
            else if (fr == 2) { Px(x - 2, y - 2, c); Px(x + 2, y - 2, c); }
            else if (fr == 3) { Blend(x - 2, y - 1, c, 0.5); Blend(x + 2, y - 1, c, 0.5); }
        }

        static readonly double[] Flash = { 0, 0.42, 0.18, 0.05, 0.34, 0.22, 0.12, 0.07, 0.04, 0.02, 0.01, 0, 0, 0, 0 };
        static readonly int[][] CrossPts = { new[] { 2, 0 }, new[] { 2, 1 }, new[] { 0, 2 }, new[] { 1, 2 }, new[] { 2, 2 }, new[] { 3, 2 }, new[] { 4, 2 }, new[] { 2, 3 }, new[] { 2, 4 } };
        // Haze and mist cost the most: their shading is recomputed every other frame and kept in between.
        readonly double[] fogA = new double[(GY - 60) * W], mistA = new double[(GY - 96) * W];
        bool primed;
        protected override void Draw(int f)
        {
            int offF = f / 40, offM = f / 20, offN = f / 10;
            Array.Copy(sky, Fb, Fb.Length);
            Blit(cloudA, f / 8, 0);
            Blit(cloudB, f / 4, 40);
            // lightning: a soft flash behind the clouds and a thin far bolt
            double li = boltT > 0 ? Flash[14 - boltT] : 0;
            if (li > 0)
                for (int y = 0; y < 100; y++) for (int x = 0; x < W; x++) { int i = y * W + x; Blend(x, y, flash, li * (1 - y / 130.0) * (Fb[i] != sky[i] ? 0.9 : 0.55)); }
            if (li > 0.15) foreach (int[] p in boltPath) Line(p[0], p[1], p[2], p[3], li > 0.3 ? boltHi : boltLo);
            // far city in the haze
            Blit(Lf, offF);
            DrawWindows(farWins, offF, 480, farWin, f);
            foreach (Light l in farLights) { int x = Sx(l.X, offF, 480); if ((f + l.Ph) % 40 < 14) { Px(x, l.Y, red); Glow(x, l.Y, 3, red, 0.3); } }
            bool fresh = (f & 1) == 0 || !primed;
            primed = true;
            for (int y = 60; y < GY; y++)
            {
                double a = Math.Min(0.55, (y - 60) / 120.0);
                for (int x = 0; x < W; x++)
                {
                    int i = (y - 60) * W + x;
                    if (fresh) fogA[i] = a * (0.75 + 0.25 * Math.Sin(x * 0.05 + y * 0.3 + f * 0.02));
                    Blend(x, y, fog, fogA[i]);
                }
            }
            RainLayer(170, 2, 4, 0, f, null);
            // mid city and rolling mist
            Blit(Lm, offM);
            DrawWindows(midWins, offM, 560, midWin, f);
            for (int y = 96; y < GY; y++)
            {
                double sy = Math.Sin(y * 0.2) * 2, ry = (y - 96) / 70.0;
                for (int x = 0; x < W; x++)
                {
                    int i = (y - 96) * W + x;
                    if (fresh)
                    {
                        double n = Math.Sin((x + f * 0.4) * 0.045 + sy) * 0.5 + Math.Sin((x - f * 0.25) * 0.09 + y * 0.13) * 0.5;
                        mistA[i] = Math.Max(0, ry * (0.45 + 0.35 * n));
                    }
                    Blend(x, y, mistC, mistA[i]);
                }
            }
            RainLayer(120, 4, 7, 1, f, null);
            // near street
            Blit(Ln, offN);
            DrawWindows(nearWins, offN, NW, win, f);
            Blit(Lfront, offN);
            int cx = Sx(cafe.X, offN, NW);
            if (cx > -20 && cx < W + 4) Neon("CAFE", cx, cafe.Y, false, Lit(cafe, f), neon, neonOff, neonGlow);
            int ux = Sx(sushi.X, offN, NW);
            if (ux > -10 && ux < W + 4) Neon("SUSHI", ux + 1, sushi.Y, true, Lit(sushi, f), pink, pinkOff, pinkGlow);
            int kx = Sx(crossX, offN, NW);
            bool kon = f % 60 < 44 || (f % 60 & 4) != 0;
            if (kx > -10 && kx < W)
            {
                if (kon) foreach (int[] p in CrossPts) Glow(kx + p[0], crossY + p[1], 3, crossGlow, 0.06);
                foreach (int[] p in CrossPts) Px(kx + p[0], crossY + p[1], kon ? cross : crossOff);
            }
            // lamps: heads lit, one of them flickers
            List<Glint> lights = new List<Glint>();
            foreach (Lamp l in lamps)
            {
                int x = Sx(l.Hx, offN, NW);
                l.Sx = x; l.On = false;
                if (x < -30 || x > W + 30) continue;
                l.On = !l.Flick || !(Hash2(f / 2, 9) < 0.18 || f % 160 > 150);
                if (l.On) { Rect(x - 1, l.Hy, 3, 1, lamp); lights.Add(new Glint { X = x, Y = l.Hy + 2, R = 22, A = 0.55 }); }
            }
            // ground, and the reflection of everything above it
            Blit(Lg, offN, GY);
            for (int y = GY; y < H; y++)
            {
                int sy = 2 * GY - 1 - y, prow = (y - GY) * NW;
                double a0 = y < CURB ? 0.06 : 0.1;
                for (int x = 0; x < W; x++)
                {
                    int px = (x + offN) % NW, m = puddle[prow + px], i = y * W + x;
                    double a = m == 1 ? 0.55 : m == 2 ? 0.3 : a0;
                    int dx = m != 0 ? Rd(Math.Sin(y * 1.3 + f * 0.45 + x * 0.08) * 0.8) : 0;
                    Fb[i] = Lerp(Fb[i], Fb[sy * W + Math.Min(W - 1, Math.Max(0, x + dx))], a);
                    if (m == 1 && (y == GY || puddle[prow - NW + px] != 1)) Fb[i] = Lerp(Fb[i], splash2, 0.35);
                }
            }
            // light pools, shop light spilling out and long wet streaks
            foreach (Lamp l in lamps) if (l.On) { Glow(l.Sx, GY + 3, 18, lampGlow, 0.22, 0.3); WetStreak(l.Sx, CURB + 1, lampGlow2, 0.42, f); }
            foreach (Span s in shops)
            {
                int x0 = Sx(s.X0, offN, NW), w = s.X1 - s.X0;
                if (x0 + w + 3 < 0 || x0 - 3 >= W) continue;
                for (int y = GY; y < ROAD; y++) for (int x = x0 - 3; x <= x0 + w + 3; x++) { double e = Math.Min(Math.Min(x - x0 + 4, x0 + w + 4 - x), 4) / 4.0; Blend(x, y, shopC, 0.26 * e * (1 - (y - GY) / 10.0)); }
            }
            if (cx > -20 && cx < W) { WetStreak(cx + 3, ROAD + 1, neon, 0.16, f); WetStreak(cx + 11, ROAD + 1, neon, 0.14, f); }
            if (ux > -10 && ux < W) WetStreak(ux + 2, CURB + 1, pink, 0.2, f);
            // splashes: on the street, rings on puddles, and on ledges
            for (int k = 0; k < 46; k++)
            {
                int per = 5 + k % 3, tt = f + k * 7, cyc = tt / per, fr = tt % per;
                int x = Fl(Hash2(k, cyc) * W), y = GY + 1 + Fl(Hash2(k + 99, cyc) * (H - GY - 2));
                if (puddle[(y - GY) * NW + (x + offN) % NW] == 1)
                {
                    int c = splash2;
                    if (fr == 0) Px(x, y, splash);
                    else if (fr == 1) { Px(x - 1, y, c); Px(x + 1, y, c); }
                    else if (fr == 2) { Blend(x - 2, y, c, 0.7); Blend(x + 2, y, c, 0.7); Px(x, y - 1, c); Blend(x, y + 1, c, 0.5); }
                    else if (fr == 3) { Blend(x - 3, y, c, 0.4); Blend(x + 3, y, c, 0.4); }
                }
                else Splash(x, y, fr, splash);
            }
            for (int k = 0; k < 18; k++)
            {
                int tt = f + k * 5, cyc = tt / 6, fr = tt % 6;
                Span s = sills[Fl(Hash2(k, cyc) * sills.Count)];
                int x = Sx(s.X0 + Fl(Hash2(k + 7, cyc) * (s.X1 - s.X0)), offN, NW);
                if (x >= 0 && x < W) Splash(x, s.Y, fr, splash2);
            }
            foreach (Bit d in drips) { int x = Sx((int)d.X, offN, NW); if (d.Hit > 0) Splash(x, GY + 3, d.Hit, splash); else Px(x, Rd(d.Y), rain[2]); }
            // people under umbrellas, with their own reflection
            foreach (Person p in people)
            {
                if (!p.On) continue;
                Spr s = p.Sp[(p.Step >> 3) % 4];
                int x = Rd(p.X - offN), bs = GY + 4 + p.Lane * 2;
                bool fl = p.Dir < 0;
                PutReflect(s, x, bs + 1, fl, 0.3, f);
                Put(s, x, bs - s.H + 1, fl);
                int hit = (f + Rd(p.X)) % 7;
                if (hit < 2) Px(x + 2 + (f * 3) % 7, bs - s.H + hit, splash);
            }
            // car with headlights, tail lights and road spray
            if (car.On)
            {
                int x = Rd(car.X), top = car.Y - carS.H + 1;
                bool fl = car.Dir < 0;
                PutReflect(carS, x, car.Y + 1, fl, 0.28, f);
                int hx = fl ? x : x + carS.W - 1, tx = fl ? x + carS.W - 1 : x, hy = top + 4;
                for (int d = 1; d < 46; d++) { double half = 1 + d * 0.22; for (int j = -Fl(half * 0.5); j <= Math.Ceiling(half); j++) Blend(hx + car.Dir * d, hy + j, lamp, 0.2 * (1 - d / 46.0)); }
                WetStreak(hx - car.Dir, car.Y + 1, lamp, 0.5, f); WetStreak(tx, car.Y + 1, red, 0.45, f);
                Put(carS, x, top, fl);
                Glow(hx + car.Dir, hy, 5, lamp, 0.5); Glow(tx, top + 5, 3, red, 0.45);
                lights.Add(new Glint { X = hx + car.Dir * 16, Y = hy + 1, R = 18, A = 0.5 });
                foreach (Bit s in spray) Blend(Rd(s.X), Rd(s.Y), splash, 0.7 - s.L * 0.07);
            }
            // lamp halos over everything, then the closest rain (lit inside the halos)
            foreach (Lamp l in lamps)
            {
                if (!l.On) continue;
                Glow(l.Sx, l.Hy + 1, 14, lampGlow, 0.32); Glow(l.Sx, l.Hy + 1, 5, lampGlow2, 0.45);
                for (int y = l.Hy + 2; y < GY + 2; y++)
                {
                    double hw = (y - l.Hy) * 0.35, a = 0.07 * (1 - (double)(y - l.Hy) / (GY + 2 - l.Hy));
                    for (double x = -hw; x <= hw; x++) Blend(l.Sx + Rd(x), y, lampGlow, a);
                }
            }
            RainLayer(60, 8, 12, 2, f, lights);
            // drops sliding on the window glass (foreground)
            foreach (Drop g in glass)
            {
                foreach (double[] t in g.Trail) if ((int)t[2] % 3 == 0) Blend(Rd(t[0]), Rd(t[1]), trailC, 0.12 * (1 - t[2] / 70));
                int x = Rd(g.X), y = Rd(g.Y);
                Blend(x, y, drop, 0.35); Blend(x + 1, y, drop, 0.3); Blend(x, y + 1, drop, 0.3); Blend(x + 1, y + 1, drop, 0.3);
                Blend(x, y, dropHi, 0.55); Blend(x, y + 2, black, 0.25); Blend(x + 1, y + 2, black, 0.25);
            }
        }
    }

    // ===== ARCADE: 8-bit night with a striped sun, a grid floor, a blocky city and critters. =====
    sealed class Arcade : Scene
    {
        const int HZ = 112, SX = 160, SY = 80, SR = 38;
        readonly int aK = Col("#07030f"), aD = Col("#150a2e"), aP = Col("#2b1159"), aV = Col("#4b1b7d"), aM = Col("#8f1f8c"), aR = Col("#e3317c"), aO = Col("#ff7b3b"), aY = Col("#ffd94b"),
            aW = Col("#fff7e8"), aC = Col("#3ae9f2"), aB = Col("#2b6ce2"), aN = Col("#14296b"), aT = Col("#0f5c6c"), aG = Col("#5cf07a"), ag = Col("#1d7a4c"), aL = Col("#b8a6ff");
        readonly Dictionary<char, int> A;
        readonly int[] sky = new int[W * H];
        readonly int hud, hud2;

        sealed class Star { public int X, Y, C, Per, Ph; public bool Plus; }
        sealed class Shot { public int X, Y, L, Life; }
        sealed class Cw { public int X, Y, C; public bool On; }
        sealed class Eq { public int X, Wd, Y0, Y1; public double Seed; }
        sealed class Actor { public bool On; public int T, Dir = 1, Hop, Beam, Y; public double X, Yd, Y0; public readonly List<double[]> Trail = new List<double[]>(); }

        readonly List<Star> stars = new List<Star>();
        readonly List<Shot> shoot = new List<Shot>();
        readonly float[] rf, rn;
        readonly double[] valleyF = new double[W], valleyN = new double[W];
        readonly Spr Lc = new Spr(640, H);
        readonly List<Cw> cwins = new List<Cw>();
        readonly List<Eq> eqs = new List<Eq>(), marq = new List<Eq>();
        readonly List<Light> beacons = new List<Light>(), boards = new List<Light>();
        readonly string[][] signs = { new[] { ".R.R.", "RRRRR", "RRRRR", ".RRR.", "..R.." }, new[] { "..YY.", "..Y.Y", "..Y..", "YYY..", "YYY.." }, new[] { "..C..", ".CCC.", "CCCCC", ".C.C.", "C...C" } };
        readonly Spr ship, ufo, blobA, blobB, lifeS;
        readonly Spr[] bat;
        readonly int[] gridIdx, gridCol;
        int score = 4210;
        readonly Actor sh = new Actor { T = 30 }, uf = new Actor { T = 140 }, blob = new Actor { T = 10, Y = 160, Dir = -1 }, bats = new Actor { T = 120 };

        public Arcade()
        {
            R = new Rng(8088);
            A = Pal("KDPVMROYWCBNTGgL", aK, aD, aP, aV, aM, aR, aO, aY, aW, aC, aB, aN, aT, aG, ag, aL);
            // sky: hard bands with 8-bit dither steps
            BakeBands(sky, new[] { 0, 22, 44, 66, 86, 100, HZ + 1 }, new[] { aK, aD, aP, aV, aM, aR, aR }, 0.66);
            // little ringed planet: back half of the ring, body, front half
            const int pcx = 270, pcy = 28, pr = 8;
            double ca = Math.Cos(-0.32), sa = Math.Sin(-0.32);
            Action<bool> ring = front =>
            {
                for (int y = -10; y <= 10; y++) for (int x = -18; x <= 18; x++)
                {
                    double u = x * ca - y * sa, v = x * sa + y * ca, e = u * u / 225 + v * v / 12;
                    if (e > 0.78 && e < 1.05 && (v > 0) == front) sky[(pcy + y) * W + pcx + x] = e < 0.9 ? aL : aV;
                }
            };
            ring(false);
            for (int y = -pr; y <= pr; y++)
                for (int x = -pr; x <= pr; x++)
                {
                    if (Hypot(x, y) > pr + 0.3) continue;
                    int X = pcx + x, Y = pcy + y;
                    double l = (x + y * 1.2) / (2 * pr);
                    int c = l < -0.4 ? aC : l < -0.15 ? (Dith(X, Y, 0.5) ? aB : aC) : l < 0.25 ? aB : l < 0.45 ? (Dith(X, Y, 0.5) ? aN : aB) : aN;
                    if (y == -2 || y == 3) c = l < 0.2 ? aN : aD;   // cloud bands
                    sky[Y * W + X] = c;
                }
            ring(true);
            for (int y = SY - SR - 7; y <= HZ; y++)
                for (int x = SX - SR - 7; x <= SX + SR + 7; x++)
                {
                    double d = Hypot(x - SX, y - SY);
                    if (d > SR && d <= SR + 6 && Dith(x, y, 0.55 - (d - SR) * 0.085)) sky[y * W + x] = y > 92 ? aO : aM;
                }
            // the floor never moves: baked under the horizon
            for (int y = HZ + 3; y < H; y++) for (int x = 0; x < W; x++) sky[y * W + x] = y < HZ + 6 ? (Dith(x, y, 0.5) ? aP : aD) : y < HZ + 12 ? (Dith(x, y, (y - HZ - 6) / 6.0) ? aK : aD) : aK;
            // and so do the lines from the vanishing point, kept as a pixel list to draw over the glints
            List<int> gi = new List<int>(), gc = new List<int>();
            for (int i = -16; i <= 16; i++)
            {
                int xb = SX + i * 26, xt = Rd(SX + (xb - SX) * 4.0 / (H - 1 - HZ));
                int x0 = xt, y0 = HZ + 4, x1 = xb, y1 = H - 1, dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), s = x0 < x1 ? 1 : -1, e = dx + dy;
                for (;;)
                {
                    if ((uint)x0 < W && (uint)y0 < H) { gi.Add(y0 * W + x0); gc.Add(GridC(y0)); }
                    if (y0 >= y1) break;
                    int e2 = 2 * e;
                    if (e2 >= dy) { e += dy; x0 += s; }
                    if (e2 <= dx) { e += dx; y0++; }
                    if (x0 < -2 || x0 > W + 1) break;
                }
            }
            gridIdx = gi.ToArray(); gridCol = gc.ToArray();

            int[] starC = { aW, aL, aC, aY };
            for (int k = 0; k < 95; k++) stars.Add(new Star { X = Ri(0, W - 1), Y = Fl(Math.Pow(R.Next(), 1.4) * 78), C = starC[Ri(0, 3)], Per = Ri(10, 40), Ph = Ri(0, 40), Plus = R.Next() < 0.1 });
            rf = Ridge(512, 22, 0.55); rn = Ridge(512, 14, 0.6);
            for (int x = 0; x < W; x++) { valleyF[x] = Valley(x, 100); valleyN[x] = Valley(x, 90); }

            // blocky city along the horizon
            int[] cwC = { aY, aC, aR, aW, aY };
            List<Bld> cb = GenBuildings(640, 8, 20, 96, 108, 0.35, 0, 4);
            for (int i = 0; i < cb.Count; i++)
            {
                Bld b = cb[i];
                if (i % 6 == 2) b.Top = Ri(82, 90);
                LRect(Lc, b.X, b.Top, b.Wd, HZ - b.Top + 1, aK);
                for (int x = b.X; x < b.X + b.Wd; x++) LPx(Lc, x, b.Top, aN);
                if (b.Top < 92) { int ax = b.X + (b.Wd >> 1); LLine(Lc, ax, b.Top - 1, ax, b.Top - 7, aN); beacons.Add(new Light { X = ax, Y = b.Top - 8, Ph = Ri(0, 20) }); }
                if (i % 11 == 6 && b.Wd >= 14)
                {
                    boards.Add(new Light { X = b.X + ((b.Wd - 11) >> 1), Y = b.Top - 10 });
                    LRect(Lc, b.X + ((b.Wd - 11) >> 1), b.Top - 10, 11, 9, aN); LRect(Lc, b.X + (b.Wd >> 1) - 2, b.Top - 1, 1, 1, aN); LRect(Lc, b.X + (b.Wd >> 1) + 2, b.Top - 1, 1, 1, aN);
                }
                if (i % 5 == 1 && b.Wd >= 10) eqs.Add(new Eq { X = b.X + 1, Wd = b.Wd - 2, Y0 = b.Top + 3, Y1 = HZ - 1, Seed = R.Next() * 10 });
                else if (i % 5 == 3) marq.Add(new Eq { X = b.X + 1, Wd = b.Wd - 2, Y0 = b.Top + 3 });
                else for (int y = b.Top + 3; y < HZ - 1; y += 3) for (int x = b.X + 1; x < b.X + b.Wd - 1; x += 2) if (R.Next() < 0.45) cwins.Add(new Cw { X = x, Y = y, On = R.Next() < 0.5, C = cwC[Ri(0, 4)] });
            }

            // sprites (all original)
            ship = new Spr(new[] { ".....CC.......", "...CCWWWC.....", "..BBWWWWWWCC..", ".BBNBWWWWWWWCW", "..BBWWWWWWCC..", "...CCWWWC.....", ".....CC......." }, A);
            Dictionary<char, int> ua = new Dictionary<char, int>(A); ua['x'] = aM;
            ufo = new Spr(new[] { "....CCCCC....", "...CWWWWWC...", ".LLLLLLLLLLL.", "LxLLxLLxLLxLL", ".LLLLLLLLLLL.", "...L.....L..." }, ua);
            blobA = new Spr(new[] { "...GGG...", "..GGGGG..", ".GWKGWKG.", ".GGGGGGG.", "GGGGgGGGG", "GGgGGGgGG", ".GgggggG.", "..g...g.." }, A);
            blobB = new Spr(new[] { ".........", "...GGG...", ".GGGGGGG.", "GGWKGGWKG", "GGGGGGGGG", "GGgGGGgGG", ".gggggg.." }, A);
            bat = new[] { new Spr(new[] { "L...L", ".LLL.", "..L.." }, A), new Spr(new[] { ".....", "LLLLL", "..L.." }, A), new Spr(new[] { "..L..", ".LLL.", "L...L" }, A) };
            lifeS = new Spr(new[] { "..T..", ".TCT.", "TTTTT" }, A);
            hud = Lerp(aL, aK, 0.45); hud2 = Lerp(aM, aK, 0.2);
        }

        int GridC(int y) { return y < HZ + 10 ? aV : y < HZ + 24 ? aM : aR; }
        float[] Ridge(int n, double amp, double rough)
        {
            float[] h = new float[n];
            int step = n;
            double a = amp;
            while (step > 1)
            {
                int half = step >> 1;
                for (int i = 0; i < n; i += step) h[i + half] = (float)(((double)h[i] + h[(i + step) % n]) / 2 + (R.Next() * 2 - 1) * a);
                a *= rough; step = half;
            }
            return h;
        }
        static double Valley(int x, int k) { return 0.12 + 0.88 * Math.Pow(Math.Min(1, Math.Abs(x - SX) / (double)k), 1.4); }
        // Periodic ridge with a valley carved around the sun.
        void Mountains(int off, float[] rd, int bse, int amp, double[] valley, bool near)
        {
            for (int x = 0; x < W; x++)
            {
                int wx = (x + off) % 512, top = Rd(HZ - bse - (amp + Math.Abs((double)rd[wx])) * valley[x]);
                bool lit = rd[(wx + 1) % 512] - rd[wx] > 0 == rd[wx] > 0;
                for (int y = Math.Max(0, top); y < HZ; y++)
                {
                    int c;
                    if (near) c = y == top ? aC : y < top + 3 && Dith(x, y, 0.5) ? aT : (wx % 6 == 0 && y > top + 1 && (y & 1) != 0) ? aN : aD;
                    else c = y == top ? (top < 82 ? aW : aM) : (top < 82 && y < top + 3) ? (Dith(x, y, 0.6) ? aL : aV) : (lit && y < top + 8 && Dith(x, y, 0.5)) ? aV : aP;
                    Fb[y * W + x] = c;
                }
            }
        }

        protected override void Update(int f)
        {
            if (f % 6 == 0) score += 10;
            foreach (Cw w in cwins) if (R.Next() < 0.006) w.On = !w.On;
            if (R.Next() < 0.03) shoot.Add(new Shot { X = Ri(80, W + 20), Y = Ri(2, 34), Life = Ri(10, 16) });
            for (int k = shoot.Count - 1; k >= 0; k--) { Shot s = shoot[k]; s.X -= 4; s.Y += 2; if (++s.L > s.Life) shoot.RemoveAt(k); }
            if (!sh.On) { if (--sh.T <= 0) { sh.On = true; sh.X = -16; sh.Yd = Ri(14, 40); sh.Y0 = sh.Yd; } }
            else
            {
                sh.X += 1.6; sh.Yd = sh.Y0 + Math.Sin(f * 0.12) * 3;
                if (f % 2 == 0) sh.Trail.Add(new[] { sh.X, sh.Yd + 3, 0 });
                if (sh.X > W + 20) { sh.On = false; sh.T = Ri(140, 320); score += 500; }
            }
            foreach (double[] t in sh.Trail) t[2]++;
            sh.Trail.RemoveAll(t => t[2] >= 14);
            if (!uf.On) { if (--uf.T <= 0) { uf.On = true; uf.Dir = R.Next() < 0.5 ? 1 : -1; uf.X = uf.Dir > 0 ? -16 : W + 4; uf.Yd = Ri(52, 66); uf.Beam = 0; } }
            else
            {
                if (uf.Beam > 0) uf.Beam--;
                else { uf.X += uf.Dir * 0.5; if (R.Next() < 0.006) uf.Beam = 50; }
                if (uf.X < -20 || uf.X > W + 20) { uf.On = false; uf.T = Ri(300, 700); }
            }
            if (!blob.On) { if (--blob.T <= 0) { blob.On = true; blob.Dir = R.Next() < 0.5 ? 1 : -1; blob.X = blob.Dir > 0 ? -10 : W + 2; blob.Y = Ri(150, 170); blob.Hop = 0; } }
            else { blob.Hop = (blob.Hop + 1) % 14; if (blob.Hop > 2) blob.X += blob.Dir * 0.9; if (blob.X < -14 || blob.X > W + 14) { blob.On = false; blob.T = Ri(120, 360); } }
            if (!bats.On) { if (--bats.T <= 0) { bats.On = true; bats.X = W + 4; bats.Yd = Ri(40, 60); } }
            else { bats.X -= 0.8; if (bats.X < -40) { bats.On = false; bats.T = Ri(300, 700); } }
        }

        static readonly int[] BatDy = { 0, -3, 2, -1 };
        static readonly int[] UfoLights = { 1, 4, 7, 10 };
        protected override void Draw(int f)
        {
            Array.Copy(sky, Fb, Fb.Length);
            foreach (Star s in stars)
            {
                if ((f + s.Ph) % s.Per >= s.Per * 0.7) { Px(s.X, s.Y, aV); continue; }
                Px(s.X, s.Y, s.C);
                if (s.Plus && (f + s.Ph) % s.Per < 4) { Px(s.X - 1, s.Y, aL); Px(s.X + 1, s.Y, aL); Px(s.X, s.Y - 1, aL); Px(s.X, s.Y + 1, aL); }
            }
            foreach (Shot s in shoot)
                for (int k = 0; k < 7; k++)
                {
                    int c = k == 0 ? aW : k < 3 ? aL : k < 5 ? aV : aP;
                    if (k > 4 && ((k + f) & 1) != 0) continue;
                    Px(s.X + k * 2, s.Y - k, c); Px(s.X + k * 2 + 1, s.Y - k, c);
                }
            // striped sun: the gaps slide down and widen toward the horizon
            int shift = (f >> 1) % 7;
            for (int y = SY - SR; y < HZ; y++)
            {
                double v = (double)(y - (SY - SR)) / (2 * SR);
                int u = y - (SY - 10);
                if (u >= 0 && (((u - shift) % 7) + 7) % 7 < Math.Min(4, 1 + u / 9)) continue;
                double half = Math.Sqrt(Math.Max(0, SR * SR - (y - SY) * (y - SY)));
                for (int x = (int)Math.Ceiling(SX - half); x <= SX + half; x++)
                    Px(x, y, v < 0.14 ? aY : v < 0.22 ? (((x + y) & 1) != 0 ? aO : aY) : v < 0.36 ? aO : v < 0.44 ? (((x + y) & 1) != 0 ? aR : aO) : v < 0.62 ? aR : (((x + y) & 1) != 0 ? aM : aR));
            }
            if (bats.On) for (int k = 0; k < 4; k++) { int fr = ((f >> 1) + k) % 4; Put(bat[fr == 3 ? 1 : fr], Rd(bats.X + k * 8), Rd(bats.Yd + BatDy[k] + Math.Sin(f * 0.3 + k) * 1.2)); }
            Mountains(f / 24, rf, 6, 16, valleyF, false);
            if (uf.On)
            {
                int x = Rd(uf.X), y = Rd(uf.Yd + Math.Sin(f * 0.2) * 1.5);
                if (uf.Beam > 0)
                    for (int j = 6; j < HZ - y; j++) { double hw = 2 + j * 0.25; for (double i = -hw; i <= hw; i++) { int bx = (int)(x + 6 + i); if (Dith(bx, y + j, 0.35)) Px(bx, y + j, aC); } }
                Put(ufo, x, y);
                int[] pal = { aM, aY, aC, aR };
                for (int k = 0; k < 4; k++) Px(x + UfoLights[k], y + 3, pal[((f >> 2) + k) % 4]);
            }
            Mountains(f / 12, rn, 2, 7, valleyN, true);
            // city: blinking windows, equalizer towers, marquees and a tiny billboard
            int offC = f / 8;
            Blit(Lc, offC);
            foreach (Cw w in cwins) if (w.On) { int x = Sx(w.X, offC, 640); if (x >= 0 && x < W) Px(x, w.Y, w.C); }
            foreach (Eq e in eqs)
            {
                int x0 = Sx(e.X, offC, 640);
                if (x0 > W || x0 + e.Wd < 0) continue;
                int rows = (e.Y1 - e.Y0) / 2;
                for (int c = 0; c < e.Wd; c += 2)
                {
                    int lv = Fl(rows * Math.Abs(Math.Sin(f * 0.21 + c * 0.9 + e.Seed) * 0.6 + Math.Sin(f * 0.53 + c * 1.7) * 0.4));
                    for (int r = 0; r < rows; r++) Px(x0 + c, e.Y1 - r * 2, r < lv ? (r < rows * 0.5 ? aG : r < rows * 0.8 ? aY : aR) : aN);
                }
            }
            foreach (Eq m in marq)
            {
                int x0 = Sx(m.X, offC, 640);
                for (int i = 0; i < m.Wd; i++) { Px(x0 + i, m.Y0, (i + (f >> 1)) % 4 < 2 ? aY : aO); Px(x0 + i, m.Y0 + 3, (((i - (f >> 1)) % 4) + 4) % 4 < 2 ? aC : aB); }
            }
            foreach (Light b in beacons) { int x = Sx(b.X, offC, 640); if ((f + b.Ph) % 20 < 8) { Px(x, b.Y, aR); Px(x, b.Y - 1, aO); } }
            foreach (Light bd in boards)
            {
                int x = Sx(bd.X, offC, 640);
                if (x < -12 || x > W) continue;
                Rect(x + 1, bd.Y + 1, 9, 7, aK);
                string[] g = signs[(f / 30) % 3];
                for (int j = 0; j < g.Length; j++) for (int i = 0; i < 5; i++) if (g[j][i] != '.') Px(x + 3 + i, bd.Y + 2 + j, A[g[j][i]]);
            }
            // horizon glow line
            for (int x = 0; x < W; x++) { Fb[HZ * W + x] = aY; Fb[(HZ + 1) * W + x] = aO; Fb[(HZ + 2) * W + x] = (x & 1) != 0 ? aR : aM; }
            // sun glints on the floor (odd rows only), then the baked grid lines and the rows rushing toward the viewer
            for (int y = HZ + 3; y < HZ + 22; y++)
            {
                if ((y & 1) == 0) continue;
                double hw = 30 - (y - HZ) * 1.3;
                int c = y < HZ + 8 ? aY : y < HZ + 14 ? aO : aR;
                for (int x = Rd(SX - hw); x <= SX + hw; x++) if (Math.Sin(y * 2.1 + f * 0.45 + Fl(x / 3.0) * 1.3) > 0.55) Px(x, y, c);
            }
            for (int i = 0; i < gridIdx.Length; i++) Fb[gridIdx[i]] = gridCol[i];
            double ph = f * 0.07 % 1;
            for (int k = 0; k < 40; k++)
            {
                double z = k + 1 - ph;
                if (z <= 0.2) continue;
                int y = HZ + Rd(66 / z);
                if (y >= H || y < HZ + 7) continue;
                Rect(0, y, W, 1, GridC(y));
                if (z < 1.5) Rect(0, y + 1, W, 1, aM);
            }
            // critters
            if (blob.On)
            {
                double air = blob.Hop > 2 ? Math.Sin((blob.Hop - 2) / 11.0 * Math.PI) * 9 : 0;
                Spr s = blob.Hop <= 2 ? blobB : blobA;
                int x = Rd(blob.X), sw = air > 5 ? 2 : air > 2 ? 1 : 0;
                Rect(x + 1 + sw, blob.Y + 1, 7 - 2 * sw, 1, aK);
                Put(s, x, Rd(blob.Y - s.H + 1 - air), blob.Dir > 0);
            }
            foreach (double[] t in sh.Trail) { int a = (int)t[2]; Px(Rd(t[0]), Rd(t[1]), a < 4 ? aY : a < 8 ? aO : (a & 1) != 0 ? aM : aV); }
            if (sh.On)
            {
                int x = Rd(sh.X), y = Rd(sh.Yd), fl = (f >> 1) % 3;
                Put(ship, x, y);
                int[] fire = fl == 0 ? new[] { aY, aO } : fl == 1 ? new[] { aW, aY, aO } : new[] { aY, aR };
                for (int k = 0; k < fire.Length; k++) { Px(x - k, y + 3, fire[k]); if (k < 1) { Px(x - k, y + 2, aO); Px(x - k, y + 4, aO); } }
            }
            // No HUD in the app: its text showed through buttons and between cards.
        }
    }
}
