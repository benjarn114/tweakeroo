using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;

namespace Tablero
{
    public class Prefs
    {
        public bool StartMin { get; set; }
        public bool Tray { get; set; }
        public int Motion { get; set; }   // 0 all, 1 no bounce, 2 off
        public int Confirm { get; set; }  // seconds to confirm a resolution, 5 to 30
        public bool AutoCheck { get; set; }
        public bool WifiSteady { get; set; }
        public string Game { get; set; }      // game on screen
        public bool KeepGame { get; set; }    // do not switch by itself when another game opens
        public Dictionary<string, ColorPick> Colors { get; set; } // per game
        public Dictionary<string, List<string>> Boosts { get; set; } // what is applied while playing, per game
        public Dictionary<string, string> Paths { get; set; } // .exe picked by hand, per game
        public List<string> Hidden { get; set; }              // games hidden from the selector
        public List<string> Pinned { get; set; }              // the 2 games at the top of the selector, in order
        public string Music { get; set; }     // "noche", "arcade", "lluvia" or "" (off)
        public int MusicMode { get; set; }    // 0 only with the app in front, 1 always except while playing, 2 muted
        public int MusicVolume { get; set; }  // 0-100
        public bool Sfx { get; set; }
        public int SfxVolume { get; set; }    // 0-100
        public bool SoundSaved { get; set; }  // false = saved before sound existed: it gets the defaults
        public string Theme { get; set; }     // "noche", "lluvia", "arcade" or "simple" (palette per game, no background)
        public bool CrossOn { get; set; }           // crosshair drawn over Fortnite
        public bool CrossAlways { get; set; }       // also in the lobby and while loading; false = "Solo en partida" (the default)
        public string CrossNow { get; set; }        // Crosshair.Serialize() of the one being edited; null = default
        public List<string> CrossSaved { get; set; } // saved crosshairs, Serialize() each
        public string OverlayKey { get; set; }      // shortcut that shows and hides the overlay, Hotkeys.Text; "" = none
        public string MarkKey { get; set; }         // shortcut that marks a lag in the match (Tracker.Mark); "" = none
        public bool OverlayOn { get; set; }         // performance overlay over the game (Overlay.cs); OverlayKey also switches it
        public int OverlayStyle { get; set; }       // 0 one line, 1 column, 2 numbers only
        public int OverlayCorner { get; set; }      // main screen: 0 top left, 1 top right, 2 bottom left, 3 bottom right
        public List<string> OverlayShow { get; set; } // OverlayModel.Ids shown; null = the defaults
        public string ToolGame { get; set; }        // Tools > eDPI: "Fortnite", "Valorant" or "CS2"; null = the game on screen
        public double ToolSens { get; set; }        // last sensitivity typed (Fortnite in %), 0 = none
        public int ToolDpi { get; set; }            // last DPI typed, 0 = none
        public int ReactBest { get; set; }          // best average of 5 reaction tries in ms, 0 = none
        public string RegionDay { get; set; }       // day (yyyy-MM-dd) the server notice was last shown as a toast; null = never
        public string WeekDay { get; set; }         // day (yyyy-MM-dd) the weekly summary was last shown; null = never
        public bool LaptopMode { get; set; }        // laptop profile (Laptop.cs): Balanced on battery, the previous plan plugged in
        public string LaptopPlan { get; set; }      // plan active before unplugging, to go back to; null = none
        public bool HeavyOn { get; set; }           // ask to close heavy apps when a game opens (Heavy.cs)
        public List<string> HeavyApps { get; set; } // process names chosen; null = Heavy.Defaults
        public int HeavyKeep { get; set; }          // remembered answer: 0 ask, 1 close without asking, 2 do not ask

        public static Prefs Now = Default();

        // The crosshair being edited is always one of the saved ones (CrossNow is a copy of it), and edits go straight to it.
        // Before, CrossNow could hold changes that were never saved: they become one more saved crosshair, so nothing is lost.
        public static void CrossFix(Prefs p)
        {
            p.CrossSaved = (p.CrossSaved ?? new List<string>()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (string.IsNullOrEmpty(p.CrossNow) && p.CrossSaved.Count > 0) p.CrossNow = p.CrossSaved[0];
            if (p.CrossSaved.Contains(p.CrossNow)) return;
            Crosshair c = Crosshair.Deserialize(p.CrossNow);
            c.Name = FreeName(c.Name, p.CrossSaved.Select(x => Crosshair.Deserialize(x).Name));
            p.CrossNow = c.Serialize();
            p.CrossSaved.Add(p.CrossNow);
        }
        // The name, or the name with the first free number after it.
        public static string FreeName(string name, IEnumerable<string> taken)
        {
            HashSet<string> t = new HashSet<string>(taken.Select(x => (x ?? "").Trim()), StringComparer.CurrentCultureIgnoreCase);
            string n = string.IsNullOrWhiteSpace(name) ? "Crosshair" : name.Trim();
            if (!t.Contains(n)) return n;
            for (int i = 2; ; i++) if (!t.Contains(n + " " + i)) return n + " " + i;
        }
        public static Prefs Default() { Prefs d = Default0(); CrossFix(d); return d; }
        static Prefs Default0() { return new Prefs { Confirm = 15, AutoCheck = true, Game = Tablero.Game.Fortnite.Id, Colors = new Dictionary<string, ColorPick>(), Boosts = new Dictionary<string, List<string>>(), Paths = new Dictionary<string, string>(), Hidden = new List<string>(), Pinned = new List<string> { Tablero.Game.Fortnite.Id, Tablero.Game.Valorant.Id }, Music = Sound.Themes[0], MusicVolume = 30, Sfx = true, SfxVolume = 60, SoundSaved = true, Theme = "noche", CrossSaved = new List<string>(), OverlayKey = "Ctrl+Shift+O", MarkKey = "Ctrl+Shift+M", OverlayShow = OverlayModel.Defaults() }; }
        static string FileOf(string dir) { return Path.Combine(dir, "ajustes.json"); }
        // Anything not understood goes back to its default value.
        public static Prefs Parse(string json)
        {
            Prefs p = null;
            try { p = new JavaScriptSerializer().Deserialize<Prefs>(json); }
            catch (Exception) { }
            Prefs d = Default();
            if (p == null) return d;
            if (p.Confirm < 5 || p.Confirm > 30) p.Confirm = d.Confirm; // an old "Page" is simply ignored
            if (p.Motion < 0 || p.Motion > 2) p.Motion = d.Motion;
            if (p.HeavyKeep < 0 || p.HeavyKeep > 2) p.HeavyKeep = 0;
            if (p.HeavyApps != null) p.HeavyApps = p.HeavyApps.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!p.SoundSaved) { p.Music = d.Music; p.MusicMode = d.MusicMode; p.MusicVolume = d.MusicVolume; p.Sfx = d.Sfx; p.SfxVolume = d.SfxVolume; p.SoundSaved = true; }
            if (p.Music != "" && !Sound.Themes.Contains(p.Music)) p.Music = d.Music;
            if (p.MusicMode < 0 || p.MusicMode > 2) p.MusicMode = d.MusicMode;
            p.MusicVolume = Math.Max(0, Math.Min(100, p.MusicVolume));
            p.SfxVolume = Math.Max(0, Math.Min(100, p.SfxVolume));
            // Settings saved before themes existed, or not understood: Simple, the look the app already had.
            p.Theme = Tablero.Theme.LookOf(p.Theme).Id;
            p.Game = Tablero.Game.Of(p.Game).Id;
            CrossFix(p);
            p.OverlayKey = Hotkeys.Clean(p.OverlayKey, d.OverlayKey);
            p.MarkKey = Hotkeys.Clean(p.MarkKey, d.MarkKey);
            if (p.OverlayStyle < 0 || p.OverlayStyle > 2) p.OverlayStyle = d.OverlayStyle;
            if (p.OverlayCorner < 0 || p.OverlayCorner > 3) p.OverlayCorner = d.OverlayCorner;
            p.OverlayShow = p.OverlayShow == null ? d.OverlayShow : p.OverlayShow.Where(x => OverlayModel.Ids.Contains(x)).Distinct().ToList();
            Dictionary<string, ColorPick> had = p.Colors ?? d.Colors;
            p.Colors = d.Colors;
            foreach (Game g in Tablero.Game.All) { ColorPick c; if (had.TryGetValue(g.Id, out c)) p.Colors[g.Id] = Vivid.Clean(c); }
            Dictionary<string, List<string>> bo = p.Boosts ?? d.Boosts;
            p.Boosts = d.Boosts;
            foreach (Game g in Tablero.Game.All) { List<string> l; if (bo.TryGetValue(g.Id, out l) && l != null) p.Boosts[g.Id] = l.Where(x => Boost.All.Any(b => b.Id == x)).Distinct().ToList(); }
            Dictionary<string, string> pa = p.Paths ?? d.Paths;
            p.Paths = d.Paths;
            foreach (Game g in Tablero.Game.All) { string s; if (pa.TryGetValue(g.Id, out s) && g.IsExe(s)) p.Paths[g.Id] = s; }
            // At least one game always stays in the selector, and the one on screen is one of them.
            p.Hidden = (p.Hidden ?? d.Hidden).Where(x => Tablero.Game.All.Any(g => g.Id == x)).Distinct().ToList();
            if (p.Hidden.Count >= Tablero.Game.All.Count) p.Hidden = d.Hidden;
            if (p.Hidden.Contains(p.Game)) p.Game = Tablero.Game.All.First(g => !p.Hidden.Contains(g.Id)).Id;
            // Settings from before pinning keep Fortnite and Valorant at the top.
            p.Pinned = Tablero.Game.Slots(p.Pinned ?? d.Pinned, Tablero.Game.All.Where(g => !p.Hidden.Contains(g.Id)).ToList()).Select(g => g.Id).ToList();
            return p;
        }
        public List<string> BoostsOf(Game g) { List<string> l; return Boosts.TryGetValue(g.Id, out l) ? l : new List<string>(); }
        public ColorPick ColorOf(Game g) { ColorPick c; return Colors.TryGetValue(g.Id, out c) ? c : Vivid.All[0].Pick; }
        public static void Load(string dir)
        {
            Now = Default();
            try { if (File.Exists(FileOf(dir))) Now = Parse(File.ReadAllText(FileOf(dir))); else if (Directory.Exists(Path.Combine(dir, "guardados"))) Now.Theme = "simple"; } // updating without settings saved
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Fx.Level = Now.Motion;
        }
        public string Json() { return new JavaScriptSerializer().Serialize(this); }
        public static void Save(string dir) { Directory.CreateDirectory(dir); Engine.WriteFile(FileOf(dir), Now.Json()); }

        // ---- Export and import ----
        public class Bundle
        {
            public string App { get; set; }
            public Prefs Prefs { get; set; }
            public MacroConfig Macros { get; set; }
            public string Palette { get; set; }
            public Dictionary<string, string> Palettes { get; set; } // per game
            public string Language { get; set; }
        }
        static string PaletteIn(string dir, Game g)
        {
            string f = Path.Combine(dir, g.PaletteFile), s = "";
            try { if (File.Exists(f)) s = File.ReadAllText(f).Trim(); }
            catch (IOException) { }
            return Tablero.Theme.All.Any(p => p.Name == s) ? s : g.Palette;
        }
        public static Palette PaletteOf(string dir, Game g) { string n = PaletteIn(dir, g); return Tablero.Theme.All.First(p => p.Name == n); }
        public static string Export(string dir)
        {
            return new JavaScriptSerializer().Serialize(new Bundle { App = "Tweakeroo", Prefs = Now, Macros = Tablero.Macros.Load(dir), Palettes = Tablero.Game.All.ToDictionary(g => g.Id, g => PaletteIn(dir, g)), Language = L.En ? "en" : "es" });
        }
        // Only known and already validated values get in. The macro risk is accepted on each PC.
        public static void Import(string dir, string json)
        {
            Bundle b = null;
            try { b = new JavaScriptSerializer().Deserialize<Bundle>(json); }
            catch (Exception) { }
            if (b == null || b.App != "Tweakeroo" || b.Prefs == null) throw new InvalidDataException("El archivo no es una copia de ajustes de Tweakeroo.");
            Directory.CreateDirectory(dir);
            Now = Parse(b.Prefs.Json());
            Save(dir);
            MacroConfig m = Tablero.Macros.Clean(b.Macros);
            m.Accepted = false;
            Tablero.Macros.Save(dir, m);
            // Copies older than 4.0 have a single palette, Fortnite's.
            Dictionary<string, string> pal = b.Palettes ?? new Dictionary<string, string> { { Tablero.Game.Fortnite.Id, b.Palette } };
            foreach (Game g in Tablero.Game.All) { string n; if (pal.TryGetValue(g.Id, out n) && Tablero.Theme.All.Any(p => p.Name == n)) File.WriteAllText(Path.Combine(dir, g.PaletteFile), n); }
            if (b.Language == "en" || b.Language == "es") L.Save(dir, b.Language == "en");
        }
    }

    // Start with Windows: a task at sign-in, because the app needs administrator rights.
    public static class Autostart
    {
        public static string Name = "Tweakeroo";
        public static Func<bool> Read = () =>
        {
            try { return (bool)Folder().GetTask(Name).Enabled; }
            catch (IOException) { return false; }
            catch (COMException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        };
        public static Action<bool> Write = on =>
        {
            dynamic f = Folder();
            if (!on)
            {
                try { f.DeleteTask(Name, 0); }
                catch (IOException) { } // did not exist
                return;
            }
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            dynamic d = Service().NewTask(0);
            d.Principal.RunLevel = 1;
            d.Principal.LogonType = 3;
            d.Principal.UserId = user;
            d.Settings.DisallowStartIfOnBatteries = false;
            d.Settings.StopIfGoingOnBatteries = false;
            d.Settings.ExecutionTimeLimit = "PT0S";
            dynamic t = d.Triggers.Create(9);
            t.UserId = user;
            dynamic a = d.Actions.Create(0);
            a.Path = Installer.Exe;
            a.Arguments = "--inicio";
            f.RegisterTaskDefinition(Name, d, 6, null, null, 3);
        };
        static dynamic Service()
        {
            dynamic s = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            s.Connect();
            return s;
        }
        static dynamic Folder() { return Service().GetFolder("\\"); }
    }

    public class VersionRow
    {
        public string Version { get; set; }
        public string Date { get; set; }
        public List<string> Notes { get; set; }
        public bool Current { get; set; }
        public int Missing; // release notes without English

        // historial.txt: "## version date" and below it "- Spanish TAB English", newest to oldest.
        public static List<VersionRow> All()
        {
            List<VersionRow> r = new List<VersionRow>();
            string text;
            using (StreamReader s = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("historial.txt"))) text = s.ReadToEnd();
            foreach (string l in text.Replace("\r", "").Split('\n'))
            {
                if (l.StartsWith("## "))
                {
                    string[] h = l.Substring(3).Split(' ');
                    r.Add(new VersionRow { Version = h[0], Date = h.Length > 1 ? h[1] : "", Notes = new List<string>(), Current = h[0] == Installer.Short });
                }
                else if (l.StartsWith("- ") && r.Count > 0)
                {
                    string[] n = l.Substring(2).Split('\t');
                    if (n.Length < 2) r[r.Count - 1].Missing++;
                    r[r.Count - 1].Notes.Add(L.En && n.Length > 1 ? n[1] : n[0]);
                }
            }
            return r;
        }
    }
}
