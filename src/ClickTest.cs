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
    // Recorre la interfaz con clics reales sobre tweaks falsos en una clave de usar y tirar.
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
        static bool running;
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
        }
        public static async Task<string> Run(Window w, Ui ui)
        {
            w0 = w; live = ui;
            try
            {
                ListBox nav = (ListBox)w.FindName("Circuits");
                Is(string.Join(",", nav.Items.Cast<Circuit>().Select(c => c.Name)) == "Jugar,Mi PC,Mis FPS,Macros,Mejoras", "menu corto al abrir");
                await Task.Delay(400);
                Is(((UIElement)w.FindName("SkelPage")).Visibility != Visibility.Visible && ((UIElement)w.FindName("SkelSaves")).Visibility != Visibility.Visible, "los skeletons se van");
                // una lista desactivada no puede traer el fondo blanco de Windows
                foreach (string n in new[] { "Saves", "Rows", "Resolutions" }) Is(((ListBox)w.FindName(n)).Template == w.Resources[n == "Resolutions" ? "BareList" : "PlainList"], "plantilla de " + n);
                Go(w, "Mejoras"); Key(nav, System.Windows.Input.Key.Right); Is(nav.Items.Count > 5, "Mejoras se despliega");
                Key(nav, System.Windows.Input.Key.Left); await Task.Delay(500); Is(nav.Items.Count == 5, "Mejoras se pliega");
                Key(nav, System.Windows.Input.Key.Enter); Key(nav, System.Windows.Input.Key.Enter); Key(nav, System.Windows.Input.Key.Enter); await Task.Delay(500);
                Is(nav.Items.Count == 5 + 2 && nav.Items.Cast<Circuit>().Distinct().Count() == nav.Items.Count, "abrir y cerrar rapido no duplica");
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
                // escaner: activa lo seguro y deja lo avanzado y lo de riesgo
                await Click(w, (Button)w.FindName("Scan"));
                Is(nav.Items.Cast<Circuit>().Any(c => c.Name == "Recomendados"), "aparece Recomendados");
                Is(Scanner.Load(Path.Combine(Path.GetTempPath(), "tablero-clics")) != null, "el escaneo queda guardado");
                await Click(w, (Button)w.FindName("Scan"));
                Is(V("a") == "d:1" && V("b") == Op.None && V("r") == Op.None, "optimizar activa solo lo recomendado");
                ((ListBox)w.FindName("Palettes")).SelectedIndex = 1; await Task.Delay(900);
                Is(((TextBlock)w.FindName("PaletteName")).Text == "Color: Medianoche", "cambia la paleta");
                // stretched con pantalla, juego y lanzador de mentira
                Button play = (Button)w.FindName("Play"), back = (Button)w.FindName("Back");
                Go(w, "Jugar"); await Task.Delay(300);
                Is(((ListBox)w.FindName("Resolutions")).Items.Count == 3 && (string)play.Content == "Jugar a 1720 × 1080", "lista de resoluciones");
                Press(play);
                Is(screen.Name == "1720 × 1080" && ((string)play.Content).StartsWith("Conservar") && opened == 0, "pide confirmar antes de abrir");
                Press(back); Is(screen.Name == "2560 × 1440" && File.ReadAllText(IniOp.File) == ini, "volver repone pantalla e ini");
                Press(play); Press(play); Is(opened == 1 && screen.W == 1720, "confirmar abre el juego");
                running = true; await Task.Delay(3000); Is((string)play.Content == "Jugando a 1720 × 1080" && !play.IsEnabled, "detecta el juego");
                Press(back); Is(screen.W == 2560 && File.ReadAllText(IniOp.File) != ini, "a mitad de partida solo vuelve la pantalla");
                running = false; await Task.Delay(3000); Is(File.ReadAllText(IniOp.File) == ini && play.IsEnabled && back.Visibility != Visibility.Visible, "al cerrar el juego vuelve el ini");
                Press(play); Is(opened == 2, "la segunda vez no pregunta");
                running = true; await Task.Delay(3000); running = false; await Task.Delay(3000); Is(screen.W == 2560 && File.ReadAllText(IniOp.File) == ini, "cerrar el juego lo devuelve todo");
                ((ListBox)w.FindName("Resolutions")).SelectedIndex = 0; Press(play); Is(opened == 3 && screen.W == 2560, "la normal abre sin preguntar ni tocar la pantalla");
                running = true; await Task.Delay(3000); running = false; await Task.Delay(3000); Is(File.ReadAllText(IniOp.File) == ini, "la normal tambien repone el ini");
                Border shine = (Border)((Control)play).Template.FindName("shine", play);
                Is(!Fx.On || (shine.Background != null && shine.IsVisible), "el boton principal brilla");
                // personalizada: a prueba, y se guarda solo al confirmar
                TextBox cw = (TextBox)w.FindName("CustomW"), ch = (TextBox)w.FindName("CustomH"); Button cg = (Button)w.FindName("CustomGo");
                cw.Text = "9999"; ch.Text = "1080"; Press(cg); Is(((TextBlock)w.FindName("CustomHint")).Text.StartsWith("Máximo: la resolución"), "personalizada demasiado grande");
                cw.Text = "1500"; ch.Text = "1000"; Press(cg); Is((string)play.Content == "Jugar a 1500 × 1000", "personalizada elegida");
                Press(play); Is(tries == 1 && saved == 0 && screen.W == 1500, "a prueba"); Press(back); Is(reverts == 1 && saved == 0 && screen.W == 2560, "si no convence se retira");
                ((ListBox)w.FindName("Limits")).SelectedIndex = 2;
                Press(play); Press(play); Is(saved == 1 && opened == 4 && File.ReadAllText(IniOp.File).Contains("FrameRateLimit=0.000000"), "confirmada se guarda y aplica el limite");
                running = true; await Task.Delay(3000); running = false; await Task.Delay(3000); Is(File.ReadAllText(IniOp.File) == ini && screen.W == 2560, "y todo vuelve");
                Go(w, "Mis FPS"); await Task.Delay(2500); Is(((ItemsControl)w.FindName("Estimates")).Items.Count >= 1, "estimacion a la vista");
                Is(((ItemsControl)w.FindName("Plan")).Items.Count >= 2, "lista de compras futuras");
                Go(w, "Mejoras"); Is(((ItemsControl)w.FindName("Checks")).Items.Count > 3, "la revision lista consejos");
                // macros: nada sin aceptar, sin tecla o con una tecla repetida
                Go(w, "Macros"); await Task.Delay(100);
                FrameworkElement body = (FrameworkElement)w.FindName("MacroBody");
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
                // el clic derecho y la rueda llegan al elegir tecla del juego, como los manda Windows
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
                // ajustes: se guardan, se ven al momento y cerrar esconde sin cerrar
                string cd = Path.Combine(Path.GetTempPath(), "tablero-clics");
                RoutedEventArgs clk = new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent);
                Press((Button)w.FindName("OpenSettings")); await Task.Delay(300);
                Is(((UIElement)w.FindName("SettingsPage")).IsVisible && nav.SelectedItem == null && ((UIElement)w.FindName("NavMark")).Visibility == Visibility.Hidden, "abre Ajustes");
                ListBox cf = (ListBox)w.FindName("SetConfirm"), mo = (ListBox)w.FindName("SetMotion"), pg = (ListBox)w.FindName("SetPage");
                cf.SelectedIndex = 2; mo.SelectedIndex = 2; pg.SelectedIndex = 3;
                Prefs sp = Prefs.Parse(File.ReadAllText(Path.Combine(cd, "ajustes.json")));
                Is(sp.Confirm == 30 && sp.Motion == 2 && sp.Page == "Macros" && !Fx.On && ((TextBlock)w.FindName("PlayNote")).Text.Contains("30 segundos"), "ajustes guardados y aplicados");
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
                Is(!Prefs.Now.Tray && Prefs.Now.Confirm == 15 && tr.IsChecked == false && cf.SelectedIndex == 1 && Fx.Level == 0, "restablecer Ajustes");
                Go(w, "Jugar"); Is(((UIElement)w.FindName("NavMark")).Visibility == Visibility.Visible && !((UIElement)w.FindName("SettingsPage")).IsVisible, "vuelve a las secciones");
                return "OK clics";
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(Root.Substring(5), false); }
        }
    }
}
#endif
