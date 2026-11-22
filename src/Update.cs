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
        public string Data { get; set; } // el JSON de Release en base64, que es lo que va firmado
        public string Signature { get; set; }
    }

    // Busca una version nueva, comprueba la firma del autor y cambia el .exe. Sin firma valida no se instala nada.
    public static class Updater
    {
        const int MaxFeed = 64 * 1024, MaxExe = 64 * 1024 * 1024;
        static string Resource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                return s == null ? "" : new StreamReader(s).ReadToEnd().Trim();
        }
        // "usuario/repositorio" de GitHub; vacio = todavia no hay donde buscar.
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

        public static Release Parse(string json, Version mine)
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
            return v > now ? r : null;
        }
        public static Release Check() { return Parse(Encoding.UTF8.GetString(Download(Feed, MaxFeed)), Installer.MyVersion); }

        // Deja la version nueva en el sitio de la actual; si algo falla, vuelve la actual.
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
        readonly Func<string> blocked;   // motivo para no actualizar ahora, o null
        T F<T>(string n) where T : class { return w.FindName(n) as T; }
        Release release;
        bool working;
        public static Action<string> Restart = exe => { Installer.Start(exe); Application.Current.Shutdown(); };

        public UpdateUi(Window w, Func<string> blocked)
        {
            this.w = w; this.blocked = blocked;
            F<Btn>("UpdateLater").Click += (s, e) => Hide();
            F<Btn>("UpdateNow").Click += async (s, e) => await Go();
            F<FrameworkElement>("UpdateBox").PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && !working) Hide(); };
            w.Closing += (s, e) => { if (working) e.Cancel = true; };
        }

        // Sin internet o sin canal no molesta: se vuelve a mirar la proxima vez que se abra.
        public async Task Check()
        {
            if (!Updater.Ready) return;
            Release r = null;
            await Task.Run(() => { try { r = Updater.Check(); } catch (Exception) { } });
            if (r != null) Show(r);
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
