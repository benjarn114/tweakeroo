using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Tablero
{
    public class Problem
    {
        public string Id, Name, Why, Chip;
        public string[] Keys; // phrases in Spanish and English; any UI language
    }

    // Recognises a problem written in the player's own words, without AI or internet.
    public static class Assistant
    {
        public static readonly List<Problem> All = new List<Problem>
        {
            new Problem { Id = "fps", Chip = "Bajones de FPS", Name = "Bajones de FPS y tirones", Why = "Suelen venir de la gráfica o el procesador al límite, de programas en segundo plano o de la caché de sombreadores al empezar la partida.",
                Keys = new[] { "fps", "bajones", "bajon", "bajones de fps", "bajan", "bajan los fps", "caidas de fps", "pocos fps", "fps bajos", "tirones", "tiron", "se traba", "trabado", "tranca", "se congela", "lag", "rendimiento", "va lento", "stutter", "stuttering", "fps drops", "low fps", "freeze", "freezes", "performance" } },
            new Problem { Id = "delay", Chip = "Mucho delay", Name = "Retraso al jugar (input lag)", Why = "Lo suman el VSync, la gráfica al 100 % y una pantalla por debajo de sus Hz máximos. Reflex y la baja latencia de NVIDIA lo reducen.",
                Keys = new[] { "delay", "input lag", "input delay", "retraso", "latencia", "respuesta lenta", "tarda en responder", "latency", "responde tarde" } },
            new Problem { Id = "network", Chip = "Ping alto", Name = "Ping alto, lag o cortes", Why = "Lo más común es el Wi-Fi o una descarga en segundo plano. Si la red de casa va bien, suele ser la distancia al servidor.",
                Keys = new[] { "ping", "ping alto", "lag", "lagea", "laguea", "se corta", "cortes", "desconecta", "me desconecta", "perdida de paquetes", "paquetes", "wifi", "internet", "conexion", "high ping", "packet loss", "disconnect", "disconnects", "connection", "jitter" } },
            new Problem { Id = "mouse", Chip = "Sensibilidad rara", Name = "Sensibilidad rara del ratón", Why = "Suele ser la aceleración de Windows (Mejorar precisión del puntero), un cambio de DPI o la resolución estirada.",
                Keys = new[] { "sensi", "sensibilidad", "mouse", "raton", "aceleracion", "mejorar precision del puntero", "precision del puntero", "puntero", "dpi", "raw input", "apuntar", "sensitivity", "acceleration", "mouse acceleration", "enhance pointer precision", "aim" } },
            new Problem { Id = "launch", Chip = "El juego no abre", Name = "El juego no abre o se cierra", Why = "Suele ser el driver de la gráfica, archivos del juego dañados, el anticheat o un programa superpuesto.",
                Keys = new[] { "no abre", "no me abre", "no se abre", "no inicia", "no arranca", "no carga", "se cierra", "se cierra solo", "crash", "crashea", "se crashea", "pantalla negra", "error al abrir", "anticheat", "vanguard", "wont launch", "wont start", "wont open", "doesnt launch", "doesnt open", "not launching", "launch", "crashes", "black screen" } },
            new Problem { Id = "stretched", Chip = "Stretched no anda", Name = "Stretched no funciona o hay bandas negras", Why = "La gráfica tiene que escalar a pantalla completa y el juego tiene que rellenar la pantalla en vez de poner bandas negras.",
                Keys = new[] { "stretched", "stretch", "estirada", "la estirada", "estirado", "estirar", "estrechada", "no estira", "no se estira", "bandas negras", "barras negras", "4 3", "resolucion", "black bars", "resolution", "stretched not working" } },
            new Problem { Id = "colors", Chip = "Colores apagados", Name = "Colores o brillo de la pantalla", Why = "La intensidad de color solo se cambia con NVIDIA. Con HDR de Windows activo, brillo, contraste y gamma quedan bloqueados.",
                Keys = new[] { "colores", "color", "vibrance", "vibrancia", "intensidad", "brillo", "oscuro", "muy oscuro", "lavado", "colores lavados", "saturacion", "gamma", "contraste", "pantalla oscura", "brightness", "washed out", "too dark", "digital vibrance", "colors", "hdr" } },
        };
        public const double Least = 0.7;  // one typo-matched word is enough
        const double Close = 0.75;        // the second within 75 % of the first: ask which

        // Lowercase, no accents or apostrophes, repeated letters once ("lagggg" = "lag"), split into words.
        public static List<string> Words(string s)
        {
            StringBuilder b = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant().Replace("'", "").Replace("’", "").Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                char x = char.IsLetterOrDigit(c) ? c : ' ';
                if (char.IsLetter(x) && b.Length > 0 && b[b.Length - 1] == x) continue;
                b.Append(x);
            }
            return b.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        static int Dist(string a, string b)
        {
            int[] p = new int[b.Length + 1], q = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) p[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                q[0] = i;
                for (int j = 1; j <= b.Length; j++) q[j] = Math.Min(Math.Min(p[j] + 1, q[j - 1] + 1), p[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                int[] t = p; p = q; q = t;
            }
            return p[b.Length];
        }
        // 1 the same word, 0.7 a typo (one letter from 5 letters, two from 8), 0 another word.
        static double Near(string w, string k)
        {
            if (w == k) return 1;
            int n = Math.Min(w.Length, k.Length), max = n >= 8 ? 2 : n >= 5 ? 1 : 0;
            return max > 0 && Math.Abs(w.Length - k.Length) <= max && Dist(w, k) <= max ? 0.7 : 0;
        }
        // The phrase words in order, with up to two others in between ("se me traba" is "se traba").
        static double Phrase(List<string> words, List<string> key)
        {
            double best = 0;
            for (int s = 0; s < words.Count; s++)
            {
                double sum = Near(words[s], key[0]);
                int at = s;
                for (int k = 1; k < key.Count && sum > 0; k++)
                {
                    double got = 0;
                    int found = at;
                    for (int j = at + 1; j < words.Count && j <= at + 3 && got < 1; j++) { double v = Near(words[j], key[k]); if (v > got) { got = v; found = j; } }
                    at = found;
                    sum = got == 0 ? 0 : sum + got;
                }
                best = Math.Max(best, sum);
            }
            return best;
        }
        public static double Score(Problem p, List<string> words) { return p.Keys.Sum(k => Phrase(words, Words(k))); }

        // Empty: not recognised. One: recognised. Two: too close, ask which.
        public static List<Problem> Match(string text)
        {
            List<string> w = Words(text);
            List<KeyValuePair<Problem, double>> s = All.Select(p => new KeyValuePair<Problem, double>(p, Score(p, w))).Where(x => x.Value >= Least).OrderByDescending(x => x.Value).ToList();
            if (s.Count > 1 && s[1].Value >= s[0].Value * Close) return new List<Problem> { s[0].Key, s[1].Key };
            return s.Take(1).Select(x => x.Key).ToList();
        }
    }

    // Help: the player describes the problem and the PC is checked with what the app already reads.
    partial class Ui
    {
        Circuit help; // always the last section, below Upgrades

        class HelpData
        {
            public Hw Hw, Quick;
            public Game Game;
            public List<Row> Rows;
            public List<Match> Played, Fortnite;
            public Res Last, Native;
            public ColorPick Color;
            public bool ValOn;
        }

        string lastAsk = ""; // the player's last question, for the report
        Action unsolved;     // "No se solucionó" of the newest answer (SHOT option "reporte")

        void StartHelp()
        {
            WrapPanel chips = F<WrapPanel>("HelpChips");
            foreach (Problem p in Assistant.All.Where(x => x.Id != "colors")) chips.Children.Add(Chip(p.Chip));
            TextBox box = F<TextBox>("HelpInput");
            box.KeyDown += async (s, e) => { if (e.Key == Key.Enter) await Ask(box.Text); };
            F<Btn>("HelpSend").Click += async (s, e) => await Ask(box.Text);
        }

        // Without act, the chip asks its own text.
        Btn Chip(string text, Action act = null)
        {
            Btn b = new Btn { Style = ((Btn)w.FindName("Rescan")).Style /* the theme reuses the key "Btn" for a brush */, Content = text, Height = 34, Padding = new Thickness(12, 0, 12, 0), FontSize = 13.5, Margin = new Thickness(0, 0, 8, 8) };
            b.Click += async (s, e) => { if (act != null) act(); else await Ask(text); };
            return b;
        }

        Btn Link(string text, Action act)
        {
            Btn b = new Btn { Style = (Style)w.Resources["LinkBtn"], Content = text, Margin = new Thickness(0, 0, 20, 0) };
            b.Click += (s, e) => act();
            return b;
        }

        TextBlock Line(string text) { return new Tb { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap }; }

        // A chat bubble at the end of the log: the player's on the right, the app's on the left.
        Border Bubble(bool mine, UIElement child)
        {
            Border b = new Border { CornerRadius = new CornerRadius(14), Padding = mine ? new Thickness(12, 8, 12, 8) : new Thickness(14, 12, 14, 12), HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, MaxWidth = mine ? 420 : 560, Margin = new Thickness(0, 0, 0, 12), Child = child };
            b.SetResourceReference(Border.BackgroundProperty, mine ? "Card" : "Rail");
            F<StackPanel>("HelpLog").Children.Add(b);
            return b;
        }
        void Said(string text) { Reveal(Bubble(true, new TextBlock { Text = text, FontSize = 15, TextWrapping = TextWrapping.Wrap })); }
        Border Reply(UIElement child) { Border b = Bubble(false, child); Reveal(b); return b; }

        // The newest message at the bottom; a long answer shows from its start.
        void Reveal(FrameworkElement e)
        {
            ScrollViewer page = F<ScrollViewer>("HelpPage");
            page.UpdateLayout();
            page.ScrollToEnd();
            page.UpdateLayout();
            e.BringIntoView(new Rect(0, 0, 1, 1));
        }

        internal async Task Ask(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            F<TextBox>("HelpInput").Text = "";
            // Chips are app texts and get translated; what the player typed stays as typed.
            bool chip = Assistant.All.Any(q => q.Chip == text);
            lastAsk = chip ? L.T(text) : text;
            Said(lastAsk);
            List<Problem> found = Assistant.Match(text);
            if (found.Count != 1)
            {
                if (found.Count == 0) Choose("No se reconoció el problema. Prueba con otras palabras o elige uno:", Assistant.All, true);
                else Choose("¿Cuál se parece más?", found, false);
                return;
            }
            Problem pr = found[0];
            StackPanel body = new StackPanel();
            TextBlock why = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap };
            why.Inlines.Add(new System.Windows.Documents.Run(L.T(pr.Name) + ". ") { FontWeight = FontWeights.SemiBold });
            why.Inlines.Add(new System.Windows.Documents.Run(L.T(pr.Why)));
            body.Children.Add(why);
            ItemsControl list = new ItemsControl { Focusable = false, ItemTemplate = (DataTemplate)w.Resources["HelpRow"], Margin = new Thickness(0, 8, 0, 6) };
            list.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(HelpClick));
            body.Children.Add(list);
            Action notFixed = () => { Said(L.T("No se solucionó")); ReportForm(pr, list.ItemsSource as List<Check>); };
            unsolved = notFixed;
            StackPanel more = new StackPanel { Orientation = Orientation.Horizontal };
            more.Children.Add(Link("No es este problema", () => { Said(L.T("No es este problema")); Choose("¿Cuál se parece más?", Assistant.All.Where(x => x != pr), true); }));
            more.Children.Add(Link("No se solucionó", notFixed));
            body.Children.Add(more);
            Border a = Bubble(false, body);
            a.Visibility = Visibility.Collapsed; // shown when the checks are ready, or as a skeleton if they take a while
            await Fill(pr, list, a);
        }
        internal void Unsolved() { if (unsolved != null) unsolved(); }

        // A reply with problems to pick; report adds the chip that opens the report form.
        void Choose(string text, IEnumerable<Problem> ps, bool report)
        {
            StackPanel s = new StackPanel();
            s.Children.Add(Line(text));
            WrapPanel c = new WrapPanel { Margin = new Thickness(0, 10, 0, -8) };
            foreach (Problem p in ps) c.Children.Add(Chip(p.Chip));
            if (report) c.Children.Add(Chip("Otro problema: enviar reporte", () => { Said(L.T("Otro problema: enviar reporte")); ReportForm(null, null); }));
            s.Children.Add(c);
            Reply(s);
        }

        // Same checks as Upgrades, after reading the PC in the background; a small skeleton bubble only if it takes a while.
        async Task Fill(Problem p, ItemsControl list, Border a)
        {
            HelpData d = new HelpData { Hw = hw, Quick = quick, Game = game, Rows = rows.ToList(), Played = matches, Fortnite = PlayedIn(Game.Fortnite), Color = Prefs.Now.ColorOf(game), ValOn = Val != null && Val.Stretch };
            if (stretch != null) d.Last = stretch.Last;
            if (resolutions != null && resolutions.Count > 0) d.Native = resolutions[0];
            Task<List<Check>> t = Task.Run(() =>
            {
                try { return Diagnose(p, d); }
                catch (Exception ex) { return new List<Check> { new Check { Kind = Kind.Gratis, Title = "No se pudo revisar el PC", Text = ex.Message } }; }
            });
            UIElement body = a == null ? null : a.Child;
            bool skel = false;
            if (a != null && await Task.WhenAny(t, Task.Delay(300)) != t)
            {
                StackPanel s = new StackPanel();
                foreach (double wd in new[] { 230.0, 180, 205 }) s.Children.Add(new ContentControl { Style = (Style)w.Resources["Skel"], Width = wd, Height = 12, Margin = new Thickness(0, 4, 0, 4) });
                a.Child = s;
                a.Visibility = Visibility.Visible;
                skel = true;
                Shimmer(true);
                Reveal(a);
            }
            List<Check> c = await t;
            foreach (Check k in c.Where(y => y.Fix != null))
            {
                Action f = k.Fix;
                k.Fix = () => { Task r = AfterFix(f, p, list); };
            }
            list.Tag = p;
            list.ItemsSource = c;
            if (skel && loaded.Task.IsCompleted) Shimmer(false);
            if (a == null) return;
            a.Child = body;
            a.Visibility = Visibility.Visible;
            Reveal(a);
            list.UpdateLayout();
            for (int i = 0; i < c.Count; i++)
            {
                FrameworkElement x = list.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (x != null) Fx.Enter(x, i, 18);
            }
        }

        // Tweak fixes go through Bulk (saved and reversible); afterwards the answer is checked again.
        async Task AfterFix(Action fix, Problem p, ItemsControl list)
        {
            try { fix(); }
            catch (Exception ex) { Say(ex is InvalidOperationException ? ex.Message : "No se pudo cambiar: " + ex.Message, false); }
            while (working) await Task.Delay(100);
            await Fill(p, list, null);
        }

        void HelpClick(object sender, RoutedEventArgs e)
        {
            Button b = e.OriginalSource as Button;
            Check c = b == null ? null : b.DataContext as Check;
            if (c == null || !c.HasUrl) return;
            if (c.Fix != null) { c.Fix(); return; }
            try { StretchSession.Open(c.Url); } catch (Exception ex) { Say("No se pudo abrir: " + ex.Message, false); }
        }

        // The report is written here and goes out by opening GitHub in the browser: the app itself sends nothing.
        void ReportForm(Problem p, List<Check> checks)
        {
            string question = lastAsk;
            List<string> pics = new List<string>();
            Dictionary<string, ImageSource> seen = new Dictionary<string, ImageSource>();
            Style plain = ((Btn)w.FindName("Rescan")).Style;
            StackPanel s = new StackPanel { Width = 500 };
            TextBox msg = new TextBox { Style = (Style)w.Resources["Area"], Tag = "Qué pasa, desde cuándo y qué se probó" };
            AutomationProperties.SetName(msg, L.T("Qué pasa, desde cuándo y qué se probó"));
            Btn attach = new Btn { Style = plain, Content = "Adjuntar imágenes", Height = 32, Padding = new Thickness(12, 0, 12, 0), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            WrapPanel thumbs = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            CheckBox pc = new CheckBox { Style = (Style)w.Resources["Check"], Content = L.T("Incluir datos de la PC"), IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
            TextBlock note = new Tb { Text = "El reporte es público en GitHub. No incluyas datos personales.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Ink2");
            Btn send = new Btn { Style = (Style)w.Resources["BtnCta"], Content = "Enviar reporte", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), IsEnabled = false };
            foreach (UIElement x in new UIElement[] { msg, attach, thumbs, pc, note, send }) s.Children.Add(x);

            Action ready = () => send.IsEnabled = msg.Text.Trim().Length > 0 || pics.Count > 0;
            msg.TextChanged += (o, e) => ready();
            Action draw = null;
            draw = () =>
            {
                thumbs.Children.Clear();
                foreach (string f in pics)
                {
                    string file = f;
                    Grid g = new Grid { Width = 64, Height = 64, Margin = new Thickness(0, 0, 8, 0) };
                    g.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = new ImageBrush(seen[file]) { Stretch = Stretch.UniformToFill } });
                    Btn x = new Btn { Style = plain, Content = "×", Width = 22, Height = 22, Padding = new Thickness(0), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 3, 0), ToolTip = L.T("Quitar imagen") };
                    x.SetResourceReference(Control.BackgroundProperty, "Card");
                    AutomationProperties.SetName(x, L.T("Quitar imagen"));
                    x.Click += (o, e) => { pics.Remove(file); draw(); };
                    g.Children.Add(x);
                    thumbs.Children.Add(g);
                }
                thumbs.Visibility = pics.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                attach.IsEnabled = pics.Count < Issue.MaxImages;
                ready();
            };
            attach.Click += (o, e) =>
            {
                Microsoft.Win32.OpenFileDialog d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = L.T("Imágenes") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp" };
                if (d.ShowDialog(w) != true) return;
                foreach (string f in d.FileNames)
                {
                    if (pics.Contains(f)) continue;
                    if (pics.Count == Issue.MaxImages) { Say("Se admiten hasta 4 imágenes.", false); break; }
                    try { seen[f] = Issue.Thumb(f); pics.Add(f); }
                    catch (Exception) { Say("No se pudo abrir la imagen: " + Path.GetFileName(f), false); }
                }
                draw();
            };
            send.Click += async (o, e) =>
            {
                send.IsEnabled = false;
                try
                {
                    List<string> saved = new List<string>();
                    if (pics.Count > 0)
                    {
                        List<string> src = pics.ToList();
                        string folder = Path.Combine(dir, "reportes", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                        saved = await Task.Run(() => Issue.Save(src, folder));
                    }
                    StretchSession.Open(Issue.Url(msg.Text, question, p == null ? null : L.T(p.Name), checks, pc.IsChecked == true ? PcData() : null, saved.Count));
                    if (saved.Count > 0) Issue.Reveal(saved[0]);
                    Reply(Line("Se abrió GitHub con el reporte escrito. " + (saved.Count > 0 ? "Arrastra las imágenes de la carpeta abierta al cuadro del reporte y pulsa Submit new issue." : "Pulsa Submit new issue.") + " Hace falta una cuenta de GitHub."));
                }
                catch (Exception ex) { Say("No se pudo preparar el reporte: " + ex.Message, false); }
                ready();
            };
            Reply(s);
            msg.Focus();
        }

        // What the scan, Display and the app already know; nothing personal.
        string PcData()
        {
            Hw h = hw ?? quick ?? new Hw();
            string brand = null;
            try { brand = Display.Brand(); } catch (Exception) { }
            return Support() + "\r\nGPUs: " + (h.GpuCount > 0 ? h.GpuCount.ToString() : "?") + "\r\nGame: " + game.Name + "\r\nDisplay GPU: " + (brand ?? "?");
        }

        // Only what the app already reads: the scan, the tweak states, the logs and the saved measurements.
        List<Check> Diagnose(Problem p, HelpData d)
        {
            List<Check> r = new List<Check>();
            Hw h = d.Hw;
            List<Check> review = h == null ? new List<Check>() : Review.For(h, d.Fortnite);
            Action<Func<Check, bool>> take = f => r.AddRange(review.Where(f));
            Action<string, Kind, string> tip = (title, k, text) => r.Add(new Check { Kind = k, Title = title, Text = text });
            Func<string, string, string, string, Check> go = (section, button, title, text) => new Check { Kind = Kind.Gratis, Fix = () => Open(section), Button = button, Title = title, Text = text };
            Action scan = () => { if (h == null) r.Add(go("Mi PC", "Abrir Mi PC", "PC sin escanear", "Con el escaneo también se revisan la RAM, el driver y el disco.")); };
            // any: offered even if the scan does not recommend it for this PC.
            Action<string, bool> tweak = (id, any) =>
            {
                Row x = d.Rows.FirstOrDefault(y => y.T.Id == id);
                if (x == null || !x.T.Available || x.T.Level == Level.Riesgo) return;
                bool on = x.State == State.On;
                if (!on && !any && !Advisor.For(x.T, h ?? d.Quick ?? new Hw()).Yes) return;
                r.Add(new Check { Kind = on ? Kind.Bien : Kind.Gratis, Title = x.T.Name, Text = x.T.Desc, Fix = on ? null : (Action)(() => { Task t = Bulk(new List<Row> { x }, true); }), Button = on ? null : "Activar" });
            };
            string pre = d.Game == Game.Valorant ? "val" : d.Game == Game.Cs2 ? "cs2" : "fn";
            Func<Check, bool> screen = c => c.Title.StartsWith("La pantalla usa") || c.Title.StartsWith("Pantalla a ");
            bool stretched = d.Last != null && d.Native != null && d.Last.Aspect < d.Native.Aspect - 0.01;

            switch (p.Id)
            {
                case "fps":
                    scan();
                    take(c => !screen(c) && !c.Title.StartsWith("Conexión") && !c.Title.Contains("torneos") && !c.Title.StartsWith("Un monitor"));
                    foreach (string id in new[] { "pw-plan", "gfx-gamemode", "gfx-dvr", "gfx-history", "nv-shader", "fn-render", "nv-power", "val-power", "cs2-power" }) tweak(id, false);
                    List<Match> last = d.Played.Take(5).ToList();
                    if (last.Count == 0) r.Add(go("Mis FPS", "Abrir Mis FPS", "Sin partidas registradas", "Mis FPS mide cada partida desde los registros del juego."));
                    else
                    {
                        int med = (int)last.Average(m => (double)m.Median), low = last.Min(m => m.Low);
                        tip("Últimas partidas: " + med + " FPS de media", low < med / 2 ? Kind.Gratis : Kind.Bien, "En los peores momentos bajan a " + low + " FPS.");
                    }
                    double free = Display.FreeRamGb();
                    if (free < 2) r.Add(go("Jugar", "Abrir Jugar", "Memoria libre: " + free.ToString("0.0") + " GB", "Cerrar programas antes de jugar libera memoria. Jugar muestra los que más usan."));
                    else tip("Memoria libre: " + free.ToString("0.0") + " GB", Kind.Bien, "Suficiente para jugar.");
                    tip("Tirones al empezar la partida", Kind.Gratis, "Suelen ser los sombreadores compilándose y desaparecen solos tras unos minutos.");
                    break;
                case "delay":
                    scan();
                    take(screen);
                    foreach (string id in new[] { "fn-reflex", "fn-vsync", "val-vsync", "cs2-vsync", pre + "-low", "gfx-windowed", "gfx-dvr" }) tweak(id, false);
                    tip("Gráfica al 100 %", Kind.Gratis, "Con la gráfica al límite el retraso sube. Un límite de FPS algo por debajo del máximo lo evita.");
                    break;
                case "network":
                    int signal = 0, channel = 0;
                    bool wifi = false;
                    try { wifi = Wifi.Info(out signal, out channel); } catch (Exception) { }
                    r.AddRange(Wifi.Advice(wifi, signal, channel, NetTest.Busy()));
                    NetResult n = NetTest.Load(dir);
                    Check net = go("Conexión", n == null ? "Abrir Conexión" : "Medir otra vez", "Conexión sin medir", "La prueba de Conexión mide el ping y las pérdidas hasta el router, internet y el servidor.");
                    if (n != null)
                    {
                        net.Title = "Última medición: " + n.Date.Substring(0, 16);
                        net.Text = d.Game == Game.Fortnite ? NetTest.Verdict(n) : NetTest.Home(n);
                        if (net.Text == "Conexión estable.") net.Kind = Kind.Bien;
                    }
                    r.Add(net);
                    foreach (string id in new[] { "net-p2p", "net-bg", "pw-wifi", "net-roam" }) tweak(id, false);
                    tip("Región del servidor", Kind.Gratis, "En los ajustes del juego conviene elegir la región más cercana.");
                    break;
                case "mouse":
                    tweak("in-accel", true);
                    tweak("fn-mouse", true);
                    tweak("pw-usb", false);
                    if (stretched) tip("Resolución estirada", Kind.Gratis, "Con stretched los giros horizontales parecen más rápidos. Conviene quedarse siempre con la misma resolución.");
                    tip("DPI y sensibilidad", Kind.Gratis, "La sensibilidad real es los DPI del ratón por la del juego. Si cambian los DPI, hay que ajustar la del juego.");
                    tip("Raw input", Kind.Gratis, "Valorant, CS2 y Fortnite leen el ratón directamente: la velocidad del puntero de Windows solo cambia el escritorio.");
                    break;
                case "launch":
                    if (!d.Game.Installed()) r.Add(go("Jugar", "Abrir Jugar", d.Game.Name + " no está instalado o no se encontró", "Se puede elegir su archivo a mano."));
                    else tip(d.Game.Name + " instalado", Kind.Bien, d.Game.Exe);
                    scan();
                    take(c => c.Title.IndexOf("driver", StringComparison.OrdinalIgnoreCase) >= 0 || c.Title.Contains("libres en el disco"));
                    Row hvci = d.Rows.FirstOrDefault(y => y.T.Id == "pw-hvci");
                    if (hvci != null && hvci.State == State.On)
                        r.Add(new Check { Kind = Kind.Gratis, Title = "Integridad de memoria apagada", Text = "Algunos anticheat la exigen para abrir el juego. Encenderla requiere reiniciar el PC.", Fix = engine.Mine(hvci.T) ? (Action)(() => { Task t = Bulk(new List<Row> { hvci }, false); }) : null, Url = "windowsdefender://coreisolation", Button = engine.Mine(hvci.T) ? "Deshacer" : null });
                    if (d.Game == Game.Valorant && h != null && h.SecureBoot != null && h.Tpm2 != null)
                        tip(h.SecureBoot == true && h.Tpm2 == true ? "TPM 2.0 y Secure Boot activos" : "Vanguard: falta TPM 2.0 o Secure Boot", h.SecureBoot == true && h.Tpm2 == true ? Kind.Bien : Kind.Gratis, "En Windows 11, Vanguard los exige para abrir Valorant. Se activan en la BIOS.");
                    tip("Reparar los archivos del juego", Kind.Gratis, "Desde su launcher: Epic Games (Administrar, Verificar), Riot Client (Ajustes, Reparar) o Steam (Propiedades, Verificar integridad).");
                    tip("Programas superpuestos", Kind.Gratis, "Cerrar superposiciones como MSI Afterburner o RivaTuner antes de abrir el juego descarta que lo cierren.");
                    break;
                case "stretched":
                    Hw g = h ?? d.Quick;
                    string gpu = g == null || g.Gpu == null ? null : g.Nvidia ? "nvidia" : g.Gpu.Contains("Radeon") ? "amd" : null;
                    if (g == null || g.Gpu == null) try { if (Vivid.GetVib() != null) gpu = "nvidia"; } catch (Exception) { }
                    if (!stretched) r.Add(go("Stretched Resolution", "Abrir Stretched Resolution", "Sin resolución estirada elegida", "Se elige en Stretched Resolution y se aplica al pulsar Jugar."));
                    else tip("Resolución elegida: " + d.Last.Name, Kind.Bien, "Se aplica al pulsar Jugar y se restaura al cerrar el juego.");
                    if (d.Game == Game.Valorant)
                    {
                        int mon = 0;
                        try { mon = Monitors.On().Count; } catch (InvalidOperationException) { }
                        r.AddRange(Valorant.Checks(Valorant.Settings(), mon, gpu, dir));
                    }
                    else if (d.Game == Game.Cs2) r.AddRange(Cs2.Checks(Cs2.Video(), Cs2.LaunchOptions(), gpu, dir));
                    else Valorant.Scale(r, gpu, dir);
                    break;
                case "colors":
                    int? vib = null;
                    bool ramp = false;
                    try { vib = Vivid.GetVib(); ramp = Vivid.GetRamp() != null; } catch (Exception) { }
                    Check col = go("Colores", "Abrir Colores", "Sin perfil de color en " + d.Game.Name, "En Colores se elige un perfil, que se aplica al abrir el juego y se quita al cerrarlo.");
                    if (d.Color != null && d.Color.Profile != Vivid.None) { col.Kind = Kind.Bien; col.Title = "Perfil de color: " + Vivid.All.First(x => x.Id == d.Color.Profile).Name; col.Text = "Se aplica al abrir el juego y se quita al cerrarlo."; }
                    r.Add(col);
                    if (vib == null) tip("Intensidad de color no disponible", Kind.Gratis, "La intensidad necesita una gráfica NVIDIA.");
                    else tip("Intensidad de color: " + vib + " %", Kind.Bien, "Valor actual de la intensidad digital de NVIDIA.");
                    if (!ramp) tip("Brillo, contraste y gamma bloqueados", Kind.Gratis, "Windows no permite cambiar brillo, contraste y gamma en esta pantalla. Pasa con HDR o con el monitor desactivado en el Administrador de dispositivos.");
                    else tip("Brillo, contraste y gamma disponibles", Kind.Bien, "Se ajustan en Colores.");
                    tweak("gfx-autohdr", false);
                    break;
            }
            return r.OrderBy(c => (int)c.Kind).ToList();
        }
    }

    // A report as a new GitHub issue opened in the browser: the URL carries the text, the images are dragged in by hand.
    public static class Issue
    {
        public const string New = "https://github.com/benjarn114/tweakeroo/issues/new";
        public const int Max = 7500, MaxImages = 4, Side = 1600;
        public static Action<string> Reveal = file => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = false });

        // The first n characters, without splitting an emoji in two.
        static string Head(string s, int n)
        {
            if (s.Length <= n) return s;
            if (n > 0 && char.IsHighSurrogate(s[n - 1])) n--;
            return s.Substring(0, n);
        }

        // Too long: the checks go first, then the end of the message, each marked "(recortado)".
        public static string Url(string message, string question, string problem, List<Check> checks, string pc, int images)
        {
            message = (message ?? "").Trim();
            question = Head((question ?? "").Trim(), 500); // shortcut: a longer question is cut here so the rest always fits
            problem = problem ?? L.T("Problema sin identificar");
            string mark = L.T("(recortado)");
            string first = System.Text.RegularExpressions.Regex.Replace(message, @"\s+", " ");
            string title = "[App] " + problem + (first.Length == 0 ? "" : ": " + (first.Length > 60 ? Head(first, 60).TrimEnd() + "…" : first));
            List<string> lines = (checks ?? new List<Check>()).Select(c => "- " + (c.Ok ? "OK" : "Pending") + ": " + L.T(c.Title)).ToList();
            Func<string, int, string> build = (m, keep) =>
            {
                StringBuilder b = new StringBuilder();
                b.Append("### Message\n").Append(m.Length == 0 ? "-" : m);
                b.Append("\n\n### Last question\n").Append(question.Length == 0 ? "-" : question);
                b.Append("\n\n### Matched problem\n").Append(problem);
                if (lines.Count > 0)
                {
                    b.Append("\n\n### Checks shown\n").Append(string.Join("\n", lines.Take(keep)));
                    if (keep < lines.Count) b.Append(keep > 0 ? "\n" : "").Append("- ").Append(mark);
                }
                if (pc != null) b.Append("\n\n### PC data\n```\n").Append(pc.Replace("\r\n", "\n")).Append("\n```");
                if (images > 0) b.Append("\n\n### Images\n").Append(images).Append(images == 1 ? " image attached below" : " images attached below");
                return New + "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(b.ToString()) + "&labels=from-app";
            };
            // A message longer than the whole URL never fits: cut it first so the escaping stays small.
            if (message.Length > Max) message = Head(message, Max) + " " + mark;
            int k = lines.Count;
            string url = build(message, k);
            while (url.Length > Max && k > 0) url = build(message, --k);
            if (url.Length <= Max) return url;
            int lo = 0, hi = message.Length - 1; // the longest start of the message that fits
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (build(Head(message, mid).TrimEnd() + " " + mark, 0).Length <= Max) lo = mid; else hi = mid - 1;
            }
            return build(Head(message, lo).TrimEnd() + " " + mark, 0);
        }

        public static ImageSource Thumb(string file)
        {
            BitmapImage b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(file);
            b.DecodePixelWidth = 128;
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.EndInit();
            b.Freeze();
            return b;
        }

        // At most Side pixels on the long side (never enlarged), JPEG at 80, or 65 if it still weighs over 1 MB.
        public static byte[] Jpeg(BitmapSource src)
        {
            int big = Math.Max(src.PixelWidth, src.PixelHeight);
            BitmapSource b = big > Side ? new TransformedBitmap(src, new ScaleTransform(Side / (double)big, Side / (double)big)) : src;
            // shortcut: transparent parts come out black; screenshots have none
            if (b.Format != PixelFormats.Bgr24) b = new FormatConvertedBitmap(b, PixelFormats.Bgr24, null, 0);
            byte[] r = null;
            foreach (int q in new[] { 80, 65 })
            {
                JpegBitmapEncoder e = new JpegBitmapEncoder { QualityLevel = q };
                e.Frames.Add(BitmapFrame.Create(b));
                using (MemoryStream m = new MemoryStream()) { e.Save(m); r = m.ToArray(); }
                if (r.Length <= 1024 * 1024) break;
            }
            return r;
        }

        // Compressed copies as imagen-1.jpg, imagen-2.jpg… in folder; the originals are not touched.
        public static List<string> Save(List<string> files, string folder)
        {
            Directory.CreateDirectory(folder);
            List<string> r = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                BitmapSource src;
                using (FileStream f = File.OpenRead(files[i])) src = BitmapDecoder.Create(f, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                string to = Path.Combine(folder, "imagen-" + (i + 1) + ".jpg");
                File.WriteAllBytes(to, Jpeg(src));
                r.Add(to);
            }
            return r;
        }
    }
}
