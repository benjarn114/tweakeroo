#if SHOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tablero
{
    // Walks the UI with real clicks on fake tweaks in a throwaway key.
    static class ClickTest
    {
        const string Root = @"HKCU\Software\Tablero_Clics";
        class Bad : RegOp { public Bad() : base(Root, "mal", "d:1", None) { } public override void Write(string k, string v) { throw new InvalidOperationException("fallo a propósito"); } }
        public static void Prepare(ref string dir)
        {
            dir = Path.Combine(Path.GetTempPath(), "tablero-clics");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "punto-de-restauracion.txt"), "prueba");
            // Starts in Simple so the palette checks see the colors per game; the themes are tried later.
            Prefs start = Prefs.Default(); start.Theme = "simple"; File.WriteAllText(Path.Combine(dir, "ajustes.json"), start.Json());
            RegOp.WriteValue(Root, "a", "d:5");
            RegOp.WriteValue(Root, "ya", "d:1");
            Fakes(dir);
            Ui.Source = () => new List<Tweak> { M("a", Level.Seguro, new RegOp(Root, "a", "d:1", "d:0")), M("b", Level.Avanzado, new RegOp(Root, "b", "d:1", Op.None)), M("ya", Level.Seguro, new RegOp(Root, "ya", "d:1", "d:0")), M("r", Level.Riesgo, new RegOp(Root, "r", "d:1", Op.None)), M("mal", Level.Seguro, new Bad()) };
        }
        static Tweak M(string id, Level l, Op o) { return new Tweak { Id = id, Cat = "Prueba", Name = id, Desc = id, Effect = "Bajo", Restart = "", Level = l, Ops = new List<Op> { o } }; }
        static string V(string n) { return RegOp.ReadValue(Root, n); }
        static void Is(bool ok, string what) { if (!ok) throw new Exception("FALLA: " + what); }
        static IEnumerable<Button> Buttons(DependencyObject o)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(o); i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(o, i);
                if (c is Button) yield return (Button)c;
                foreach (Button b in Buttons(c)) yield return b;
            }
        }
        static async Task Click(Window w, Button b)
        {
            ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(50);
            while (!((Button)w.FindName("AllOn")).IsEnabled) await Task.Delay(50);
            await Task.Delay(100);
        }
        static Ui live;
        static void Go(Window w, string name) { live.Open(name); Is(((Circuit)((ListBox)w.FindName("Circuits")).SelectedItem).Name == name, "abrir " + name); }
        static Button RowBtn(Window w, string id) { return Buttons((ListBox)w.FindName("Rows")).First(b => ((Row)b.DataContext).T.Id == id); }
        static Button SaveBtn(Window w, string kind, string cmd) { return Buttons((ListBox)w.FindName("Saves")).First(b => ((SaveRow)b.DataContext).S.Kind == kind && (string)b.CommandParameter == cmd); }
        static void Key(ListBox l, System.Windows.Input.Key k) { l.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(l), 0, k) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent }); }
        static void Press(Button b) { ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke(); w0.Dispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.Background); }
        static Window w0;
        static Res screen = new Res { W = 2560, H = 1440, Hz = 200 };
        static bool running, steady, vrunning;
        static int vib = 50; static ushort[] ramp = Vivid.Ramp(Vivid.All[0].Pick);
        static List<string> mons = new List<string> { "A", "B" };
        static string vlog, undo, vini;
        static int boosted;
        static int opened, tries, saved, reverts;
        const string ini = "[/Script/FortniteGame.FortGameUserSettings]\r\nFullscreenMode=0\r\nResolutionSizeX=2560\r\nResolutionSizeY=1440\r\n";
        static void Fakes(string dir)
        {
            IniOp.File = Path.Combine(dir, "g.ini"); File.WriteAllText(IniOp.File, ini);
            IniOp.GameRunning = () => running;
            Res native = screen;
            Display.Modes = () => new List<Res> { native, new Res { W = 1440, H = 1080, Hz = 60 }, new Res { W = 1440, H = 1080, Hz = 200 }, new Res { W = 1720, H = 1080, Hz = 200 }, new Res { W = 1920, H = 1080, Hz = 200 } };
            Display.Current = () => screen; Display.Saved = () => native; Display.Set = r => screen = r; Display.Reset = () => screen = native;
            StretchSession.Open = u => opened++;
            Display.Accepts = r => true;
            Custom.Available = () => true; Custom.Try = r => { tries++; screen = r; }; Custom.Save = () => saved++; Custom.Revert = () => { reverts++; screen = native; };
            FpsLog.Folder = Path.Combine(dir, "sin-registros");
            Game.Valorant.Running = () => vrunning;
            Game.Fortnite.Installed = () => true; Game.Valorant.Installed = () => true;
            Boost.All = new List<Boost> { new Boost { Id = "x", Name = "x", Desc = "x", On = () => { boosted++; return "viejo"; }, Off = o => { boosted--; undo = o; } } };
            Drivers.Download = u => "{ \"Version\" : \"999.10\", \"ReleaseDateTime\" : \"Thu Nov 12, 2026\", \"DetailsURL\" : \"https://www.nvidia.com/en-us/drivers/details/1/\" }";
            Valorant.Saved = Path.Combine(dir, "val"); Directory.CreateDirectory(Valorant.Logs); vlog = Path.Combine(Valorant.Logs, "ShooterGame.log"); File.WriteAllText(vlog, Map(DateTime.UtcNow.AddHours(-1), "Triad", true));
            vini = Path.Combine(Valorant.Saved, @"Config\u1\WindowsClient\GameUserSettings.ini"); Directory.CreateDirectory(Path.GetDirectoryName(vini)); File.WriteAllText(vini, "[/Script/ShooterGame.ShooterGameUserSettings]\r\nFullscreenMode=0\r\nbShouldLetterbox=False\r\n");
            NvScale.Get = () => 2; NvScale.Set = v => { throw new InvalidOperationException("NvScale.Set real en una prueba"); };
            Monitors.On = () => mons.ToList(); Monitors.Set = (id, on) => { if (on) mons.Add(id); else mons.Remove(id); };
            Vivid.GetVib = () => vib; Vivid.SetVib = v => vib = v; Vivid.GetRamp = () => ramp; Vivid.SetRamp = r => { ramp = r; return true; };
            Binds.Folder = Path.Combine(dir, "sin-teclas");
            NetTest.Ping = (h, ms) => Task.FromResult(h.Contains("-br.") ? 41 : h == "1.1.1.1" ? 20 : h == "10.0.0.1" ? 2 : -1); NetTest.Router = () => "10.0.0.1"; Ui.NetTries = 10; Ui.NetPause = 5;
            Wifi.Steady = on => { steady = on; return true; };
            // CS2: a fake Steam with the player's video file (other monitor) and -vulkan.
            string steam = Path.Combine(dir, "steam"), cfg = Path.Combine(steam, @"userdata\1\730\local\cfg");
            Cs2.Steam = () => steam; Directory.CreateDirectory(cfg); Directory.CreateDirectory(Path.Combine(steam, @"userdata\1\config"));
            File.WriteAllText(Path.Combine(steam, "steam.exe"), "");
            cvid = Path.Combine(cfg, "cs2_video.txt"); File.WriteAllText(cvid, cvid0);
            File.WriteAllText(Path.Combine(steam, @"userdata\1\config\localconfig.vdf"), "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"730\" { \"LaunchOptions\" \"-vulkan\" } } } } } }");
            Game.Cs2.Running = () => crunning; Game.Cs2.Installed = () => cinst;
        }
        static bool crunning, cinst = true;
        static string cvid;
        const string cvid0 = "\"video.cfg\"\r\n{\r\n\t\"setting.defaultres\"\t\t\"2560\"\r\n\t\"setting.defaultresheight\"\t\t\"1440\"\r\n\t\"setting.aspectratiomode\"\t\t\"1\"\r\n\t\"setting.fullscreen\"\t\t\"1\"\r\n\t\"setting.monitor_index\"\t\t\"1\"\r\n}\r\n";
        static string Map(DateTime utc, string name, bool inMap)
        {
            string b = inMap ? "TRUE" : "FALSE";
            return utc.ToString("[yyyy.MM.dd-HH.mm.ss:fff]", System.Globalization.CultureInfo.InvariantCulture) + "[  1]LogMapLoadModel: Update: [Map Name: " + name + " | Changed: TRUE] [Local World: " + b + " | Changed: TRUE] [Match Setup: TRUE | Changed: TRUE] [Map Ready: " + b + " | Changed: TRUE]\n";
        }
        public static async Task<string> Run(Window w, Ui ui)
        {
            w0 = w; live = ui;
            try
            {
                ListBox nav = (ListBox)w.FindName("Circuits");
                Is(string.Join(",", nav.Items.Cast<Circuit>().Select(c => c.Name)) == "Jugar,Stretched Resolution,Colores,Al jugar,Mi PC,Mis FPS,Conexión,Macros,Tweaks,Mejoras", "menu corto al abrir");
                await Task.Delay(400);
                Is(((UIElement)w.FindName("SkelPage")).Visibility != Visibility.Visible && ((UIElement)w.FindName("SkelSaves")).Visibility != Visibility.Visible, "los skeletons se van");
                // On some PCs WPF freezes the shine brush once used; showing the skeletons again must not touch it.
                ((System.Windows.Media.Brush)w.Resources["Shimmer"]).Freeze();
                ui.ShowSkeletons("SkelPage", "SkelNav", "SkelSaves");
                Is(!((System.Windows.Media.Brush)w.Resources["Shimmer"]).IsFrozen || !Fx.On, "brillo nuevo aunque el anterior estuviera congelado");
                await ui.HideSkeletons();
                // a disabled list must not get the Windows white background
                foreach (string n in new[] { "Saves", "Rows", "Resolutions" }) Is(((ListBox)w.FindName(n)).Template == w.Resources["PlainList"], "plantilla de " + n);
                Go(w, "Mejoras"); Key(nav, System.Windows.Input.Key.Right); Is(nav.Items.Count == 10, "Mejoras ya no se despliega");
                Go(w, "Tweaks"); Key(nav, System.Windows.Input.Key.Right); Is(nav.Items.Count > 10, "Tweaks se despliega");
                Key(nav, System.Windows.Input.Key.Left); await Task.Delay(500); Is(nav.Items.Count == 10, "Tweaks se pliega");
                Key(nav, System.Windows.Input.Key.Enter); Key(nav, System.Windows.Input.Key.Enter); Key(nav, System.Windows.Input.Key.Enter); await Task.Delay(500);
                // Off, On, the line, All and the one category
                Is(nav.Items.Count == 10 + 5 && nav.Items.Cast<Circuit>().Distinct().Count() == nav.Items.Count, "abrir y cerrar rapido no duplica");
                Func<string> order = () => string.Join(",", nav.Items.Cast<Circuit>().Skip(8).Select(c => c.Name));
                Is(order() == "Tweaks,Apagados,Activados,,Todos,Prueba,Mejoras", "orden bajo Tweaks: " + order());
                // Off: a switched row stays until leaving, the counts move at once
                Circuit offC = nav.Items.Cast<Circuit>().First(c => c.Name == "Apagados"), onC = nav.Items.Cast<Circuit>().First(c => c.Name == "Activados");
                ListBox rowsL = (ListBox)w.FindName("Rows");
                Is(offC.Count == "4" && onC.Count == "1", "cuentas de Tweaks: " + offC.Count + " y " + onC.Count);
                Go(w, "Apagados"); await Task.Delay(700);
                Is(rowsL.Items.Count == 4 && rowsL.Items.Groups != null && rowsL.Items.Groups.Count == 1, "Apagados agrupado por categoría");
                await Click(w, RowBtn(w, "a")); Is(V("a") == "d:1" && offC.Count == "3" && onC.Count == "2", "encender en Apagados: " + offC.Count + " y " + onC.Count);
                Is(rowsL.Items.Count == 4 && RowBtn(w, "a") != null, "lo encendido sigue a la vista");
                Go(w, "Activados"); await Task.Delay(300); Is(rowsL.Items.Count == 2, "Activados lo trae al volver");
                await Click(w, RowBtn(w, "a")); Is(V("a") == "d:5" && offC.Count == "4" && onC.Count == "1" && rowsL.Items.Count == 2, "apagar en Activados tampoco salta");
                Go(w, "Apagados"); await Task.Delay(300); Is(rowsL.Items.Count == 4, "Apagados al volver");
                Go(w, "Todos");
                await Task.Delay(700);
                await Click(w, RowBtn(w, "a")); Is(V("a") == "d:1", "prender uno");
                await Click(w, RowBtn(w, "a")); Is(V("a") == "d:5", "apagar uno devuelve 5");
                await Click(w, RowBtn(w, "mal")); Is(((Row)RowBtn(w, "mal").DataContext).HasError, "el error se ve en su fila");
                await Click(w, (Button)w.FindName("AllOn")); Is(V("a") == "d:1" && V("b") == "d:1" && V("r") == Op.None, "prender todo deja fuera Riesgo");
                string st = ((TextBlock)w.FindName("Status")).Text; Is(st.StartsWith("2 activados. 1 fallaron"), "mensaje: " + st);
                await Click(w, (Button)w.FindName("AllOff")); Is(V("a") == "d:5" && V("b") == Op.None && V("ya") == "d:1", "apagar todo respeta lo previo");
                ((TextBox)w.FindName("SaveName")).Text = "mío";
                await Click(w, (Button)w.FindName("SaveNow")); Is(((ListBox)w.FindName("Saves")).Items.Cast<SaveRow>().Any(s => s.Name == "mío"), "guardar");
                await Click(w, (Button)w.FindName("AllOn"));
                await Click(w, SaveBtn(w, "original", "restore")); Is(V("a") == "d:1", "un clic no restaura");
                await Click(w, SaveBtn(w, "original", "restore")); Is(V("a") == "d:5" && V("b") == Op.None, "dos clics restauran");
                await Click(w, SaveBtn(w, "manual", "delete")); await Click(w, SaveBtn(w, "manual", "delete"));
                Is(!((ListBox)w.FindName("Saves")).Items.Cast<SaveRow>().Any(s => s.Name == "mío"), "borrar");
                Is(((TextBlock)w.FindName("PaletteName")).Text == "Color: Glaciar", "Glaciar por defecto");
                // scanner: enables the safe ones and leaves the advanced and risky ones
                await Click(w, (Button)w.FindName("Scan"));
                Is(nav.Items.Cast<Circuit>().Any(c => c.Name == "Recomendados"), "aparece Recomendados");
                Is(order() == "Tweaks,Apagados,Activados,,Recomendados,Todos,Prueba,Mejoras", "Recomendados bajo Tweaks: " + order());
                Is(Scanner.Load(Path.Combine(Path.GetTempPath(), "tablero-clics")) != null, "el escaneo queda guardado");
                await Click(w, (Button)w.FindName("Scan"));
                Is(V("a") == "d:1" && V("b") == Op.None && V("r") == Op.None, "optimizar activa solo lo recomendado");
                ((ListBox)w.FindName("Palettes")).SelectedIndex = 1; await Task.Delay(900);
                Is(((TextBlock)w.FindName("PaletteName")).Text == "Color: Medianoche", "cambia la paleta");
                {
                    // every swatch fits in the column: the list cannot scroll, a cut one is unreachable
                    ListBox pl = (ListBox)w.FindName("Palettes");
                    FrameworkElement last = (FrameworkElement)pl.ItemContainerGenerator.ContainerFromIndex(pl.Items.Count - 1);
                    Is(VisualTreeHelper.GetOffset(last).X + last.ActualWidth <= pl.ActualWidth + 0.5, "el último color entra entero");
                }
                // stretched with a fake display, game and launcher
                Button play = (Button)w.FindName("Play"), back = (Button)w.FindName("Back");
                Go(w, "Jugar"); await Task.Delay(300);
                ListBox rl = (ListBox)w.FindName("Resolutions"), rol = (ListBox)w.FindName("ResOpenList"); Drop ro = (Drop)w.FindName("ResOpen"), rd = (Drop)w.FindName("ResDrop");
                Func<string> names = () => string.Join(",", rl.Items.Cast<Res>().Select(x => x.Name));
                Is(rl.Items.Count == 3 && rl.SelectedIndex == 0 && (string)play.Content == "Jugar" && ((Res)ro.Content).Name == "2560 × 1440" && ((ResRow)ro.Content).Detail == "1,78:1 · 200 Hz", "empieza en la del monitor");
                Is(names() == "2560 × 1440,1720 × 1080,1440 × 1080" && ((Res)rl.Items[1]).Note == "1,59:1" && ((Res)rl.Items[0]).Note == "Normal" && rl.Items.Cast<ResRow>().All(x => !x.Best), "de menos a mas estirada: " + names());
                Press(ro); await Task.Delay(300); Is(ro.IsOpen && rol.Items.Count == 3, "Jugar abre su desplegable");
                rol.SelectedIndex = 1; ro.Close(); Is(rl.SelectedIndex == 1 && ((Res)ro.Content).Name == "1720 × 1080" && ro.Content == rd.Content && (string)play.Content == "Jugar", "elegida en Jugar llega a Stretched Resolution: " + ro.Content);
                Press(play);
                Is(screen.Name == "1720 × 1080" && ((string)play.Content).StartsWith("Conservar") && opened == 0, "pide confirmar antes de abrir");
                Press(back); Is(screen.Name == "2560 × 1440" && File.ReadAllText(IniOp.File) == ini, "volver repone pantalla e ini");
                Press(play); Press(play); Is(opened == 1 && screen.W == 1720, "confirmar abre el juego");
                running = true; await Task.Delay(3000); Is((string)play.Content == "Jugando" && !play.IsEnabled, "detecta el juego");
                Press(back); Is(screen.W == 2560 && File.ReadAllText(IniOp.File) != ini, "a mitad de partida solo vuelve la pantalla");
                running = false; await Task.Delay(3000); Is(File.ReadAllText(IniOp.File) == ini && play.IsEnabled && back.Visibility != Visibility.Visible, "al cerrar el juego vuelve el ini");
                Press(play); Is(opened == 2, "la segunda vez no pregunta");
                running = true; await Task.Delay(3000); running = false; await Task.Delay(3000); Is(screen.W == 2560 && File.ReadAllText(IniOp.File) == ini, "cerrar el juego lo devuelve todo");
                ((ListBox)w.FindName("Resolutions")).SelectedIndex = 0; Press(play); Is(opened == 3 && screen.W == 2560, "la normal abre sin preguntar ni tocar la pantalla");
                running = true; await Task.Delay(3000); running = false; await Task.Delay(3000); Is(File.ReadAllText(IniOp.File) == ini, "la normal tambien repone el ini");
                Border shine = (Border)((Control)play).Template.FindName("shine", play);
                Is(!Fx.On || (shine.Background != null && shine.IsVisible), "el boton principal brilla");
                // custom: on trial, and only saved when confirmed
                TextBox cw = (TextBox)w.FindName("CustomW"), ch = (TextBox)w.FindName("CustomH"); Button cg = (Button)w.FindName("CustomGo");
                cw.Text = "9999"; ch.Text = "1080"; Press(cg); Is(((TextBlock)w.FindName("CustomHint")).Text.StartsWith("Máximo: la resolución"), "personalizada demasiado grande");
                Func<bool> red = () => ((TextBlock)w.FindName("CustomHint")).Foreground.ToString() == "#FFE5484D"; Is(red(), "el error sale en rojo");
                cw.Text = "999"; Is(!red() && !((TextBlock)w.FindName("CustomHint")).Text.StartsWith("Máximo"), "al escribir vuelve la explicacion");
                cw.Text = "1500"; ch.Text = "1000"; Press(cg); Is(((Res)ro.Content).Name == "1500 × 1000", "personalizada elegida");
                Press(play); Is(tries == 1 && saved == 0 && screen.W == 1500, "a prueba"); Press(back); Is(reverts == 1 && saved == 0 && screen.W == 2560, "si no convence se retira");
                ((ListBox)w.FindName("Limits")).SelectedIndex = 2;
                Press(play); Press(play); Is(saved == 1 && opened == 4 && File.ReadAllText(IniOp.File).Contains("FrameRateLimit=0.000000"), "confirmada se guarda y aplica el limite");
                running = true; await Task.Delay(3000); running = false; await Task.Delay(3000); Is(File.ReadAllText(IniOp.File) == ini && screen.W == 2560, "y todo vuelve");
                Go(w, "Mis FPS"); await Task.Delay(2500); Is(((ItemsControl)w.FindName("Estimates")).Items.Count >= 1, "estimacion a la vista");
                Is(((ItemsControl)w.FindName("Plan")).Items.Count >= 2, "lista de compras futuras");
                Go(w, "Mejoras"); Is(((ItemsControl)w.FindName("Checks")).Items.Count > 3, "la revision lista consejos"); Is(!Drivers.Known.Url.Contains("malo") && ((ItemsControl)w.FindName("Checks")).Items.Cast<Check>().Any(c => c.Title == "Driver de NVIDIA: hay una versión nueva" && c.Act == "Abrir web"), "avisa del driver");
                // macros: nothing without accepting, without a key or with a repeated key
                Go(w, "Macros"); await Task.Delay(700);
                TextBlock mst = (TextBlock)w.FindName("MacroState"); Panel mtop = (Panel)mst.Parent; int mIdx = mtop.Children.IndexOf(mst);
                TextBlock mfix = mIdx > 0 ? mtop.Children[mIdx - 1] as TextBlock : null;
                Is(mfix != null && mfix.IsVisible && mfix.Text == L.T("Las macros solo funcionan en mapas de Creativo, para entrenar ediciones. En partidas públicas, torneos y competitivo están desactivadas."), "macros: texto fijo de solo Creativo arriba");
                Is(mst.IsVisible && mst.Text == L.T("Apagadas") && mst.Foreground == w.TryFindResource("Ink2"), "macros: indicador apagadas justo debajo: " + mst.Text);
                FrameworkElement body = (FrameworkElement)w.FindName("MacroBody");
                CheckBox au = (CheckBox)w.FindName("MacroAuto"); Is(au.IsChecked == true && !((FrameworkElement)w.FindName("KeyEdit")).IsEnabled, "por defecto usa las teclas de Fortnite");
                au.IsChecked = false; au.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                ItemsControl mr = (ItemsControl)w.FindName("MacroRows"); mr.UpdateLayout();
                Is(!body.IsEnabled && Macros.Status == "Apagadas", "macros apagadas sin aceptar");
                CheckBox ac = (CheckBox)w.FindName("MacroAccept"); ac.IsChecked = true; ac.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Is(body.IsEnabled && Macros.Status == "Apagadas", "aceptar no enciende ninguna");
                MacroRow dbl = mr.Items.Cast<MacroRow>().First(x => x.Id == "doble"), ed = mr.Items.Cast<MacroRow>().First(x => x.Id == "edit");
                Func<MacroRow, Button> kb = x => Buttons(mr).First(b => b.DataContext == x);
                Press(kb(dbl)); Is(dbl.KeyText == "Pulsa una tecla…", "espera la tecla"); live.Captured(0x46); Is(!dbl.On && dbl.KeyText == "Elegir tecla", "no acepta la tecla de editar");
                Press(kb(dbl)); live.Captured(0x05); Is(dbl.On && dbl.KeyText == "Botón lateral 1", "macro con boton lateral");
                Press(kb(ed)); live.Captured(0x05); Is(!ed.On, "no repite tecla");
                MacroConfig mc = Macros.Load(Path.Combine(Path.GetTempPath(), "tablero-clics")); Is(mc.Accepted && mc.On.Single() == "doble" && mc.Keys["doble"] == 5, "macros guardadas");
                // right click and wheel reach the game key picker, as Windows sends them
                Button kr = (Button)w.FindName("KeyReset");
                Press(kr); await Task.Delay(250);
                w.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Right) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
                Is((string)kr.Content == "Clic derecho", "clic derecho como tecla: " + kr.Content);
                Press(kr); await Task.Delay(250);
                w.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
                Is((string)kr.Content == "Rueda abajo" && Macros.Load(Path.Combine(Path.GetTempPath(), "tablero-clics")).Reset == Macros.WheelDown, "rueda como tecla: " + kr.Content);
                Button ks = (Button)w.FindName("KeySelect");
                Press(ks); live.Captured(Macros.WheelUp); Is((string)ks.Content == "Clic izquierdo", "la rueda no sirve para seleccionar");
                Press(kb(ed)); live.Captured(Macros.WheelUp); Is(!ed.On, "la rueda no sirve para lanzar macros");
                ac.IsChecked = false; ac.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Is(Macros.Status == "Apagadas" && !body.IsEnabled, "sin aceptar se apagan");
                // connection: measures, says where the problem is and saves it
                Go(w, "Conexión"); await Task.Delay(400);
                Button nr = (Button)w.FindName("NetRun"); Press(nr); Is(!nr.IsEnabled, "midiendo"); for (int i = 0; i < 60 && !nr.IsEnabled; i++) await Task.Delay(100);
                List<NetTile> nt = ((ItemsControl)w.FindName("NetTiles")).Items.Cast<NetTile>().ToList();
                Is(nt[0].Main == "2 ms" && nt[1].Main == "20 ms" && nt[2].Main == "41 ms" && nt[2].Label.EndsWith("Brasil") && ((TextBlock)w.FindName("NetVerdict")).Text == "Conexión estable.", "medicion: " + string.Join(" ", nt.Select(x => x.Main)));
                Is(((ItemsControl)w.FindName("NetChecks")).Items.Count >= 2 && NetTest.Load(Path.Combine(Path.GetTempPath(), "tablero-clics")).Region == "br", "consejos y guardado");
                CheckBox ns = (CheckBox)w.FindName("NetSteady"); ns.IsChecked = true; ns.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Is(!steady, "sin Fortnite no toca el Wi-Fi");
                running = true; await Task.Delay(3500); Is(steady, "con Fortnite abierto, Wi-Fi estable"); running = false;
                body.IsEnabled = true; Is(((FrameworkElement)w.FindName("KeyEdit")).IsEnabled, "a mano se pueden cambiar"); body.IsEnabled = false;
                // speed
                Button sr = (Button)w.FindName("SpeedRun"); running = true; Press(sr); Is(sr.IsEnabled && ((TextBlock)w.FindName("Status")).Text.StartsWith("Cierra Fortnite"), "con Fortnite abierto no mide"); running = false;
                SpeedTest.Measure = (down, step) => { System.Threading.Thread.Sleep(300); return down ? 250 : 40; };
                Press(sr); for (int i = 0; i < 80 && !sr.IsEnabled; i++) await Task.Delay(100); await Task.Delay(200);
                List<NetTile> vt = ((ItemsControl)w.FindName("SpeedTiles")).Items.Cast<NetTile>().ToList(); Is(vt[0].Main == "250 Mbps" && vt[1].Main == "40 Mbps" && vt[2].Main == "20 ms", "velocidad: " + string.Join(" ", vt.Select(x => x.Main)));

                // valorant: changes everything, does not touch the display without permission, stretches when the map loads and restores it
                RoutedEventArgs ck = new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent);
                TextBlock pn = (TextBlock)w.FindName("PaletteName"); string fortPal = pn.Text;
                Go(w, "Jugar"); ((ListBox)w.FindName("Games")).SelectedItem = Game.Valorant; await Task.Delay(1400);
                Is(nav.Items.Cast<Circuit>().Last().Name == "Mejoras" && nav.Items.Cast<Circuit>().Count(c => c.Name == "Apagados") == 1, "Valorant: Mejoras al final: " + string.Join(",", nav.Items.Cast<Circuit>().Select(c => c.Name)));
                Is(pn.Text == "Color: Carmesí" && !nav.Items.Cast<Circuit>().Any(c => c.Name == "Macros") && ((UIElement)w.FindName("Limits")).Visibility != Visibility.Visible && Prefs.Now.Game == "valorant", "todo pasa a Valorant");
                Is((string)play.Content == "Jugar" && ((UIElement)w.FindName("ResBox")).Visibility != Visibility.Visible, "estirar viene apagado: " + play.Content);
                int was = opened; Press(play); Is(opened == was + 1 && screen.W == 2560 && mons.Count == 2, "abrir sin estirar no toca nada");
                vrunning = true; await Task.Delay(3200); Is((string)play.Content == "Jugando" && !live.SwitchGame(Game.Fortnite, true) && pn.Text == "Color: Carmesí", "jugando no se cambia de juego: " + play.Content);
                vrunning = false; await Task.Delay(3200); Is(play.IsEnabled && (string)play.Content == "Jugar", "cerrar Valorant: " + play.Content);
                // a 4:3 saved by an older version goes back to the recommended one
                ValorantSession vs = (ValorantSession)((Dictionary<string, StretchSession>)typeof(Ui).GetField("sessions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(live))[Game.Valorant.Id];
                vs.Choose(new Res { W = 1440, H = 1080, Hz = 200 });
                CheckBox von = (CheckBox)w.FindName("ValOn"); von.IsChecked = true; von.RaiseEvent(ck); await Task.Delay(300);
                Is(names() == "2560 × 1440,1720 × 1080,1500 × 1000,2091 × 1440" && ((ItemsControl)w.FindName("ValChecks")).Items.Count >= 3, "Valorant, solo las de 1,45 o mas: " + names());
                Is(((ResRow)rl.Items[3]).Best && rl.Items.Cast<ResRow>().Count(x => x.Best) == 1 && rl.SelectedIndex == 3 && vs.Last.W == 2091, "4:3 guardada vuelve a la recomendada: " + rl.SelectedIndex + " " + vs.Last.Name);
                Press(ro); await Task.Delay(400); Is(rol.Items.Count == 4, "el desplegable de Jugar tampoco la ofrece"); ro.Close();
                // fix the display mode: writes the fake ini and the row turns OK
                ItemsControl vc = (ItemsControl)w.FindName("ValChecks");
                Go(w, "Stretched Resolution"); Button fixMode = null;
                for (int i = 0; i < 30 && fixMode == null; i++) { await Task.Delay(100); fixMode = Buttons(vc).FirstOrDefault(b => b.DataContext is Check && ((Check)b.DataContext).Title == "Modo de pantalla"); }
                Is(fixMode != null, "fila de modo de pantalla: " + string.Join(", ", vc.Items.Cast<Check>().Select(c => c.Title + " " + c.Act + " " + c.HasUrl)) + " visible " + vc.IsVisible);
                Is(fixMode.Visibility == Visibility.Visible && (string)fixMode.Content == "Arreglar" && !((Check)fixMode.DataContext).Ok, "modo de pantalla con boton Arreglar");
                Press(fixMode); await Task.Delay(800);
                Is(File.ReadAllText(vini).Contains("\r\nFullscreenMode=1\r\n") && File.Exists(vini + ".tweakeroo") && vc.Items.Cast<Check>().First(c => c.Title == "Modo de pantalla").Ok, "arreglar lo deja en 1 y pasa a Correcto"); Go(w, "Jugar");
                cw.Text = "1440"; ch.Text = "1080"; Press(cg); Is(((TextBlock)w.FindName("CustomHint")).Text.StartsWith("Valorant solo estira"), "4:3 no vale en Valorant"); Is(red(), "en rojo tambien en Valorant"); rl.SelectedIndex = 1;
                Press(play); Is(screen.W == 1720 && mons.Count == 0 && ((string)play.Content).StartsWith("Conservar"), "prueba la resolucion");
                Press(back); Is(screen.W == 2560 && mons.Count == 2, "sin confirmar vuelve todo");
                Press(play); Press(play); Is(screen.W == 2560 && mons.Count == 0 && opened == was + 2, "confirmada abre el juego con la pantalla normal");
                vrunning = true; await Task.Delay(3200); Is(screen.W == 2560 && (string)play.Content == "Estirar ahora", "el mapa de antes no cuenta: " + play.Content);
                File.AppendAllText(vlog, Map(DateTime.UtcNow, "MainMenuV2", false)); await Task.Delay(2200); Is(screen.W == 2560, "en el menu no estira");
                File.AppendAllText(vlog, Map(DateTime.UtcNow, "Jam", true)); await Task.Delay(2500); Is(screen.W == 1720 && (string)play.Content == "Jugando", "estira al cargar el mapa");
                Press(back); await Task.Delay(2500); Is(screen.W == 2560 && mons.Count == 0 && (string)play.Content == "Estirar ahora", "a mitad de partida vuelve y no insiste");
                Press(play); Is(screen.W == 1720, "estirar a mano");
                vrunning = false; await Task.Delay(3200); Is(screen.W == 2560 && mons.Count == 2 && play.IsEnabled, "al cerrar vuelven pantalla y monitores");
                Press(play); Is(opened == was + 3 && screen.W == 2560, "la segunda vez no pregunta"); vrunning = true; await Task.Delay(3200); vrunning = false; await Task.Delay(3200);
                // colors: per game, only with the game open
                Go(w, "Colores"); ListBox cp = (ListBox)w.FindName("ColorProfiles"); cp.SelectedIndex = 3; Is(vib == 50 && Prefs.Now.ColorOf(Game.Valorant).Vib == 88 && Prefs.Now.ColorOf(Game.Fortnite).Profile == Vivid.None, "elegir no cambia la pantalla");
                ushort[] before = ramp; vrunning = true; await Task.Delay(2600); Is(vib == 88 && ramp[128] > before[128], "color puesto al abrir");
                vrunning = false; await Task.Delay(2600); Is(vib == 50 && ramp.SequenceEqual(before), "color devuelto al cerrar");
                ((Slider)w.FindName("ColVib")).Value = 70; Is(((ColorProfile)cp.SelectedItem).Id == Vivid.Mine && Prefs.Now.ColorOf(Game.Valorant).Vib == 70, "mover un control pasa a personalizado");
                Btn ct = (Btn)w.FindName("ColorTry"); Func<bool> cta = () => ct.Foreground.ToString() == ((Brush)w.FindResource("Cta")).ToString() && ct.BorderBrush.ToString() == ct.Foreground.ToString();
                Press(ct); Is(vib == 70, "probar"); Is((string)ct.Content == "Volviendo en 10 s" && cta(), "cuenta atras en Cta: " + ct.Content);
                await Task.Delay(3500); Is((string)ct.Content == "Volviendo en 7 s" || (string)ct.Content == "Volviendo en 6 s", "baja cada segundo: " + ct.Content);
                await Task.Delay(7500); Is(vib == 50, "la prueba se quita sola"); Is((string)ct.Content == "Probar 10 segundos" && !cta(), "vuelve el texto normal: " + ct.Content);
                // while playing: applied on open and removed on close
                Go(w, "Al jugar"); Press((Button)w.FindName("BoostAll")); Is(boosted == 0 && Prefs.Now.BoostsOf(Game.Valorant).Count == 1 && Prefs.Now.BoostsOf(Game.Fortnite).Count == 0, "elegir no cambia nada");
                vrunning = true; await Task.Delay(2800); Is(boosted == 1, "puesto al abrir"); vrunning = false; await Task.Delay(2800); Is(boosted == 0 && undo == "viejo", "quitado al cerrar");
                Press((Button)w.FindName("BoostNone")); Is(Prefs.Now.BoostsOf(Game.Valorant).Count == 0, "quitar todos");
                Go(w, "Mis FPS"); await Task.Delay(500); Is(((UIElement)w.FindName("EstBox")).Visibility != Visibility.Visible, "sin estimacion para Valorant");
                Go(w, "Conexión"); await Task.Delay(300); Is(((ItemsControl)w.FindName("NetTiles")).Items.Count == 2, "sin servidores de Epic");
                // opening Fortnite brings its part of the app
                running = true; await Task.Delay(4200); Is(pn.Text == fortPal && nav.Items.Cast<Circuit>().Any(c => c.Name == "Macros") && ((ItemsControl)w.FindName("NetTiles")).Items.Count == 3, "cambia solo al abrir Fortnite: " + pn.Text); running = false; await Task.Delay(1500);

                // CS2: like Valorant but without Macros or Limit, and Play writes its video file and stretches
                {
                    string cd2 = Path.Combine(Path.GetTempPath(), "tablero-clics");
                    ListBox g2 = (ListBox)w.FindName("Games");
                    FrameworkElement miss2 = (FrameworkElement)((Panel)((ScrollViewer)w.FindName("StretchPage")).Content).Children[1];
                    // the selector: 2 pinned slots; All games in Play: a card per game; pinning CS2 keeps the game on screen
                    Go(w, "Jugar");
                    Is(string.Join(",", g2.Items.Cast<Game>().Select(g => g.Id)) == "fortnite,valorant" && g2.SelectedItem == Game.Fortnite, "arriba 2 huecos: " + string.Join(",", g2.Items.Cast<Game>().Select(g => g.Id)));
                    Border agb = (Border)w.FindName("AllGamesBox"); System.Windows.Controls.Primitives.UniformGrid agg = (System.Windows.Controls.Primitives.UniformGrid)w.FindName("AllGamesGrid");
                    Func<string, Button> card = n => Buttons(agg).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == n);
                    Press((Button)w.FindName("AllGames")); await Task.Delay(700);
                    Is(agb.IsVisible && double.IsNaN(agb.Height) && agb.ActualHeight > 200 && agg.Children.Count == 3, "abrir Todos los juegos da 3 tarjetas: " + agg.Children.Count + " " + agb.ActualHeight);
                    Is((string)card("Fijado Fortnite").Content == "Fijado" && !card("Fijado Fortnite").IsEnabled && card("Jugar Counter-Strike 2").IsEnabled, "Fortnite fijado, CS2 se puede jugar");
                    Press(card("Fijar arriba Counter-Strike 2")); await Task.Delay(300);
                    Is(string.Join(",", g2.Items.Cast<Game>().Select(g => g.Id)) == "fortnite,cs2" && g2.SelectedItem == Game.Fortnite && Prefs.Now.Game == "fortnite" && string.Join(",", Prefs.Parse(File.ReadAllText(Path.Combine(cd2, "ajustes.json"))).Pinned) == "fortnite,cs2" && card("Fijado Counter-Strike 2") != null, "fijar CS2 ocupa el hueco del otro y conserva el actual");
                    cinst = false; Go(w, "Jugar"); g2.SelectedItem = Game.Cs2; await Task.Delay(1400);
                    Is(Prefs.Now.Game == "cs2" && miss2.IsVisible && !play.IsEnabled, "CS2 sin instalar: aviso y Jugar apagado");
                    cinst = true; Go(w, "Stretched Resolution"); Go(w, "Jugar");
                    Is(!miss2.IsVisible && play.IsEnabled && pn.Text == "Color: CS2", "CS2 instalado, con su paleta: " + pn.Text);
                    Is(!nav.Items.Cast<Circuit>().Any(c => c.Name == "Macros") && ((UIElement)w.FindName("Limits")).Visibility != Visibility.Visible && ((TextBlock)w.FindName("PlayText")).Text.StartsWith("Abre CS2 por Steam"), "CS2 sin Macros ni Límite");
                    List<Check> c2 = null;
                    for (int i = 0; i < 30 && (c2 == null || c2.Count == 0); i++) { await Task.Delay(100); c2 = ((ItemsControl)w.FindName("ValChecks")).Items.Cast<Check>().ToList(); }
                    Is(c2.Any(c => c.Text.StartsWith("CS2 está en otro monitor")) && c2.Any(c => c.Text.StartsWith("Con -vulkan")) && !c2.Any(c => c.Title == "Modo de pantalla" || c.Title == "Relación de aspecto"), "avisos de CS2: " + string.Join(", ", c2.Select(c => c.Title)));
                    // Play with the fake display: 4:3 written in its video file and the screen stretched
                    rl.SelectedItem = rl.Items.Cast<Res>().First(r => r.W == 1440 && r.H == 1080);
                    // Play on CS2's card from another game: switches to CS2 and does what the main Play does
                    g2.SelectedItem = Game.Fortnite; await Task.Delay(1400); Is(Prefs.Now.Game == "fortnite", "a Fortnite antes de jugar desde la tarjeta");
                    int was2 = opened; Press(card("Jugar Counter-Strike 2")); if (((string)play.Content).StartsWith("Conservar")) Press(play);
                    string vNow = File.ReadAllText(cvid);
                    Is(Prefs.Now.Game == "cs2" && g2.SelectedItem == Game.Cs2, "Jugar en la tarjeta cambia a CS2");
                    Is(opened == was2 + 1 && screen.W == 1440 && vNow.Contains("\"setting.defaultres\"\t\t\"1440\"") && vNow.Contains("\"setting.aspectratiomode\"\t\t\"0\"") && vNow.Contains("\"setting.monitor_index\"\t\t\"1\""), "Jugar en CS2 estira y escribe resolución y relación");
                    crunning = true; await Task.Delay(3200); Is((string)play.Content == "Jugando" && !live.SwitchGame(Game.Fortnite, true) && Prefs.Now.Game == "cs2" && !card("Jugar Fortnite").IsEnabled && !card("Jugar Counter-Strike 2").IsEnabled, "CS2 jugando, sin Jugar en las tarjetas: " + play.Content);
                    crunning = false; await Task.Delay(3200);
                    Is(screen.W == 2560 && File.ReadAllText(cvid) == cvid0 && play.IsEnabled && card("Jugar Counter-Strike 2").IsEnabled && ((TextBlock)w.FindName("Status")).Text.EndsWith("resolución restaurada."), "al cerrar CS2, pantalla y archivo restaurados: " + ((TextBlock)w.FindName("Status")).Text);
                    // Mis FPS from a fake history: CS2's text, the ETW status and no estimates
                    FpsLog.Add(cd2, Game.Cs2, new Match { Start = DateTime.Now.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss"), Map = "Partida", Seconds = 300, Median = 280, Low = 190, Max = 400, Cap = 400 });
                    typeof(FpsRecorder).GetProperty("Status").SetValue(null, FpsRecorder.Admin, null);
                    Go(w, "Mis FPS"); await Task.Delay(1500);
                    string ft = ((TextBlock)w.FindName("FpsText")).Text;
                    Is(ft.StartsWith("Se miden mientras Tweakeroo está abierto") && ft.EndsWith(FpsRecorder.Admin) && ((UIElement)w.FindName("EstBox")).Visibility != Visibility.Visible && ((UIElement)w.FindName("FpsNow")).Visibility == Visibility.Visible, "Mis FPS de CS2: " + ft);
                    typeof(FpsRecorder).GetProperty("Status").SetValue(null, null, null);
                    Go(w, "Jugar"); await Task.Delay(800);
                    Is(((TextBlock)w.FindName("PlayEst")).Text == "280 FPS", "media de 10 partidas en CS2: " + ((TextBlock)w.FindName("PlayEst")).Text);
                    Go(w, "Conexión"); await Task.Delay(300); Is(((ItemsControl)w.FindName("NetTiles")).Items.Count == 2, "Conexión de CS2: 2 casillas");
                    // the themes keep their own colors with CS2; Simple brings back CS2's
                    ListBox th2 = (ListBox)w.FindName("Themes");
                    foreach (string lk in new[] { "noche", "lluvia", "arcade" })
                    {
                        th2.SelectedItem = Theme.LookOf(lk); await Task.Delay(700);
                        Is(((SolidColorBrush)w.Resources["Card"]).Color == Theme.Parse(Theme.LookOf(lk).Pal.C["Card"]) && Prefs.Now.Game == "cs2", "temática " + lk + " con CS2");
                    }
                    th2.SelectedItem = Theme.LookOf("simple"); await Task.Delay(700);
                    Is(((SolidColorBrush)w.Resources["Card"]).Color == Theme.Parse(Theme.Named("CS2").C["Card"]), "Simple vuelve a la paleta CS2");
                    ((ListBox)w.FindName("SetMusic")).SelectedIndex = 0;
                    g2.SelectedItem = Game.Fortnite; await Task.Delay(1400); Is(Prefs.Now.Game == "fortnite", "de CS2 a Fortnite");
                }

                // game not found: warning, Play disabled and removing it from the selector
                ListBox gl = (ListBox)w.FindName("Games");
                FrameworkElement miss = (FrameworkElement)((Panel)((ScrollViewer)w.FindName("StretchPage")).Content).Children[1];
                Is(miss.Visibility == Visibility.Collapsed && gl.Items.Count == 2, "Fortnite instalado, sin aviso");
                Game.Valorant.Installed = () => false;
                // Valorant is no longer pinned: no slot marked, and its card says it is not installed
                Go(w, "Jugar"); live.SwitchGame(Game.Valorant, true); await Task.Delay(1400);
                Is(Prefs.Now.Game == "valorant" && miss.IsVisible && !play.IsEnabled, "Valorant sin instalar: aviso y Jugar apagado");
                Is(gl.SelectedItem == null && ((UIElement)w.FindName("GameMark")).Opacity == 0 && string.Join(",", gl.Items.Cast<Game>().Select(g => g.Id)) == "fortnite,cs2", "sin fijar: ningún hueco marcado");
                {
                    System.Windows.Controls.Primitives.UniformGrid agg = (System.Windows.Controls.Primitives.UniformGrid)w.FindName("AllGamesGrid");
                    Button vp = Buttons(agg).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Jugar Valorant");
                    Is(!vp.IsEnabled && ((Panel)((Panel)vp.Parent).Parent).Children.OfType<TextBlock>().Any(t => t.Text == "No instalado" && t.Visibility == Visibility.Visible), "tarjeta de Valorant: No instalado y sin Jugar");
                }
                Game.Valorant.Installed = () => true; Go(w, "Stretched Resolution"); Go(w, "Jugar");
                Is(!miss.IsVisible && play.IsEnabled, "encontrado: el aviso se va");
                Press((Button)w.FindName("OpenSettings")); await Task.Delay(300);
                CheckBox sf = (CheckBox)w.FindName("SetShowFortnite"), sv = (CheckBox)w.FindName("SetShowValorant"), sc2 = (CheckBox)w.FindName("SetShowCounterStrike2");
                sv.IsChecked = false; sv.RaiseEvent(ck); await Task.Delay(1400);
                Is(Prefs.Now.Game == "fortnite" && pn.Text == fortPal && gl.Items.Count == 2 && sv.IsChecked == false && Prefs.Parse(File.ReadAllText(Path.Combine(Path.GetTempPath(), "tablero-clics", "ajustes.json"))).Hidden.Single() == "valorant", "ocultar el juego a la vista vuelve a Fortnite");
                sc2.IsChecked = false; sc2.RaiseEvent(ck); await Task.Delay(300); // only Fortnite left
                sf.IsChecked = false; sf.RaiseEvent(ck);
                Is(sf.IsChecked == true && gl.Items.Count == 1 && ((TextBlock)w.FindName("Status")).Text == "Tiene que quedar al menos un juego.", "no se ocultan todos");
                sv.IsChecked = true; sv.RaiseEvent(ck); sc2.IsChecked = true; sc2.RaiseEvent(ck); Is(gl.Items.Count == 2 && Prefs.Now.Hidden.Count == 0 && gl.SelectedItem == Game.Fortnite, "vuelve al selector");
                {
                    // hiding a pinned CS2 leaves 2 cards and gives its slot to Valorant
                    System.Windows.Controls.Primitives.UniformGrid agg = (System.Windows.Controls.Primitives.UniformGrid)w.FindName("AllGamesGrid");
                    Press(Buttons(agg).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Fijar arriba Counter-Strike 2"));
                    Is(string.Join(",", gl.Items.Cast<Game>().Select(g => g.Id)) == "fortnite,cs2", "CS2 fijado otra vez");
                    sc2.IsChecked = false; sc2.RaiseEvent(ck); await Task.Delay(300);
                    Is(agg.Children.Count == 2 && string.Join(",", gl.Items.Cast<Game>().Select(g => g.Id)) == "fortnite,valorant", "ocultar CS2 deja 2 tarjetas: " + agg.Children.Count);
                    sc2.IsChecked = true; sc2.RaiseEvent(ck); Is(agg.Children.Count == 3 && Prefs.Now.Hidden.Count == 0, "CS2 vuelve a la cuadrícula");
                }

                // sound: only through the replaceable points, which here just note what is asked
                List<string> sndFx = new List<string>(), sndMusic = new List<string>();
                Action<string> realPlay = Sound.Play; Action<string, double> realMusic = Sound.Music;
                Sound.Play = id => sndFx.Add(id); Sound.Music = (id, v) => { if (id != "") sndMusic.Add(id); };
                Prefs sndKeep = Prefs.Parse(Prefs.Now.Json());
                try
                {
                    Press((Button)w.FindName("InfoClose")); Is(sndFx.SequenceEqual(new[] { "click" }), "un botón suena a clic: " + string.Join(",", sndFx));
                    sndFx.Clear(); gl.SelectedItem = Game.Valorant; await Task.Delay(1400); Is(sndFx.Contains("switch") && Prefs.Now.Game == "valorant", "cambiar de juego suena: " + string.Join(",", sndFx));
                    gl.SelectedItem = Game.Fortnite; await Task.Delay(1400);
                    ListBox mmode = (ListBox)w.FindName("SetMusicMode"), mtheme = (ListBox)w.FindName("SetMusic");
                    mmode.SelectedIndex = 1; mtheme.SelectedIndex = 1; Is(sndMusic.Contains("arcade"), "elegir música la pide");
                    mmode.SelectedIndex = 2; sndMusic.Clear(); mtheme.SelectedIndex = 2; await Task.Delay(2200); Is(sndMusic.Count == 0, "silenciada no pide música");
                    CheckBox sfxBox = (CheckBox)w.FindName("SetSfx"); sfxBox.IsChecked = false; sfxBox.RaiseEvent(ck); Is(!Prefs.Now.Sfx, "efectos apagados");
                    sndFx.Clear(); Press((Button)w.FindName("InfoClose")); gl.SelectedItem = Game.Valorant; await Task.Delay(1400); gl.SelectedItem = Game.Fortnite; await Task.Delay(1400);
                    Is(sndFx.Count == 0, "sin efectos no suena nada: " + string.Join(",", sndFx));
                    sfxBox.IsChecked = true; sfxBox.RaiseEvent(ck); mtheme.SelectedIndex = 0; mmode.SelectedIndex = 0;
                    Prefs sndNow = Prefs.Parse(File.ReadAllText(Path.Combine(Path.GetTempPath(), "tablero-clics", "ajustes.json")));
                    Is(sndNow.Sfx == sndKeep.Sfx && sndNow.Music == sndKeep.Music && sndNow.MusicMode == sndKeep.MusicMode && sndNow.MusicVolume == sndKeep.MusicVolume && sndNow.SfxVolume == sndKeep.SfxVolume, "el sonido queda como estaba");
                }
                finally { Sound.Play = realPlay; Sound.Music = realMusic; }

                // themes: one for the whole app, with its background, music and colors; Simple removes the layer
                {
                    ListBox th = (ListBox)w.FindName("Themes"), tracks = (ListBox)w.FindName("SetMusic");
                    Image sc = (Image)w.FindName("Scene");
                    UIElement pbox = (UIElement)w.FindName("PaletteBox");
                    TextBlock tn = (TextBlock)w.FindName("ThemeName");
                    Func<string, Color> res = k => ((SolidColorBrush)w.Resources[k]).Color;
                    Func<string> heard = () => ((LimitOption)tracks.SelectedItem).Value;
                    Is(sc.Visibility != Visibility.Visible && tn.Text == "Temática: Simple" && pbox.IsVisible && res("BgA").A == 255, "Simple: sin fondo y con paletas");
                    th.SelectedItem = Theme.LookOf("lluvia"); await Task.Delay(900);
                    Is(sc.IsVisible && sc.Source == Theme.LookOf("lluvia").Thumb && Prefs.Now.Music == "lluvia" && heard() == "lluvia" && tn.Text == "Temática: Lluvia" && !pbox.IsVisible, "Lluvia pone su fondo y su música");
                    Is(res("Card") == Theme.Parse(Theme.Named("Grafito").C["Card"]) && res("BgA").A < 255 && res("RailA").A < 255 && res("Card").A == 255, "colores de Lluvia; fondo y menú dejan ver la escena, tarjetas opacas");
                    // By the clock, 15 frames a second even with the buttons' shine looping (a Background timer starved to ~5); then it stops.
                    int drawn = 0;
                    EventHandler count = (s, e) => drawn++;
                    ((System.Windows.Media.Imaging.WriteableBitmap)sc.Source).Changed += count;
                    Ui.Always = true; live.SceneNow(); await Task.Delay(2000); Ui.Always = false; live.SceneNow();
                    int moved = drawn; await Task.Delay(300);
                    ((System.Windows.Media.Imaging.WriteableBitmap)sc.Source).Changed -= count;
                    Is(moved >= 26 && moved <= 31 && drawn == moved, "el fondo va a 15 fotogramas por segundo y se para: " + moved + " en 2 s, " + (drawn - moved) + " después");
                    th.SelectedItem = Theme.LookOf("arcade"); await Task.Delay(900);
                    Is(sc.Source == Theme.LookOf("arcade").Thumb && Prefs.Now.Music == "arcade" && heard() == "arcade" && res("Card") == Theme.Parse(Theme.Named("Ciruela").C["Card"]), "cambiar de temática cambia escena, música y colores");
                    Is(Prefs.Parse(File.ReadAllText(Path.Combine(Path.GetTempPath(), "tablero-clics", "ajustes.json"))).Theme == "arcade", "temática guardada");
                    gl.SelectedItem = Game.Valorant; await Task.Delay(1400);
                    Is(res("Card") == Theme.Parse(Theme.Named("Ciruela").C["Card"]) && pn.Text == "Color: Carmesí" && sc.IsVisible, "el juego no cambia los colores de la temática");
                    gl.SelectedItem = Game.Fortnite; await Task.Delay(1400);
                    th.SelectedItem = Theme.LookOf("simple"); await Task.Delay(900);
                    Is(!sc.IsVisible && sc.Source == null && Prefs.Now.Music == "arcade" && pbox.IsVisible && res("BgA").A == 255 && res("Card") == Theme.Parse(Prefs.PaletteOf(Path.Combine(Path.GetTempPath(), "tablero-clics"), Game.Fortnite).C["Card"]), "Simple quita la capa, vuelve a la paleta del juego y deja la música");
                    tracks.SelectedIndex = 0;
                }

                // settings: saved, shown immediately, and close hides without closing
                string cd = Path.Combine(Path.GetTempPath(), "tablero-clics");
                RoutedEventArgs clk = new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent);
                Press((Button)w.FindName("OpenSettings")); await Task.Delay(300);
                Is(((UIElement)w.FindName("SettingsPage")).IsVisible && nav.SelectedItem == null && ((UIElement)w.FindName("NavMark")).Visibility == Visibility.Hidden, "abre Ajustes");
                Is(w.FindName("SetPage") == null && !File.ReadAllText(Path.Combine(cd, "ajustes.json")).Contains("\"Page\""), "sin sección de inicio");
                Drop cfd = (Drop)w.FindName("SetConfirm"), mud = (Drop)w.FindName("SetMusicDrop"); Wheel cwh = (Wheel)w.FindName("SetConfirmWheel"); Tri mo = (Tri)w.FindName("SetMotion");
                Func<System.Windows.Input.Key, System.Windows.Input.KeyEventArgs> key = k => new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, k) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                // the wheel: one tick per number at most, saved when it settles
                Press(cfd); await Task.Delay(300); Is(cfd.IsOpen && cwh.IsVisible, "abre el desplegable del tiempo");
                int ticks = 0; Action<string> rp = Sound.Play; Sound.Play = id => { if (id == "hover") ticks++; };
                try { cwh.Spin(5); await Task.Delay(1200); } finally { Sound.Play = rp; }
                Is(Prefs.Now.Confirm == 5 && Prefs.Parse(File.ReadAllText(Path.Combine(cd, "ajustes.json"))).Confirm == 5 && (string)cfd.Content == "5 s" && ticks >= 1 && ticks <= 10, "la rueda guarda 5 (" + ticks + " tics)");
                cwh.RaiseEvent(key(System.Windows.Input.Key.Down)); cwh.RaiseEvent(key(System.Windows.Input.Key.Down)); await Task.Delay(1000); Is(Prefs.Now.Confirm == 7, "flechas en la rueda: " + Prefs.Now.Confirm);
                cwh.Spin(30); await Task.Delay(1200); cwh.RaiseEvent(key(System.Windows.Input.Key.Escape)); await Task.Delay(100); Is(!cfd.IsOpen && Prefs.Now.Confirm == 30, "Esc cierra y queda 30");
                // the three-position switch
                mo.Snap(1); await Task.Delay(800); Is(Prefs.Now.Motion == 1 && Fx.Level == 1 && mo.Value == 1, "interruptor en Sin rebote");
                mo.RaiseEvent(key(System.Windows.Input.Key.Right)); await Task.Delay(800); Is(Prefs.Now.Motion == 2 && mo.Value == 2, "flecha en el interruptor");
                // music dropdown: the field shows the choice
                Press(mud); await Task.Delay(300); ListBox mus = (ListBox)w.FindName("SetMusic"); Is(mud.IsOpen && mus.IsVisible, "abre el desplegable de música");
                mus.SelectedIndex = 1; Is(Prefs.Now.Music == "arcade" && ((LimitOption)mud.Content).Value == "arcade", "el desplegable cambia la música");
                mus.SelectedIndex = 3; Is(Prefs.Now.Music == "" && ((IconOption)mud.Content).Muted, "música apagada");
                Press(mud); Is(!mud.IsOpen, "el campo cierra el desplegable"); mus.SelectedIndex = 0;
                Prefs sp = Prefs.Parse(File.ReadAllText(Path.Combine(cd, "ajustes.json")));
                Is(sp.Confirm == 30 && sp.Motion == 2 && sp.Music == "noche" && !Fx.On && ((TextBlock)w.FindName("PlayNote")).Text.Contains("30 segundos"), "ajustes guardados y aplicados");
                {
                    // without animations All games closes and opens at once
                    Border agb = (Border)w.FindName("AllGamesBox"); Button agBtn = (Button)w.FindName("AllGames");
                    Press(agBtn); Is(agb.Visibility == Visibility.Collapsed, "sin animaciones cierra de golpe");
                    Press(agBtn); Is(agb.Visibility == Visibility.Visible && double.IsNaN(agb.Height), "sin animaciones abre de golpe: " + agb.Visibility + " " + agb.Height);
                }
                Is(!((CheckBox)w.FindName("SetBoot")).IsEnabled, "sin instalar no se abre con Windows");
                CheckBox mi = (CheckBox)w.FindName("SetMinimize"), mp = (CheckBox)w.FindName("Minimize"); mi.IsChecked = !(mp.IsChecked == true); mi.RaiseEvent(clk); Is(mp.IsChecked == mi.IsChecked, "minimizar al dia en Jugar");
                ListBox l2 = (ListBox)w.FindName("SetLimits"), l1 = (ListBox)w.FindName("Limits"); l2.SelectedIndex = 1; Is(l1.SelectedIndex == 1, "limite al dia en Jugar"); l1.SelectedIndex = 0; Is(l2.SelectedIndex == 0, "y al reves");
                Button cs = (Button)w.FindName("ClearScan");
                Press(cs); Is((string)cs.Content == "Pulsa otra vez para confirmar" && Scanner.Load(cd) != null, "borrar pide confirmar"); Press(cs); Is(Scanner.Load(cd) == null && (string)cs.Content == "Borrar escaneo", "borrar escaneo");
                Press((Button)w.FindName("ShowVersions")); Is(((UIElement)w.FindName("InfoBox")).IsVisible && ((ItemsControl)w.FindName("InfoVersions")).Items.Count >= 8, "historial de versiones");
                Press((Button)w.FindName("InfoClose")); Is(!((UIElement)w.FindName("InfoBox")).IsVisible, "cerrar historial");
                Press((Button)w.FindName("ShowTerms")); Is(((TextBlock)w.FindName("InfoText")).Text.Length > 2000, "terminos"); Press((Button)w.FindName("InfoClose"));
                Is(live.Support().Contains("Tweakeroo " + Installer.Short) && !live.Support().Contains(Environment.UserName), "datos de soporte sin nombre");
                int asked = 0; live.CheckUpdates = () => { asked++; return Task.FromResult("Es la versión más reciente."); }; Press((Button)w.FindName("CheckNow")); Is(asked == 1, "buscar ahora");
                CheckBox tr = (CheckBox)w.FindName("SetTray"); tr.IsChecked = true; tr.RaiseEvent(clk);
                w.Close(); await Task.Delay(200); Is(!w.IsVisible && w.IsLoaded, "cerrar se queda junto al reloj"); live.Reveal(); await Task.Delay(200); Is(w.IsVisible, "vuelve a abrirse");
                Button rs = (Button)w.FindName("ResetSet"); Press(rs); Press(rs);
                Is(!Prefs.Now.Tray && Prefs.Now.Confirm == 15 && tr.IsChecked == false && cwh.Value == 15 && (string)cfd.Content == "15 s" && mo.Value == 0 && Fx.Level == 0, "restablecer Ajustes");
                Go(w, "Jugar"); Is(((UIElement)w.FindName("NavMark")).Visibility == Visibility.Visible && !((UIElement)w.FindName("SettingsPage")).IsVisible, "vuelve a las secciones");
                return "OK clics";
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Root.Substring(5), false); }
        }
    }
}
#endif
