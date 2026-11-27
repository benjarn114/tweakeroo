using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Tablero
{
    // What is shared: tweaks on, crosshair, stretched per game and colors. Each null = section not included.
    // Nothing of the PC: no paths, no user names.
    public class ShareData
    {
        public List<string> Tweaks;
        public string CrossNow;                    // Crosshair.Serialize()
        public List<string> CrossSaved;
        public Dictionary<string, Res> Stretch;    // per game, only W and H
        public string Theme;                       // Look id
        public Dictionary<string, string> Palettes; // per game, palette name
        public Dictionary<string, ColorPick> Colors; // per game
    }

    public class ShareLine { public string Text; public List<string> Names; }

    // Only the differences with the current state.
    public class SharePlan
    {
        public List<Tweak> On = new List<Tweak>(), Off = new List<Tweak>(), Manual = new List<Tweak>();
        public int Skipped;                        // unknown, retired or not available on this PC
        public string CrossNow;                    // the incoming one, when it differs
        public List<string> CrossAdd = new List<string>();
        public Dictionary<string, Res> Stretch = new Dictionary<string, Res>();
        public string Theme;
        public Dictionary<string, string> Palettes = new Dictionary<string, string>();
        public Dictionary<string, ColorPick> Colors = new Dictionary<string, ColorPick>();
        public List<string> Notes = new List<string>(); // what is skipped because of this PC or an open game
        public bool Empty { get { return On.Count + Off.Count + CrossAdd.Count + Stretch.Count + Palettes.Count + Colors.Count == 0 && CrossNow == null && Theme == null; } }

        static string GameName(string id) { return Game.All.First(g => g.Id == id).Name; }
        public List<ShareLine> Lines()
        {
            List<ShareLine> r = new List<ShareLine>();
            Func<List<Tweak>, List<string>> names = l => l.Select(t => t.Name).ToList();
            if (On.Count > 0) r.Add(new ShareLine { Text = On.Count == 1 ? "Se activa 1 tweak" : "Se activan " + On.Count + " tweaks", Names = names(On) });
            if (Off.Count > 0) r.Add(new ShareLine { Text = Off.Count == 1 ? "Se apaga 1" : "Se apagan " + Off.Count, Names = names(Off) });
            if (Manual.Count > 0) r.Add(new ShareLine { Text = "Se activan a mano: " + Manual.Count, Names = names(Manual) });
            if (CrossNow != null) r.Add(new ShareLine { Text = "Crosshair: se reemplaza" });
            if (CrossAdd.Count > 0) r.Add(new ShareLine { Text = "Crosshairs guardados nuevos: " + CrossAdd.Count });
            foreach (KeyValuePair<string, Res> s in Stretch) r.Add(new ShareLine { Text = "Stretched de " + GameName(s.Key) + ": " + s.Value.Name });
            if (Theme != null) r.Add(new ShareLine { Text = "Temática: " + Tablero.Theme.LookOf(Theme).Name });
            foreach (KeyValuePair<string, string> p in Palettes) r.Add(new ShareLine { Text = "Paleta de " + GameName(p.Key) + ": " + p.Value });
            foreach (KeyValuePair<string, ColorPick> c in Colors) r.Add(new ShareLine { Text = "Colores de " + GameName(c.Key) + ": " + Vivid.All.First(x => x.Id == c.Value.Profile).Name });
            if (Skipped > 0) r.Add(new ShareLine { Text = Skipped == 1 ? "Se salta 1 tweak que no existe o no aplica a este PC" : "Se saltan " + Skipped + " tweaks que no existen o no aplican a este PC" });
            r.AddRange(Notes.Select(x => new ShareLine { Text = x }));
            return r;
        }
    }

    public static class Share
    {
        public const int Max = 1 << 20;
        public const string Prefix = "TWK1:", Unknown = "Código no reconocido.";
        static InvalidDataException Bad() { return new InvalidDataException(Unknown); }

        // ---- Writing ----
        public static string Json(ShareData d)
        {
            Dictionary<string, object> o = new Dictionary<string, object> { { "v", 1 } };
            if (d.Tweaks != null) o["tweaks"] = d.Tweaks;
            if (d.CrossNow != null) o["crosshair"] = new Dictionary<string, object> { { "now", d.CrossNow }, { "saved", d.CrossSaved ?? new List<string>() } };
            if (d.Stretch != null) o["stretched"] = d.Stretch.ToDictionary(p => p.Key, p => (object)new Dictionary<string, object> { { "w", p.Value.W }, { "h", p.Value.H } });
            Dictionary<string, object> c = new Dictionary<string, object>();
            if (d.Theme != null) c["theme"] = d.Theme;
            if (d.Palettes != null) c["palettes"] = d.Palettes;
            if (d.Colors != null) c["profiles"] = d.Colors.ToDictionary(p => p.Key, p => (object)new Dictionary<string, object> { { "profile", p.Value.Profile }, { "vib", p.Value.Vib }, { "bright", p.Value.Bright }, { "contrast", p.Value.Contrast }, { "gamma", p.Value.Gamma } });
            if (c.Count > 0) o["colors"] = c;
            return new JavaScriptSerializer().Serialize(o);
        }
        public static string Code(ShareData d)
        {
            byte[] b = Encoding.UTF8.GetBytes(Json(d));
            using (MemoryStream m = new MemoryStream())
            {
                using (GZipStream g = new GZipStream(m, CompressionMode.Compress, true)) g.Write(b, 0, b.Length);
                return Prefix + Convert.ToBase64String(m.ToArray());
            }
        }

        // ---- Reading: untrusted input. A wrong shape rejects everything; unknown values are skipped ----
        public static ShareData ReadFile(string path)
        {
            if (new FileInfo(path).Length > Max) throw Bad();
            return Read(File.ReadAllText(path));
        }
        public static ShareData Read(string text)
        {
            if (text == null || text.Length > Max) throw Bad();
            try
            {
                text = text.Trim();
                if (text.StartsWith(Prefix, StringComparison.Ordinal)) text = Unzip(text.Substring(Prefix.Length));
                IDictionary<string, object> o = Of<IDictionary<string, object>>(new JavaScriptSerializer().DeserializeObject(text));
                if (o == null || !(Get(o, "v") is int) || (int)o["v"] != 1) throw Bad();
                ShareData d = new ShareData();
                d.Tweaks = Strings(Get(o, "tweaks"));
                if (d.Tweaks != null) d.Tweaks = d.Tweaks.Distinct().ToList();
                IDictionary<string, object> x = Of<IDictionary<string, object>>(Get(o, "crosshair"));
                if (x != null)
                {
                    d.CrossNow = Cross(Of<string>(Get(x, "now")));
                    d.CrossSaved = (Strings(Get(x, "saved")) ?? new List<string>()).Select(Cross).Distinct().ToList();
                }
                IDictionary<string, object> st = Of<IDictionary<string, object>>(Get(o, "stretched"));
                if (st != null)
                {
                    d.Stretch = new Dictionary<string, Res>();
                    foreach (KeyValuePair<string, object> p in st)
                    {
                        IDictionary<string, object> r = Of<IDictionary<string, object>>(p.Value);
                        int w = Int(Get(r, "w")), h = Int(Get(r, "h"));
                        if (w < 640 || w > 7680 || h < 480 || h > 4320) throw Bad();
                        if (Game.All.Any(g => g.Id == p.Key)) d.Stretch[p.Key] = new Res { W = w, H = h };
                    }
                }
                IDictionary<string, object> c = Of<IDictionary<string, object>>(Get(o, "colors"));
                if (c != null)
                {
                    string th = Of<string>(Get(c, "theme"));
                    if (th != null && Tablero.Theme.Looks.Any(l => l.Id == th)) d.Theme = th;
                    IDictionary<string, object> pa = Of<IDictionary<string, object>>(Get(c, "palettes"));
                    if (pa != null)
                    {
                        d.Palettes = new Dictionary<string, string>();
                        foreach (KeyValuePair<string, object> p in pa)
                        {
                            string n = Of<string>(p.Value);
                            if (n == null) throw Bad();
                            if (Game.All.Any(g => g.Id == p.Key) && Tablero.Theme.All.Any(t => t.Name == n)) d.Palettes[p.Key] = n;
                        }
                    }
                    IDictionary<string, object> pr = Of<IDictionary<string, object>>(Get(c, "profiles"));
                    if (pr != null)
                    {
                        d.Colors = new Dictionary<string, ColorPick>();
                        foreach (KeyValuePair<string, object> p in pr)
                        {
                            IDictionary<string, object> v = Of<IDictionary<string, object>>(p.Value);
                            string id = Of<string>(Get(v, "profile"));
                            ColorPick k = new ColorPick { Profile = id, Vib = Int(Get(v, "vib")), Bright = Int(Get(v, "bright")), Contrast = Int(Get(v, "contrast")), Gamma = Num(Get(v, "gamma")) };
                            if (Game.All.Any(g => g.Id == p.Key) && Vivid.All.Any(z => z.Id == id)) d.Colors[p.Key] = Vivid.Clean(k);
                        }
                    }
                }
                return d;
            }
            catch (Exception) { throw Bad(); } // whatever fails, nothing is used
        }
        static string Unzip(string b64)
        {
            using (GZipStream g = new GZipStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress))
            using (MemoryStream m = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = g.Read(buf, 0, buf.Length)) > 0) { m.Write(buf, 0, n); if (m.Length > Max) throw Bad(); }
                return new UTF8Encoding(false, true).GetString(m.ToArray());
            }
        }
        static object Get(IDictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) ? v : null; }
        static T Of<T>(object v) where T : class { if (v == null) return null; T t = v as T; if (t == null) throw Bad(); return t; }
        static int Int(object v) { if (v is int) return (int)v; throw Bad(); }
        static double Num(object v) { if (v is int || v is long || v is decimal || v is double) return Convert.ToDouble(v, CultureInfo.InvariantCulture); throw Bad(); }
        static List<string> Strings(object v)
        {
            IList a = Of<IList>(v);
            if (a == null) return null;
            List<string> r = new List<string>();
            foreach (object x in a) { string s = Of<string>(x); if (s == null) throw Bad(); r.Add(s); }
            return r;
        }
        // A crosshair whose code does not read is rejected, not swapped for the default one.
        static string Cross(string s)
        {
            if (s == null || s.Length > 4000) throw Bad();
            Crosshair.FromValorant(s.Split('|')[0]);
            return Crosshair.Deserialize(s).Serialize();
        }

        // ---- Differences ----
        // on: already on (read from the system). canOff: what a bulk undo would turn off (on and turned on by the app).
        public static SharePlan Diff(ShareData now, ShareData inc, IEnumerable<Tweak> here, Func<Tweak, bool> on, Func<Tweak, bool> canOff)
        {
            SharePlan p = new SharePlan();
            if (inc.Tweaks != null)
            {
                List<Tweak> list = here.ToList();
                HashSet<string> want = new HashSet<string>(inc.Tweaks);
                foreach (string id in inc.Tweaks)
                {
                    Tweak t = list.FirstOrDefault(x => x.Id == id);
                    if (t == null || t.Hidden) p.Skipped++;
                    else if (on(t)) continue;
                    else if (t.Level == Level.Riesgo) p.Manual.Add(t); // Riesgo is turned on one by one, with confirmation
                    else p.On.Add(t);
                }
                p.Off = list.Where(t => !want.Contains(t.Id) && canOff(t)).ToList();
            }
            if (inc.CrossNow != null)
            {
                if (inc.CrossNow != now.CrossNow) p.CrossNow = inc.CrossNow;
                List<string> had = now.CrossSaved ?? new List<string>();
                p.CrossAdd = inc.CrossSaved.Concat(new[] { inc.CrossNow }).Distinct().Where(x => !had.Contains(x)).ToList();
            }
            if (inc.Stretch != null)
                foreach (KeyValuePair<string, Res> s in inc.Stretch) { Res r; if (now.Stretch == null || !now.Stretch.TryGetValue(s.Key, out r) || !s.Value.Same(r)) p.Stretch[s.Key] = s.Value; }
            if (inc.Theme != null && inc.Theme != now.Theme) p.Theme = inc.Theme;
            if (inc.Palettes != null)
                foreach (KeyValuePair<string, string> s in inc.Palettes) { string n; if (now.Palettes == null || !now.Palettes.TryGetValue(s.Key, out n) || n != s.Value) p.Palettes[s.Key] = s.Value; }
            if (inc.Colors != null)
                foreach (KeyValuePair<string, ColorPick> s in inc.Colors)
                {
                    ColorPick c;
                    if (now.Colors == null || !now.Colors.TryGetValue(s.Key, out c)) c = Vivid.All[0].Pick;
                    ColorPick v = s.Value;
                    if (v.Profile != c.Profile || v.Vib != c.Vib || v.Bright != c.Bright || v.Contrast != c.Contrast || Math.Abs(v.Gamma - c.Gamma) > 0.001) p.Colors[s.Key] = v;
                }
            return p;
        }

        // The incoming crosshair becomes the one in use; the saved ones are added, renamed if the name is taken. Nothing saved is lost.
        public static void PutCross(Prefs pr, SharePlan p)
        {
            string use = p.CrossNow;
            foreach (string s in p.CrossAdd)
            {
                Crosshair c = Crosshair.Deserialize(s);
                c.Name = Prefs.FreeName(c.Name, pr.CrossSaved.Select(x => Crosshair.Deserialize(x).Name));
                string x2 = c.Serialize();
                pr.CrossSaved.Add(x2);
                if (s == p.CrossNow) use = x2;
            }
            if (use != null) pr.CrossNow = use;
        }

        // Texts made in code, for the translation check (SelfTest.cs).
        public static List<string> Texts()
        {
            Tweak t = new Tweak { Id = "x", Name = "x" };
            SharePlan a = new SharePlan { On = { t }, Off = { t }, Manual = { t }, Skipped = 1, CrossNow = "x", CrossAdd = { "x" }, Theme = "noche" };
            SharePlan b = new SharePlan { On = { t, t }, Off = { t, t }, Skipped = 2 };
            foreach (Game g in Game.All) { a.Stretch[g.Id] = new Res { W = 1720, H = 1080 }; a.Palettes[g.Id] = g.Palette; a.Colors[g.Id] = Vivid.All[2].Pick; }
            List<string> r = a.Lines().Concat(b.Lines()).Select(x => x.Text).ToList();
            foreach (Game g in Game.All) r.AddRange(new[] { "Stretched de " + g.Name + ": 1720 × 1080 no está disponible en esta pantalla.", g.Name + " está abierto: su stretched se salta." });
            r.AddRange(new[] { "Fortnite está abierto: se saltan 3 ajustes del juego.", Unknown, "Código copiado.", "No se pudo copiar: x", "No se pudo guardar: x", "No se pudo leer: x", "Configuración exportada.", "Configuración aplicada.", "Ya coincide con esta configuración.", "Ver nombres", "Ocultar nombres", "Tweakeroo" });
            return r;
        }
    }

    // ---- Settings card and import preview ----
    partial class Ui
    {
        SharePlan sharing; // the plan on screen in the preview

        void StartShare()
        {
            F<Btn>("ShareExport").Click += async (s, e) => await ShareExport();
            F<Btn>("ShareImport").Click += async (s, e) => await ShareImport(true);
            F<Btn>("ShareCopy").Click += async (s, e) =>
            {
                await SafeRefresh();
                try { Clipboard.SetText(Share.Code(ShareNow())); Say("Código copiado.", false); }
                catch (COMException ex) { Say("No se pudo copiar: " + ex.Message, false); }
            };
            F<Btn>("SharePaste").Click += async (s, e) => await ShareImport(false);
            F<Btn>("ShareApply").Click += async (s, e) => { SharePlan p = sharing; ShareHide(); if (p != null) await ShareApply(p); };
            F<Btn>("ShareCancel").Click += (s, e) => ShareHide();
            F<FrameworkElement>("ShareShade").MouseLeftButtonDown += (s, e) => ShareHide();
            F<FrameworkElement>("ShareBox").PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) ShareHide(); };
        }

        // The tweaks as Windows has them now, not as the list last showed them: callers refresh first.
        ShareData ShareNow()
        {
            return new ShareData
            {
                Tweaks = every == null ? null : every.Where(r => r.State == State.On).Select(r => r.T.Id).ToList(),
                CrossNow = Prefs.Now.CrossNow,
                CrossSaved = Prefs.Now.CrossSaved.ToList(),
                Stretch = Game.All.Where(g => { StretchSession x; return sessions.TryGetValue(g.Id, out x) && x != null && x.Last != null; }).ToDictionary(g => g.Id, g => new Res { W = sessions[g.Id].Last.W, H = sessions[g.Id].Last.H }),
                Theme = Prefs.Now.Theme,
                Palettes = Game.All.ToDictionary(g => g.Id, g => Prefs.PaletteOf(dir, g).Name),
                Colors = new Dictionary<string, ColorPick>(Prefs.Now.Colors)
            };
        }

        async Task ShareExport()
        {
            Microsoft.Win32.SaveFileDialog d = new Microsoft.Win32.SaveFileDialog { FileName = "Tweakeroo.tweakeroo", Filter = "Tweakeroo|*.tweakeroo" };
            if (d.ShowDialog(w) != true) return;
            await SafeRefresh();
            try { File.WriteAllText(d.FileName, Share.Json(ShareNow())); Say("Configuración exportada.", false); }
            catch (Exception ex) { Say("No se pudo guardar: " + ex.Message, false); }
        }

        async Task ShareImport(bool file)
        {
            if (Busy() != null) { Say(Busy(), false); return; }
            ShareData inc;
            try
            {
                if (file)
                {
                    Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog { Filter = "Tweakeroo|*.tweakeroo" };
                    if (d.ShowDialog(w) != true) return;
                    inc = Share.ReadFile(d.FileName);
                }
                else inc = Share.Read(Clipboard.GetText());
            }
            catch (InvalidDataException ex) { Say(ex.Message, false); return; }
            catch (Exception ex) { Say("No se pudo leer: " + ex.Message, false); return; }
            await SafeRefresh();
            SharePlan p = Share.Diff(ShareNow(), inc, every == null ? new List<Tweak>() : every.Select(r => r.T),
                t => every.First(r => r.T == t).State == State.On, t => { Row r = every.First(x => x.T == t); return r.State != State.Off && engine.Mine(t); });
            ShareHold(p);
            if (p.Empty && p.Manual.Count == 0) { Say("Ya coincide con esta configuración.", false); return; }
            ShareShow(p);
        }

        // What this PC or an open game does not allow leaves the plan and is said in the preview.
        void ShareHold(SharePlan p)
        {
            if (IniOp.GameRunning())
            {
                int n = p.On.RemoveAll(t => t.Ops.Any(o => o is IniOp)) + p.Off.RemoveAll(t => t.Ops.Any(o => o is IniOp));
                if (n > 0) p.Notes.Add("Fortnite está abierto: se saltan " + n + " ajustes del juego.");
            }
            foreach (string id in p.Stretch.Keys.ToList())
            {
                Game g = Game.All.First(x => x.Id == id);
                Res want = p.Stretch[id], use = null;
                StretchSession x2;
                if (sessions.TryGetValue(id, out x2) && x2 != null && x2.Pending) { p.Stretch.Remove(id); p.Notes.Add(g.Name + " está abierto: su stretched se salta."); continue; }
                try
                {
                    Res native = resolutions == null ? null : resolutions[0];
                    double least = g == Game.Valorant ? Display.ValorantRatio : 0;
                    use = native == null || x2 == null ? null : resolutions.FirstOrDefault(r => r.Same(want) && (r.Same(native) || r.Aspect >= least - 0.0001));
                    if (use == null && native != null && x2 != null && Custom.Available() && Custom.Invalid(want.W.ToString(), want.H.ToString(), native, least) == null)
                    {
                        Res mode = Display.Modes().Where(m => m.Same(want)).OrderByDescending(m => m.Hz).FirstOrDefault();
                        use = new Res { W = want.W, H = want.H, Hz = mode != null ? mode.Hz : native.Hz };
                    }
                }
                catch (Exception) { use = null; }
                if (use != null) p.Stretch[id] = use;
                else { p.Stretch.Remove(id); p.Notes.Add("Stretched de " + g.Name + ": " + want.Name + " no está disponible en esta pantalla."); }
            }
        }

        void ShareShow(SharePlan p)
        {
            sharing = p;
            StackPanel box = F<StackPanel>("ShareLines");
            box.Children.Clear();
            Style link = (Style)w.FindResource("LinkBtn");
            foreach (ShareLine x in p.Lines())
            {
                DockPanel row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                Ellipse dot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 6, 14, 0) };
                dot.SetResourceReference(Shape.FillProperty, "On");
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                StackPanel body = new StackPanel();
                body.Children.Add(new Tb { Text = x.Text, FontSize = 16, TextWrapping = TextWrapping.Wrap });
                if (x.Names != null && x.Names.Count > 0)
                {
                    Tb names = new Tb { Text = string.Join("\n", x.Names), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
                    names.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
                    Btn more = new Btn { Style = link, Content = "Ver nombres", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
                    more.Click += (s, e) => { bool open = names.Visibility != Visibility.Visible; names.Visibility = open ? Visibility.Visible : Visibility.Collapsed; more.Content = open ? "Ocultar nombres" : "Ver nombres"; };
                    body.Children.Add(more);
                    body.Children.Add(names);
                }
                row.Children.Add(body);
                box.Children.Add(row);
            }
            F<Btn>("ShareApply").Visibility = p.Empty ? Visibility.Collapsed : Visibility.Visible;
            FrameworkElement b = F<FrameworkElement>("ShareBox");
            b.Visibility = Visibility.Visible;
            Fx.From(b, UIElement.OpacityProperty, 0, 1, 160, Fx.Out, 0);
            Fx.Pop(F<FrameworkElement>("ShareCard"), 0.94);
            (p.Empty ? F<Btn>("ShareCancel") : F<Btn>("ShareApply")).Focus();
        }
        void ShareHide() { sharing = null; F<FrameworkElement>("ShareBox").Visibility = Visibility.Collapsed; }

        // By the same paths as the UI; the tweaks last, through Bulk, which saves the state before.
        async Task ShareApply(SharePlan p)
        {
            if (Busy() != null) { Say(Busy(), false); return; }
            if (p.CrossNow != null || p.CrossAdd.Count > 0)
            {
                if (crossView != null) CrossFlush();
                Share.PutCross(Prefs.Now, p);
                SavePrefs();
                if (crossView != null) { cross = CrossLoad(Prefs.Now.CrossNow) ?? new Crosshair(); crossView.X = cross; if (overlay != null) overlay.View.X = cross; ShowCross(); }
            }
            foreach (KeyValuePair<string, Res> s in p.Stretch) { KeyValuePair<string, Res> s2 = s; Try(() => sessions[s2.Key].Choose(s2.Value)); }
            if (p.Stretch.Count > 0 && stretch != null) LoadSession();
            foreach (KeyValuePair<string, ColorPick> c in p.Colors) Prefs.Now.Colors[c.Key] = c.Value;
            if (p.Colors.Count > 0) { SavePrefs(); ShowColors(); }
            bool here = false;
            foreach (KeyValuePair<string, string> c in p.Palettes)
            {
                Game g = Game.All.First(x => x.Id == c.Key);
                Palette pl = Theme.Named(c.Value);
                KeyValuePair<string, string> c2 = c;
                Attempt(() => { Directory.CreateDirectory(dir); File.WriteAllText(System.IO.Path.Combine(dir, g.PaletteFile), c2.Value); });
                if (g != game) continue;
                swapping = true;
                F<ListBox>("Palettes").SelectedItem = pl;
                swapping = false;
                F<TextBlock>("PaletteName").Text = "Color: " + pl.Name;
                Theme.LookOf("simple").Pal = pl;
                here = true;
            }
            if (p.Theme != null) ChangeLook(Theme.LookOf(p.Theme));
            else if (here && look.Simple) Theme.Apply(w, Theme.LookOf("simple").Pal);
            if (p.Off.Count > 0) await Bulk(every.Where(r => p.Off.Contains(r.T)).ToList(), false);
            if (p.On.Count > 0) await Bulk(every.Where(r => p.On.Contains(r.T)).ToList(), true);
            if (p.On.Count + p.Off.Count == 0) Say("Configuración aplicada.", false);
        }

#if SHOT
        // Captures only: the preview of a sample import, never applied.
        internal async Task SharePreviewShot()
        {
            await SafeRefresh();
            ShareData now = ShareNow();
            ShareData inc = new ShareData { Tweaks = (every ?? new List<Row>()).Where(r => r.State != State.On).Take(12).Select(r => r.T.Id).Concat(new[] { "no-existe" }).ToList(), CrossNow = new Crosshair { Name = "Importado" }.Serialize(), CrossSaved = new List<string>(), Stretch = new Dictionary<string, Res> { { Game.Fortnite.Id, new Res { W = 1720, H = 1080 } } }, Theme = "arcade", Colors = new Dictionary<string, ColorPick> { { Game.Fortnite.Id, Vivid.All[2].Pick } } };
            SharePlan p = Share.Diff(now, inc, (every ?? new List<Row>()).Select(r => r.T), t => every.First(r => r.T == t).State == State.On, t => false);
            ShareShow(p);
        }
#endif
    }
}
