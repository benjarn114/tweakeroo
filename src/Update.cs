using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tablero
{
    public class Release
    {
        public string Version { get; set; }
        public List<string> Notes { get; set; }
        public List<string> NotesEn { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
    }
    public class Signed
    {
        public string Data { get; set; } // the Release JSON in base64, which is what gets signed
        public string Signature { get; set; }
    }

    // Looks for a new version, verifies the author's signature and swaps the .exe. Without a valid signature nothing is installed.
    public static class Updater
    {
        const int MaxFeed = 64 * 1024, MaxExe = 64 * 1024 * 1024;
        static string Resource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                return s == null ? "" : new StreamReader(s).ReadToEnd().Trim();
        }
        // GitHub "user/repository"; empty = nowhere to look yet.
        public static string Channel = Resource("canal.txt");
        public static string PublicKey = Resource("clave-publica.xml");
        public static bool Ready { get { return Channel.Length > 0 && PublicKey.Length > 0; } }
        public static string Feed { get { return "https://github.com/" + Channel + "/releases/latest/download/update.json"; } }

        public static Func<string, int, byte[]> Download = (url, max) =>
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "Tweakeroo"; r.Timeout = 10000; r.ReadWriteTimeout = 30000;
            r.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            using (WebResponse a = r.GetResponse())
            using (Stream s = a.GetResponseStream())
            using (MemoryStream m = new MemoryStream())
            {
                if (a.ResponseUri.Scheme != "https") throw new InvalidDataException("La descarga no es segura.");
                byte[] b = new byte[81920]; int n;
                while ((n = s.Read(b, 0, b.Length)) > 0) { m.Write(b, 0, n); if (m.Length > max) throw new InvalidDataException("La descarga es demasiado grande."); }
                return m.ToArray();
            }
        };

        // Tag of the latest release, read from GitHub's redirect without following it; null if unknown.
        public static Func<string> LatestTag = () =>
        {
            try
            {
                HttpWebRequest q = (HttpWebRequest)WebRequest.Create("https://github.com/" + Channel + "/releases/latest");
                q.AllowAutoRedirect = false; q.Method = "HEAD"; q.UserAgent = "Tweakeroo"; q.Timeout = 10000;
                using (HttpWebResponse a = (HttpWebResponse)q.GetResponse())
                {
                    string l = a.Headers["Location"] ?? "";
                    int i = l.LastIndexOf("/tag/", StringComparison.Ordinal);
                    return i < 0 ? null : Uri.UnescapeDataString(l.Substring(i + 5));
                }
            }
            catch (WebException) { return null; }
        };

        public static Release Parse(string json, Version mine, string tag = null)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            Signed s = js.Deserialize<Signed>(json);
            byte[] data = Convert.FromBase64String(s.Data), sig = Convert.FromBase64String(s.Signature);
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(new CspParameters(24)))
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(PublicKey);
                if (!rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256"), sig)) throw new InvalidDataException("La firma de la actualización no es válida.");
            }
            Release r = js.Deserialize<Release>(Encoding.UTF8.GetString(data));
            if (!r.Url.StartsWith("https://", StringComparison.Ordinal)) throw new InvalidDataException("La actualización no usa una dirección segura.");
            Version v = Version.Parse(r.Version);
            Version now = new Version(mine.Major, mine.Minor, Math.Max(0, mine.Build));
            // Feed is /releases/latest, so the tag is only known via Url: ignore manifests pointing at another tag
            if (r.Url.IndexOf("/v" + r.Version + "/", StringComparison.Ordinal) < 0) return null;
            // An old manifest re-attached to a renamed release (v0.37.0 serving Version 3.7.0) is not an update.
            if (tag != null && tag != "v" + r.Version) return null;
            return v > now ? r : null;
        }
        public static Release Check() { return Parse(Encoding.UTF8.GetString(Download(Feed, MaxFeed)), Installer.MyVersion, LatestTag()); }

        // Puts the new version in place of the current one; if something fails, the current one comes back.
        public static void Apply(Release r, string exe)
        {
            byte[] b = Download(r.Url, MaxExe);
            string hash;
            using (SHA256 h = SHA256.Create()) hash = BitConverter.ToString(h.ComputeHash(b)).Replace("-", "");
            if (!string.Equals(hash, r.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("El archivo descargado no coincide con el firmado. No se aplicó ningún cambio.");
            string fresh = exe + ".nuevo", old = exe + ".viejo";
            File.WriteAllBytes(fresh, b);
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }
        }
        public static void Undo(string exe)
        {
            if (!File.Exists(exe + ".viejo")) return;
            if (File.Exists(exe)) File.Delete(exe);
            File.Move(exe + ".viejo", exe);
        }
    }

    class UpdateUi
    {
        readonly Window w;
        readonly Func<string> blocked;   // reason not to update now, or null
        T F<T>(string n) where T : class { return w.FindName(n) as T; }
        Release release;
        bool working;
        public static Action<string> Restart = exe => { Installer.Start(exe); Application.Current.Shutdown(); }; // when closing this way, the window does not hide

        public UpdateUi(Window w, Func<string> blocked)
        {
            this.w = w; this.blocked = blocked;
            F<Btn>("UpdateLater").Click += (s, e) => Hide();
            F<Btn>("UpdateNow").Click += async (s, e) => await Go();
            F<FrameworkElement>("UpdateBox").PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && !working) Hide(); };
            w.Closing += (s, e) => { if (working) e.Cancel = true; };
        }

        // Without internet or without a channel it does not bother: it checks again the next time it opens.
        // Returns what happened, or null if there is a new version and it is already shown.
        public async Task<string> Check()
        {
            if (!Updater.Ready) return "No hay dónde buscar actualizaciones.";
            Release r = null;
            string err = null;
            await Task.Run(() => { try { r = Updater.Check(); } catch (Exception ex) { err = ex.Message; } });
            if (r == null) return err != null ? "No se pudo buscar: " + err : "Es la versión más reciente.";
            Show(r);
            return null;
        }

        public void Show(Release r)
        {
            release = r;
            F<TextBlock>("UpdateTitle").Text = "Nueva versión disponible: " + r.Version;
            F<ItemsControl>("UpdateNotes").ItemsSource = (L.En && r.NotesEn != null && r.NotesEn.Count > 0 ? r.NotesEn : r.Notes) ?? new List<string>();
            F<TextBlock>("UpdateNote").Text = "Versión instalada: " + Installer.Short + ". Los guardados y ajustes se conservan.";
            FrameworkElement box = F<FrameworkElement>("UpdateBox");
            box.Visibility = Visibility.Visible;
            Fx.From(box, UIElement.OpacityProperty, 0, 1, 160, Fx.Out, 0);
            Fx.Pop(F<FrameworkElement>("UpdateCard"), 0.9);
            F<Btn>("UpdateNow").Focus();
        }
        void Hide() { if (!working) F<FrameworkElement>("UpdateBox").Visibility = Visibility.Collapsed; }

        async Task Go()
        {
            if (working) return;
            string why = blocked();
            if (why != null) { F<TextBlock>("UpdateNote").Text = why; return; }
            working = true;
            Btn b = F<Btn>("UpdateNow");
            b.IsEnabled = F<Btn>("UpdateLater").IsEnabled = false;
            b.Content = "Descargando…";
            string err = null, exe = Installer.Me;
            await Task.Run(() => { try { Updater.Apply(release, exe); } catch (Exception ex) { err = ex.Message; } });
            working = false;
            if (err == null)
            {
                try { Restart(exe); }
                catch (Exception ex) { err = ex.Message; try { Updater.Undo(exe); } catch (Exception) { } }
            }
            b.IsEnabled = F<Btn>("UpdateLater").IsEnabled = true;
            b.Content = err == null ? "Actualizar ahora" : "Reintentar";
            if (err != null) F<TextBlock>("UpdateNote").Text = "No se actualizó: " + err;
        }
    }
}
