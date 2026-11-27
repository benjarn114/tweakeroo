using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Tablero
{
    public class Crash
    {
        public DateTime When;
        public string Game, Cause, Action, Raw, Folder;
        public string Evidence = "", Top, Exe; // what was read; Top is the first module that is not Windows itself
    }

    // Why the game closed: the crash reports the game leaves and the Windows event log. Read only, nothing is deleted or changed.
    public static class Crashes
    {
        public const int Days = 30;
        public const string Unknown = "Sin causa clara";
        const string Driver = "El driver de la gráfica dejó de responder";

        // In order: the first that matches wins.
        static readonly string[][] Causes =
        {
            new[] { "Sin memoria de vídeo", "Bajar texturas o resolución.", @"out of video memory|insufficient video memory|outofvideomemory" },
            new[] { "Sin memoria del sistema", "Cerrar programas pesados; no desactivar el archivo de paginación.", @"bisoom\W+(1|true)|ran out of memory|out of memory|c0000017|paging file|pagefile" },
            new[] { "El anticheat cerró el juego", "Reinstalar el anticheat desde el launcher.", @"easyanticheat|easy anti-cheat|eac_?launcher|\bvgk\b|vgc\.exe|vanguard|battleye|beclient" },
        };
        static readonly string[][] Overlays =
        {
            new[] { @"discord", "Discord" }, new[] { @"rtss|rivatuner", "RivaTuner" }, new[] { @"afterburner", "MSI Afterburner" },
            new[] { @"gameoverlayrenderer", "Steam" }, new[] { @"overwolf|owclient", "Overwolf" }, new[] { @"graphics-hook|obs-", "OBS" },
            new[] { @"nvspcap|nvidia share", "NVIDIA" }, new[] { @"medal", "Medal" }, new[] { @"igo64|igo32", "EA app" }, new[] { @"gamebar", "Xbox Game Bar" },
        };
        const string GpuLost = @"dxgi_error_device_(removed|hung|reset)|887a000[567]|device (removed|lost|hung)|gpu crash|\.nv-gpudmp|nvlddmkm|display 4101|livekernelevent (141|117)|nvwgf2um|nvd3dum|nvoglv|amdxx64|atidxx64|amdxc64|atiumd|igd1\d|igxel";
        static readonly HashSet<string> System32 = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ntdll.dll", "kernelbase.dll", "kernel32.dll", "ucrtbase.dll", "vcruntime140.dll", "msvcp140.dll", "user32.dll", "win32u.dll", "unknown" };

        static bool Has(string text, string re) { return Regex.IsMatch(text, re, RegexOptions.IgnoreCase); }

        public static void Classify(Crash c)
        {
            string e = c.Evidence + " " + (c.Top ?? "");
            c.Raw = string.Join(" · ", new[] { c.Raw, c.Top }.Where(x => !string.IsNullOrEmpty(x)));
            foreach (string[] k in Causes) if (Has(e, k[2])) { c.Cause = k[0]; c.Action = k[1]; return; }
            foreach (string[] o in Overlays) if (Has(e, o[0])) { c.Cause = "Fallo en el overlay de " + o[1]; c.Action = "Probar sin el overlay de " + o[1] + "."; return; }
            if (Has(e, GpuLost)) { c.Cause = Driver; c.Action = "Actualizar o reinstalar limpio el driver; quitar overclock de la gráfica si lo hay."; return; }
            if (Has(e, @"c0000005|access_violation") && c.Top != null && c.Exe != null && c.Top.Equals(c.Exe, StringComparison.OrdinalIgnoreCase))
            { c.Cause = "Error de memoria dentro del juego"; c.Action = "Verificar los archivos del juego en su launcher."; return; }
            c.Cause = Unknown;
            c.Action = null;
        }

        static string TopModule(string text)
        {
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(text ?? "", @"([\w\-.]+\.(?:dll|exe|sys))!", RegexOptions.IgnoreCase))
                if (!System32.Contains(m.Groups[1].Value)) return m.Groups[1].Value;
            return null;
        }
        static string Tag(string xml, string tag)
        {
            System.Text.RegularExpressions.Match m = Regex.Match(xml, "<" + Regex.Escape(tag) + @">([\s\S]*?)</" + Regex.Escape(tag) + ">");
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : "";
        }
        // The last bytes of a file the game may still hold open.
        static string Tail(string file, int max)
        {
            using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long n = Math.Min(max, f.Length);
                f.Seek(-n, SeekOrigin.End);
                byte[] b = new byte[n];
                int got = f.Read(b, 0, (int)n);
                return Encoding.UTF8.GetString(b, 0, got).Replace("\0", "");
            }
        }

        // Unreal crash folders (Fortnite, Valorant): one subfolder per crash, newer than since.
        public static List<Crash> Folders(string root, string game, string exe, DateTime since)
        {
            List<Crash> r = new List<Crash>();
            if (!Directory.Exists(root)) return r;
            foreach (string d in Directory.GetDirectories(root))
            {
                try
                {
                    DateTime when = Directory.GetLastWriteTime(d);
                    if (when < since) continue;
                    string file = Path.Combine(d, "CrashContext.runtime-xml");
                    string ctx = File.Exists(file) ? Tail(file, 512 * 1024) : "";
                    if (Tag(ctx, "IsEnsure") == "true" || Tag(ctx, "CrashType") == "Ensure") continue; // ensures do not close the game
                    string msg = Tag(ctx, "ErrorMessage"), stack = Tag(ctx, "CallStack"), type = Tag(ctx, "CrashType");
                    StringBuilder ev = new StringBuilder(type + "\n" + msg + "\n" + stack + "\nbisoom " + Tag(ctx, "MemoryStats.bIsOOM") + "\n");
                    foreach (string f in Directory.GetFiles(d))
                    {
                        ev.Append(Path.GetFileName(f)).Append('\n');
                        if (!f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                        // Only the fatal lines: a log mentions overlays and drivers in normal lines too.
                        foreach (string line in Tail(f, 256 * 1024).Split('\n'))
                            if (Has(line, @"fatal error|unhandled exception|exception_|dxgi_error|out of video memory|out of memory|gpu crash|device (removed|lost|hung)")) ev.Append(line).Append('\n');
                    }
                    r.Add(new Crash { When = when, Game = game, Exe = exe, Folder = d, Evidence = ev.ToString(), Top = TopModule(msg + "\n" + stack), Raw = type.Length > 0 ? type : null });
                }
                catch (Exception) { } // unreadable: not shown
            }
            return r;
        }

        static List<EventRecord> Query(string log, string filter, DateTime since)
        {
            List<EventRecord> r = new List<EventRecord>();
            long ms = Math.Max(0, (long)(DateTime.Now - since).TotalMilliseconds);
            try
            {
                using (EventLogReader rd = new EventLogReader(new EventLogQuery(log, PathType.LogName, "*[System[" + filter + " and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]")))
                    for (EventRecord e = rd.ReadEvent(); e != null; e = rd.ReadEvent()) r.Add(e);
            }
            catch (Exception) { }
            return r;
        }
        static string Prop(EventRecord e, int i) { try { return i < e.Properties.Count && e.Properties[i].Value != null ? e.Properties[i].Value.ToString() : ""; } catch (Exception) { return ""; } }

        // Application Error 1000 of a game exe; markers: driver resets (4101) and WER LiveKernelEvent 141/117.
        static void Events(DateTime since, List<Crash> apps, List<KeyValuePair<DateTime, string>> markers)
        {
            Game[] games = { Game.Fortnite, Game.Valorant, Game.Cs2 };
            foreach (EventRecord e in Query("Application", "Provider[@Name='Application Error'] and (EventID=1000)", since))
                using (e)
                {
                    string app = Prop(e, 0);
                    Game g = games.FirstOrDefault(x => x.ExeName.Equals(app, StringComparison.OrdinalIgnoreCase));
                    if (g == null || e.TimeCreated == null) continue;
                    string module = Prop(e, 3), code = Prop(e, 6);
                    apps.Add(new Crash { When = e.TimeCreated.Value, Game = g.Name, Exe = g.ExeName, Top = System32.Contains(module) ? null : module, Evidence = code + " " + module, Raw = code.Length > 0 ? "0x" + code : null });
                }
            foreach (EventRecord e in Query("System", "(EventID=4101)", since))
                using (e) if (e.TimeCreated != null) markers.Add(new KeyValuePair<DateTime, string>(e.TimeCreated.Value, "display 4101"));
            foreach (EventRecord e in Query("Application", "Provider[@Name='Windows Error Reporting'] and (EventID=1001)", since))
                using (e) if (e.TimeCreated != null && Prop(e, 2) == "LiveKernelEvent") markers.Add(new KeyValuePair<DateTime, string>(e.TimeCreated.Value, "livekernelevent " + Prop(e, 5)));
        }

        // One entry per closure (a report and its event within 3 minutes are the same), with the driver markers of the 10 minutes before.
        public static List<Crash> Merge(List<Crash> folders, List<Crash> apps, List<KeyValuePair<DateTime, string>> markers)
        {
            List<Crash> all = folders.ToList();
            foreach (Crash a in apps)
            {
                Crash same = all.FirstOrDefault(x => x.Game == a.Game && Math.Abs((x.When - a.When).TotalMinutes) <= 3);
                if (same == null) all.Add(a);
                else { same.Evidence += "\n" + a.Evidence; if (same.Top == null) same.Top = a.Top; }
            }
            foreach (Crash c in all)
            {
                foreach (KeyValuePair<DateTime, string> m in markers)
                    if (m.Key >= c.When.AddMinutes(-10) && m.Key <= c.When.AddMinutes(2)) c.Evidence += "\n" + m.Value;
                Classify(c);
            }
            return all.OrderByDescending(c => c.When).ToList();
        }

        // CS2 leaves its minidumps next to cs2.exe; a dump from the same minutes is the report folder.
        static void Cs2Dumps(List<Crash> all)
        {
            string dir = null;
            try { string exe = Game.Cs2.Exe; if (!string.IsNullOrEmpty(exe)) dir = Path.GetDirectoryName(exe); } catch (Exception) { }
            if (dir == null || !Directory.Exists(dir)) return;
            List<DateTime> dumps;
            try { dumps = Directory.GetFiles(dir, "*.mdmp").Select(File.GetLastWriteTime).ToList(); } catch (Exception) { return; }
            foreach (Crash c in all.Where(x => x.Game == Game.Cs2.Name && x.Folder == null))
                if (dumps.Any(t => Math.Abs((t - c.When).TotalMinutes) <= 5)) c.Folder = dir;
        }

        public static Func<List<Crash>> Source = Read; // the screenshot build swaps in samples

        public static List<Crash> Recent() { try { return Source(); } catch (Exception) { return new List<Crash>(); } }

        static List<Crash> Read()
        {
            DateTime since = DateTime.Now.AddDays(-Days);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            List<Crash> folders = Folders(Path.Combine(local, @"FortniteGame\Saved\Crashes"), Game.Fortnite.Name, Game.Fortnite.ExeName, since);
            folders.AddRange(Folders(Path.Combine(local, @"VALORANT\Saved\Crashes"), Game.Valorant.Name, Game.Valorant.ExeName, since));
            List<Crash> apps = new List<Crash>();
            List<KeyValuePair<DateTime, string>> markers = new List<KeyValuePair<DateTime, string>>();
            Events(since, apps, markers);
            List<Crash> all = Merge(folders, apps, markers);
            Cs2Dumps(all);
            return all;
        }

        // Every text the card and the chat can show, for the translation check.
        public static IEnumerable<string> Texts()
        {
            foreach (string[] k in Causes) { yield return k[0]; yield return k[1]; }
            yield return Driver; yield return "Actualizar o reinstalar limpio el driver; quitar overclock de la gráfica si lo hay.";
            yield return "Error de memoria dentro del juego"; yield return "Verificar los archivos del juego en su launcher.";
            yield return Unknown; yield return "Fallo en el overlay de Discord"; yield return "Probar sin el overlay de Discord.";
        }
    }
}
