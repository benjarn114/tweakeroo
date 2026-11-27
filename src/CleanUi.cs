using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Tablero
{
    // Cleanup card at the end of My PC: search (read only), choose, confirm, delete.
    partial class Ui
    {
        List<CleanItem> cleanItems = new List<CleanItem>();
        bool cleaning;

        void StartClean()
        {
            F<Btn>("CleanFind").Click += async (s, e) => await FindClean();
            F<Btn>("CleanGo").Click += async (s, e) => await RunClean();
        }

        internal async Task FindClean()
        {
            if (cleaning) return;
            cleaning = true;
            Btn find = F<Btn>("CleanFind");
            find.IsEnabled = F<Btn>("CleanGo").IsEnabled = false;
            find.Content = "Calculando…";
            List<CleanItem> found = await Task.Run(() => Cleanup.Find());
            foreach (CleanItem i in found) i.Changed = CleanTotal;
            cleanItems = found;
            F<ItemsControl>("CleanList").ItemsSource = found;
            F<FrameworkElement>("CleanBox").Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            F<FrameworkElement>("CleanNone").Visibility = found.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            find.Content = "Buscar qué limpiar";
            find.IsEnabled = true;
            cleaning = false;
            CleanTotal();
        }

        void CleanTotal()
        {
            long t = cleanItems.Where(i => i.On).Sum(i => i.Bytes);
            F<TextBlock>("CleanTotal").Text = "Total seleccionado: " + Cleanup.Size(t);
            Btn go = F<Btn>("CleanGo");
            go.Content = "Limpiar " + Cleanup.Size(t);
            go.IsEnabled = t > 0 && !cleaning;
        }

        async Task RunClean()
        {
            List<CleanItem> sel = cleanItems.Where(i => i.On).ToList();
            if (cleaning || sel.Count == 0) return;
            if (Game.Open != null) { Say("Cierra el juego antes de limpiar.", false); return; }
            if (MessageBox.Show(w, Cleanup.Ask(sel), L.T("Limpieza"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (Game.Open != null) { Say("Cierra el juego antes de limpiar.", false); return; } // it may have opened while the dialog was up
            cleaning = true;
            Btn go = F<Btn>("CleanGo"), find = F<Btn>("CleanFind");
            go.IsEnabled = find.IsEnabled = F<ItemsControl>("CleanList").IsEnabled = false;
            go.Content = "Limpiando…";
            CleanStats s = new CleanStats();
            await Task.Run(() => { foreach (CleanItem i in sel) Cleanup.Clean(i, s); });
            F<ItemsControl>("CleanList").IsEnabled = true;
            cleaning = false;
            Say(Cleanup.Freed(s), false);
            await FindClean();
        }
    }
}
