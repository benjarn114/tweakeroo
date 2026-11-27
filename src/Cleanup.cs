using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;

namespace Tablero
{
    // One row of Cleanup: one or more folders whose contents Windows or the driver rebuild by themselves.
    public class CleanItem : Vm
    {
        public string Name { get; set; }
        public bool Warn { get; set; }          // shader cache: the first match can stutter
        public bool Aged;                      // temp files: only what has not changed in 24 h
        public List<string> Roots = new List<string>();
        public long Bytes;
        public int Files;
        public Action Changed;
        bool on;
        public bool On { get { return on; } set { on = value; Raise("On"); if (Changed != null) Changed(); } }
        public string SizeText { get { return Cleanup.Size(Bytes); } }
        public const string WarnTip = "La primera partida puede tener tirones mientras se rehace la caché.", TempTip = "Archivos temporales de más de un día. Se borra lo que no esté en uso.";
        public string Tip { get { return L.T(Warn ? WarnTip : TempTip); } } // a ToolTip does not translate itself
    }

    public class CleanStats { public long Bytes; public int Files, Left; }

    // Only this fixed list, only the contents (never the folders themselves), never through a link or junction.
    public static class Cleanup
    {
        public static readonly TimeSpan Age = TimeSpan.FromHours(24);

        // A folder under a base, rejecting it if it or any folder on the way is a link or junction.
        internal static string Safe(string bas, string rel)
        {
            if (string.IsNullOrEmpty(bas)) return null;
            string p = bas;
            foreach (string part in rel.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                p = Path.Combine(p, part);
                try { if (!Directory.Exists(p) || (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return null; }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
            }
            return p;
        }
        static string Parent(string dir) { return Path.GetDirectoryName(dir.TrimEnd('\\')); }
        static string Leaf(string dir) { return Path.GetFileName(dir.TrimEnd('\\')); }

        static List<CleanItem> Candidates()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), low = local + "Low";
            string temp = Path.GetTempPath(), win = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            Func<string, bool, bool, string[][], CleanItem> mk = (name, warn, aged, at) =>
            {
                CleanItem i = new CleanItem { Name = name, Warn = warn, Aged = aged, On = !warn };
                foreach (string[] x in at) { string r = Safe(x[0], x[1]); if (r != null) i.Roots.Add(r); }
                return i;
            };
            List<CleanItem> all = new List<CleanItem>
            {
                mk("Caché de sombreadores de DirectX", true, false, new[] { new[] { local, "D3DSCache" } }),
                mk("Caché de sombreadores de NVIDIA", true, false, new[] { new[] { local, @"NVIDIA\DXCache" }, new[] { local, @"NVIDIA\GLCache" }, new[] { low, @"NVIDIA\PerDriverVersion\DXCache" }, new[] { low, @"NVIDIA\PerDriverVersion\GLCache" } }),
                mk("Caché de sombreadores de AMD", true, false, new[] { new[] { local, @"AMD\DxCache" }, new[] { local, @"AMD\DxcCache" }, new[] { local, @"AMD\VkCache" }, new[] { local, @"AMD\GLCache" } }),
                mk("Caché de sombreadores de Intel", true, false, new[] { new[] { low, @"Intel\ShaderCache" } }),
                mk("Temporales del usuario", false, true, new[] { new[] { Parent(temp), Leaf(temp) } }),
                mk("Temporales de Windows", false, true, new[] { new[] { Parent(win), Leaf(win) } }),
            };
            // Running as SYSTEM, %TEMP% can be Windows\Temp itself: listed once.
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CleanItem i in all) i.Roots = i.Roots.Where(r => seen.Add(Path.GetFullPath(r).TrimEnd('\\'))).ToList();
            return all;
        }

        // Read only: what exists and weighs more than 0.
        public static List<CleanItem> Find()
        {
            List<CleanItem> r = new List<CleanItem>();
            foreach (CleanItem i in Candidates())
            {
                Measure(i);
                if (i.Bytes > 0) r.Add(i);
            }
            return r;
        }

        public static void Measure(CleanItem i)
        {
            CleanStats s = new CleanStats();
            DateTime? cut = i.Aged ? DateTime.UtcNow - Age : (DateTime?)null;
            foreach (string root in i.Roots) Walk(root, root, cut, false, s);
            i.Bytes = s.Bytes; i.Files = s.Files;
        }

        // Walks again with the same rules (nothing from the earlier search is trusted) and deletes. Files in use or without permission are skipped.
        public static void Clean(CleanItem i, CleanStats s)
        {
            DateTime? cut = i.Aged ? DateTime.UtcNow - Age : (DateTime?)null;
            foreach (string root in i.Roots) if (Safe(Path.GetDirectoryName(root), Path.GetFileName(root)) == root) Walk(root, root, cut, true, s); // it may have become a link since the search
        }

        internal static bool Inside(string root, string path)
        {
            string r = Path.GetFullPath(root).TrimEnd('\\') + "\\", p;
            try { p = Path.GetFullPath(path); } catch (Exception) { return false; }
            return p.Length > r.Length && p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        static void Walk(string root, string dir, DateTime? cut, bool delete, CleanStats s)
        {
            List<FileSystemInfo> list;
            try { list = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            catch (SecurityException) { return; }
            foreach (FileSystemInfo e in list)
            {
                try
                {
                    if ((e.Attributes & FileAttributes.ReparsePoint) != 0 || !Inside(root, e.FullName)) continue;
                    DirectoryInfo sub = e as DirectoryInfo;
                    if (sub != null)
                    {
                        bool old = cut == null || sub.LastWriteTimeUtc < cut.Value; // read before emptying it changes the date
                        Walk(root, sub.FullName, cut, delete, s);
                        if (delete && old) try { sub.Delete(false); } catch (IOException) { } catch (UnauthorizedAccessException) { } // only if it was left empty
                        continue;
                    }
                    FileInfo f = (FileInfo)e;
                    if (cut != null && f.LastWriteTimeUtc >= cut.Value) continue;
                    long len = f.Length;
                    if (delete)
                    {
                        try { f.Delete(); }
                        catch (IOException) { s.Left++; continue; }
                        catch (UnauthorizedAccessException) { s.Left++; continue; }
                    }
                    s.Bytes += len; s.Files++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (SecurityException) { }
            }
        }

        public static string Size(long b)
        {
            if (b <= 0) return "0 MB";
            double mb = b / 1048576.0;
            string s = mb >= 1024 ? (mb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : Math.Max(1, Math.Round(mb)).ToString("0", CultureInfo.InvariantCulture) + " MB";
            return L.En ? s : s.Replace('.', ',');
        }

        public static string Freed(CleanStats s)
        {
            return "Liberados " + Size(s.Bytes) + "." + (s.Left == 0 ? "" : s.Left == 1 ? " 1 archivo en uso se dejó." : " " + s.Left + " archivos en uso se dejaron.");
        }

        public static string Ask(IEnumerable<CleanItem> sel)
        {
            List<CleanItem> l = sel.ToList();
            return L.T("Se borra, sin pasar por la Papelera:") + "\n" + string.Join("\n", l.Select(i => "• " + L.T(i.Name) + ": " + Size(i.Bytes))) + "\n\n" + L.T("Total: " + Size(l.Sum(i => i.Bytes))) + "\n\n" + L.T("¿Limpiar ahora?");
        }

        // Texts built in code, for the English check of the self-test.
        public static IEnumerable<string> Texts()
        {
            List<string> r = Candidates().Select(i => i.Name).ToList();
            r.AddRange(new[] { CleanItem.WarnTip, CleanItem.TempTip, "Calculando…", "Limpiando…", "Buscar qué limpiar", "Nada que limpiar.", "Cierra el juego antes de limpiar.", "Limpiar 1,2 GB", "Total seleccionado: 1,2 GB",
                "Se borra, sin pasar por la Papelera:", "Total: 3 MB", "¿Limpiar ahora?" });
            r.Add(Freed(new CleanStats { Bytes = 5 << 20 })); r.Add(Freed(new CleanStats { Left = 1 })); r.Add(Freed(new CleanStats { Left = 34 }));
            return r;
        }

        // Self-test on a throwaway folder: size, the 24 h rule, links not followed, nothing outside the root, a locked file skipped.
        internal static void Test(Action<bool, string> Is, string dir)
        {
            string root = Path.Combine(dir, "limpieza", "raiz"), outside = Path.Combine(dir, "limpieza", "fuera"), link = Path.Combine(root, "union"), sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub); Directory.CreateDirectory(outside);
            DateTime old = DateTime.Now.AddDays(-2);
            Action<string, int, bool> put = (p, n, aged) => { File.WriteAllBytes(p, new byte[n]); if (aged) File.SetLastWriteTime(p, old); };
            put(Path.Combine(root, "viejo.tmp"), 1000, true);
            put(Path.Combine(root, "nuevo.tmp"), 500, false);
            put(Path.Combine(sub, "viejo2.tmp"), 2000, true);
            put(Path.Combine(root, "bloqueado.tmp"), 300, true);
            put(Path.Combine(outside, "ajeno.tmp"), 4000, true);
            put(Path.Combine(dir, "limpieza", "vecino.tmp"), 700, true);
            Directory.SetLastWriteTime(sub, old);
            using (System.Diagnostics.Process mk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + outside + "\"") { CreateNoWindow = true, UseShellExecute = false })) mk.WaitForExit(10000);
            try
            {
                Is(Directory.Exists(link) && File.Exists(Path.Combine(link, "ajeno.tmp")), "limpieza: union de prueba creada");
                Is(Safe(root, "union") == null && Safe(Path.Combine(dir, "limpieza"), "raiz") == root, "limpieza: no se entra a una raiz que es union");
                Is(!Inside(root, root) && !Inside(root, root + "x\\a") && !Inside(root, Path.Combine(root, @"..\fuera\ajeno.tmp")) && Inside(root, Path.Combine(sub, "viejo2.tmp")), "limpieza: dentro de la raiz");
                CleanItem aged = new CleanItem { Name = "x", Aged = true, Roots = { root } }, all = new CleanItem { Name = "x", Roots = { root } };
                Measure(aged); Measure(all);
                Is(aged.Bytes == 3300 && aged.Files == 3, "limpieza: tamaño con la regla de 24 h: " + aged.Bytes);
                Is(all.Bytes == 3800 && all.Files == 4, "limpieza: tamaño de una cache sin seguir la union: " + all.Bytes);
                CleanStats s = new CleanStats();
                using (new FileStream(Path.Combine(root, "bloqueado.tmp"), FileMode.Open, FileAccess.Read, FileShare.None)) Clean(aged, s);
                Is(s.Bytes == 3000 && s.Files == 2 && s.Left == 1, "limpieza: borra lo viejo y salta el bloqueado: " + s.Bytes + ", " + s.Left);
                Is(!File.Exists(Path.Combine(root, "viejo.tmp")) && !Directory.Exists(sub) && File.Exists(Path.Combine(root, "nuevo.tmp")) && File.Exists(Path.Combine(root, "bloqueado.tmp")), "limpieza: regla de 24 h al borrar");
                Is(Directory.Exists(root) && Directory.Exists(link) && File.Exists(Path.Combine(outside, "ajeno.tmp")) && File.Exists(Path.Combine(dir, "limpieza", "vecino.tmp")), "limpieza: nada fuera de la raiz");
                CleanStats s2 = new CleanStats(); Clean(all, s2);
                Is(Directory.Exists(root) && Directory.Exists(link) && File.Exists(Path.Combine(outside, "ajeno.tmp")) && Directory.GetFileSystemEntries(root).Length == 1, "limpieza: la cache se vacia salvo la union, la raiz queda");
                Is(Size(0) == "0 MB" && Size(1) == "1 MB" && (L.En ? Size(1288490189) == "1.2 GB" : Size(1288490189) == "1,2 GB"), "limpieza: tamaños");
            }
            finally { if (Directory.Exists(link)) Directory.Delete(link); }
        }
    }
}
