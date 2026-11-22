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
        public string Page { get; set; }
        public int Motion { get; set; }   // 0 todas, 1 sin rebotes, 2 apagadas
        public int Confirm { get; set; }  // segundos para confirmar una resolucion
        public bool AutoCheck { get; set; }
        public bool WifiSteady { get; set; }

        public static readonly string[] PageNames = { "Jugar", "Mi PC", "Mis FPS", "Conexión", "Macros", "Mejoras" };
        public static readonly int[] Seconds = { 10, 15, 30 };
        public static Prefs Now = Default();

        public static Prefs Default() { return new Prefs { Page = PageNames[0], Confirm = 15, AutoCheck = true }; }
        static string FileOf(string dir) { return Path.Combine(dir, "ajustes.json"); }
        // Lo que no se entiende vuelve a su valor normal.
        public static Prefs Parse(string json)
        {
            Prefs p = null;
            try { p = new JavaScriptSerializer().Deserialize<Prefs>(json); }
            catch (Exception) { }
            Prefs d = Default();
            if (p == null) return d;
            if (!PageNames.Contains(p.Page)) p.Page = d.Page;
            if (!Seconds.Contains(p.Confirm)) p.Confirm = d.Confirm;
            if (p.Motion < 0 || p.Motion > 2) p.Motion = d.Motion;
            return p;
        }
        public static void Load(string dir)
        {
            Now = Default();
            try { if (File.Exists(FileOf(dir))) Now = Parse(File.ReadAllText(FileOf(dir))); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Fx.Level = Now.Motion;
        }
        public string Json() { return new JavaScriptSerializer().Serialize(this); }
        public static void Save(string dir) { Directory.CreateDirectory(dir); Engine.WriteFile(FileOf(dir), Now.Json()); }

        // ---- Exportar e importar ----
        public class Bundle
        {
            public string App { get; set; }
            public Prefs Prefs { get; set; }
            public MacroConfig Macros { get; set; }
            public string Palette { get; set; }
            public string Language { get; set; }
        }
        public static string Export(string dir, string palette)
        {
            return new JavaScriptSerializer().Serialize(new Bundle { App = "Tweakeroo", Prefs = Now, Macros = Tablero.Macros.Load(dir), Palette = palette, Language = L.En ? "en" : "es" });
        }
        // Solo entra lo conocido y ya revisado. El riesgo de las macros se acepta en cada PC.
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
            if (Theme.All.Any(p => p.Name == b.Palette)) File.WriteAllText(Path.Combine(dir, "paleta.txt"), b.Palette);
            if (b.Language == "en" || b.Language == "es") L.Save(dir, b.Language == "en");
        }
    }

    // Abrir con Windows: una tarea al iniciar sesion, porque la app necesita permisos de administrador.
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
                catch (IOException) { } // no existia
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
        public int Missing; // novedades sin ingles

        // historial.txt: "## version fecha" y debajo "- español TAB ingles", de la mas nueva a la mas vieja.
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
