using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Tablero
{
    public class NetTile : Vm
    {
        public string Label { get; set; }
        public string Main { get; set; }
        public string Sub { get; set; }
        public void Set(string main) { Main = main; Raise("Main"); }
        public void Show(PingStats s)
        {
            Main = s.Sent == 0 ? "…" : s.Silent ? "Sin respuesta" : s.Median + " ms";
            Sub = s.Sent == 0 ? "" : s.Silent ? "" : "Variación " + NetTest.Num(s.Jitter) + " ms, pérdida " + NetTest.Num(s.Loss) + " %, peor " + s.Max + " ms";
            Raise("Main"); Raise("Sub");
        }
    }

    // Connection: measure the network and keep Wi-Fi stable while playing.
    partial class Ui
    {
        const int NetCount = 75, NetGap = 200; // 15 seconds
        internal static int NetTries = NetCount, NetPause = NetGap;
        List<NetTile> netTiles;
        bool testing;
        List<NetTile> speedTiles;

        void StartNet()
        {
            netTiles = new List<NetTile> { new NetTile { Label = "Router" }, new NetTile { Label = "Internet" }, new NetTile { Label = "Servidores de Epic" } };
            F<ItemsControl>("NetTiles").ItemsSource = netTiles;
            F<ItemsControl>("NetChecks").ItemTemplate = F<ItemsControl>("Checks").ItemTemplate;
            speedTiles = new List<NetTile> { new NetTile { Label = "Descarga", Main = "…" }, new NetTile { Label = "Subida", Main = "…" }, new NetTile { Label = "Ping con carga", Main = "…" } };
            F<ItemsControl>("SpeedTiles").ItemsSource = speedTiles;
            ShowNetGame();
            SpeedResult sp = SpeedTest.Load(dir);
            if (sp != null) ShowSpeed(sp, null);
            F<Btn>("SpeedRun").Click += async (s, e) => await TestSpeed();
            F<Btn>("NetRun").Click += async (s, e) => await TestNet();
            F<Btn>("NetTweaks").Click += (s, e) => Open("Red");
            Toggle("NetSteady", () => Prefs.Now.WifiSteady, v => { Prefs.Now.WifiSteady = v; SteadyNow(); });
            w.Closed += (s, e) => Wifi.Steady(false);
            NetResult old = NetTest.Load(dir);
            if (old != null) ShowNet(old, null);
            else foreach (NetTile x in netTiles) x.Show(new PingStats());
        }

        // Valorant and CS2 servers are not measured: only router and internet.
        void ShowNetGame()
        {
            F<ItemsControl>("NetTiles").ItemsSource = game != Game.Fortnite ? netTiles.Take(2).ToList() : netTiles;
            ShowState();
        }
        void ShowState() { F<TextBlock>("NetState").Text = Wifi.On ? "Activo ahora." : Prefs.Now.WifiSteady ? "Se activa al abrir el juego." : ""; }

        // Only with a game open; when it closes, Wi-Fi goes back to normal behavior. GameTick calls it with each check.
        void SteadyNow()
        {
            bool want = Prefs.Now.WifiSteady && lastOpen != null;
            if (want != Wifi.On) Wifi.Steady(want);
            if (F<FrameworkElement>("NetPage").IsVisible) ShowState();
        }

        async Task OpenNet()
        {
            int signal = 0, channel = 0;
            bool wifi = false;
            List<string> busy = null;
            await Task.Run(() => { wifi = Wifi.Info(out signal, out channel); busy = NetTest.Busy(); });
            F<FrameworkElement>("NetWifi").Visibility = wifi ? Visibility.Visible : Visibility.Collapsed;
            F<ItemsControl>("NetChecks").ItemsSource = Wifi.Advice(wifi, signal, channel, busy);
            SteadyNow();
        }

        async Task TestNet()
        {
            if (testing) return;
            testing = true;
            F<Btn>("SpeedRun").IsEnabled = false;
            Btn b = F<Btn>("NetRun");
            b.IsEnabled = false;
            b.Content = "Midiendo…";
            F<TextBlock>("NetVerdict").Text = "Midiendo durante 15 segundos. Lo más fiable es hacerlo sin descargas y, otra vez, mientras se juega.";
            F<TextBlock>("NetBefore").Text = "";
            foreach (NetTile x in netTiles) x.Show(new PingStats());
            NetResult old = NetTest.Load(dir), r = new NetResult { Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            int signal, channel;
            string router = null;
            await Task.Run(() => { router = NetTest.Router(); r.Wifi = Wifi.Info(out signal, out channel); });
            bool epic = game == Game.Fortnite;
            if (epic) r.Region = await NetTest.Nearest();
            if (r.Region != null) { netTiles[2].Label = "Servidores de Epic: " + NetTest.Name(r.Region); RefreshTiles(); }
            Task<PingStats> a = NetTest.Run(router, NetTries, NetPause, netTiles[0].Show), c = NetTest.Run(NetTest.Internet, NetTries, NetPause, netTiles[1].Show),
                d = NetTest.Run(r.Region == null ? null : NetTest.Host(r.Region), NetTries, NetPause, netTiles[2].Show);
            r.Router = await a; r.Internet = await c; r.Epic = await d;
            if (epic) NetTest.Save(dir, r);
            ShowNet(r, epic ? old : null);
            b.IsEnabled = true;
            b.Content = "Medir otra vez";
            F<Btn>("SpeedRun").IsEnabled = true;
            testing = false;
        }

        async Task TestSpeed()
        {
            if (testing) return;
            // Saturating the connection in the middle of a match ruins it.
            Game open = Game.Open;
            if (open != null) { Say("Cierra " + open.Name + " antes de medir la velocidad: la prueba llena la conexión.", false); return; }
            testing = true;
            Btn b = F<Btn>("SpeedRun");
            b.IsEnabled = F<Btn>("NetRun").IsEnabled = false;
            b.Content = "Midiendo…";
            foreach (NetTile x in speedTiles) x.Set("…");
            F<TextBlock>("SpeedVerdict").Text = F<TextBlock>("SpeedBefore").Text = "";
            SpeedResult old = SpeedTest.Load(dir), r = null;
            string err = null;
            try { r = await SpeedTest.Run((k, v) => w.Dispatcher.BeginInvoke(new Action(() => speedTiles[k == "Down" ? 0 : 1].Set(Mbps(v))))); }
            catch (Exception ex) { err = ex.Message; }
            if (r != null) { SpeedTest.Save(dir, r); await w.Dispatcher.BeginInvoke(new Action(() => ShowSpeed(r, old)), DispatcherPriority.Background); }
            else F<TextBlock>("SpeedVerdict").Text = "No se pudo medir: " + err;
            b.IsEnabled = F<Btn>("NetRun").IsEnabled = true;
            b.Content = "Medir otra vez";
            testing = false;
        }
        static string Mbps(double v) { return v <= 0 ? "…" : (v >= 100 ? Math.Round(v).ToString() : NetTest.Num(v)) + " Mbps"; }
        void ShowSpeed(SpeedResult r, SpeedResult old)
        {
            speedTiles[0].Set(Mbps(r.Down));
            speedTiles[1].Set(Mbps(r.Up));
            speedTiles[2].Set(r.Loaded > 0 ? r.Loaded + " ms" : "Sin respuesta");
            F<TextBlock>("SpeedVerdict").Text = SpeedTest.Verdict(r);
            F<TextBlock>("SpeedBefore").Text = old == null ? "" : "Prueba anterior (" + old.Date.Substring(0, 16) + "): descarga " + Mbps(old.Down) + ", subida " + Mbps(old.Up) + ".";
        }

        void RefreshTiles() { F<ItemsControl>("NetTiles").ItemsSource = null; ShowNetGame(); }

        void ShowNet(NetResult r, NetResult old)
        {
            netTiles[0].Show(r.Router);
            netTiles[1].Show(r.Internet);
            netTiles[2].Label = "Servidores de Epic" + (r.Region == null ? "" : ": " + NetTest.Name(r.Region));
            RefreshTiles();
            netTiles[2].Show(r.Epic);
            F<TextBlock>("NetVerdict").Text = game != Game.Fortnite ? NetTest.Home(r) : NetTest.Verdict(r);
            F<TextBlock>("NetBefore").Text = NetTest.Compare(old, r);
        }
    }
}
