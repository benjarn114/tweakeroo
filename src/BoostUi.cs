using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Tablero
{
    // What only lasts while playing, and what still needs a restart.
    partial class Ui
    {
        BoostSession boosts;
        List<BoostRow> boostRows;
        bool boosting;
        Pending pendingNow;
        Pending pending { get { if (pendingNow == null) pendingNow = new Pending(dir); return pendingNow; } }
        void ShowWaiting() { foreach (Row r in every) r.SetWaiting(pending.Need(r.T)); }

        void StartBoosts()
        {
            Drivers.Load(dir);
            boosts = new BoostSession(dir);
            boostRows = Boost.All.Select(b => new BoostRow { Id = b.Id, Name = b.Name, Desc = b.Desc }).ToList();
            ItemsControl l = F<ItemsControl>("Boosts");
            l.ItemsSource = boostRows;
            l.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) => SaveBoosts()));
            F<Btn>("BoostAll").Click += (s, e) => { foreach (BoostRow r in boostRows) r.Set(true); SaveBoosts(); };
            F<Btn>("BoostNone").Click += (s, e) => { foreach (BoostRow r in boostRows) r.Set(false); SaveBoosts(); };
            w.Closed += (s, e) => { try { boosts.Use(new string[0]); } catch (Exception) { } };
            ShowBoosts();
            BoostTick(Game.Open);
        }
        void SaveBoosts()
        {
            Prefs.Now.Boosts[game.Id] = boostRows.Where(r => r.On).Select(r => r.Id).ToList();
            SavePrefs();
            BoostTick(Game.Open);
            ShowBoosts();
        }
        void ShowBoosts()
        {
            if (boosts == null) return;
            List<string> on = Prefs.Now.BoostsOf(game);
            foreach (BoostRow r in boostRows) r.Set(on.Contains(r.Id));
            F<TextBlock>("BoostText").Text = "Se ponen al abrir " + game.Name + " y se quitan al cerrarlo, sin reiniciar. Cada juego guarda los suyos.";
            F<TextBlock>("BoostState").Text = boosts.Count > 0 ? "Activos ahora: " + boosts.Count : on.Count == 0 ? "Ninguno elegido." : on.Count == 1 ? "1 elegido." : on.Count + " elegidos.";
        }
        async void BoostTick(Game open)
        {
            if (boosts == null || boosting) return;
            List<string> want = open == null ? new List<string>() : Prefs.Now.BoostsOf(open);
            boosting = true;
            int before = boosts.Count;
            List<string> bad = null;
            // Stopping a service can take a while: off the UI thread.
            await Task.Run(() => { try { bad = boosts.Use(want); Eco.Keep(); } catch (IOException) { } catch (UnauthorizedAccessException) { } });
            boosting = false;
            if (boosts.Count != before) ShowBoosts();
            if (bad != null && bad.Count > 0) Say("No se pudo aplicar: " + string.Join(", ", bad.Select(L.T)), false);
        }
    }
}
