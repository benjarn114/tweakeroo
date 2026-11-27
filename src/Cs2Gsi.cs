// CS2's maps through Game State Integration, and the FPS of each CS2 session saved in "Mis FPS".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Tablero
{
    public class MapSpan { public string Map; public DateTime From, To; }

    // Game State Integration (an official Valve feature): with gamestate_integration_tweakeroo.cfg in its cfg folder,
    // CS2 posts its state to 127.0.0.1 while it runs. Only the map is asked for; nothing of the game is read or touched.
    public sealed class Cs2Gsi : IDisposable
    {
        public const int Port = 3121;
        const int Limit = 64 * 1024;
        const string FileName = "gamestate_integration_tweakeroo.cfg";
        static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
            { "de_dust2", "Dust II" }, { "de_mirage", "Mirage" }, { "de_inferno", "Inferno" }, { "de_nuke", "Nuke" }, { "de_ancient", "Ancient" },
            { "de_anubis", "Anubis" }, { "de_vertigo", "Vertigo" }, { "de_overpass", "Overpass" }, { "de_train", "Train" } };

        readonly string token;
        readonly TcpListener listener;
        readonly object gate = new object();
        readonly List<MapSpan> spans = new List<MapSpan>();
        string map;    // null = menu
        DateTime from;
        bool heard;    // a post with the token arrived
        public string Phase { get; private set; } // map.phase of the last post: warmup, live, intermission, gameover
        public static Cs2Gsi Now; // the one of the CS2 session in progress (Cs2Fps), for Tracker; null without one

        // Where the player is now: the map's name, "" in the menu, null until the game posts.
        public string Map { get { lock (gate) return !heard ? null : map == null ? "" : Pretty(map); } }

        // Only on 127.0.0.1. Throws SocketException if the port is taken: the session then goes on without maps.
        public Cs2Gsi(string token, int port = Port)
        {
            this.token = token;
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            new Thread(Serve) { IsBackground = true, Name = "GSI" }.Start();
        }
        public int LocalPort { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }

        void Serve()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }          // Dispose stopped the listener
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
                // Whatever a local client sends, only that request is lost: an exception here would end the app.
                try { using (c) Answer(c); }
                catch (Exception) { }
            }
        }

        // One POST per connection: headers, then Content-Length bytes of JSON. Always answers 200.
        void Answer(TcpClient c)
        {
            c.ReceiveTimeout = c.SendTimeout = 2000;
            NetworkStream s = c.GetStream();
            byte[] b = new byte[Limit];
            int n = 0, body = -1, length = 0;
            while (n < b.Length && (body < 0 || n - body < length))
            {
                int got = s.Read(b, n, b.Length - n);
                if (got <= 0) break;
                n += got;
                for (int i = 3; body < 0 && i < n; i++)
                    if (b[i - 3] == '\r' && b[i - 2] == '\n' && b[i - 1] == '\r' && b[i] == '\n') body = i + 1;
                if (body >= 0 && length == 0) int.TryParse(Regex.Match(Encoding.ASCII.GetString(b, 0, body), @"(?im)^Content-Length:[ \t]*(\d{1,9})\s*$").Groups[1].Value, out length);
            }
            if (body >= 0 && length > 0 && n - body >= length) Post(Encoding.UTF8.GetString(b, body, length), DateTime.UtcNow);
            byte[] ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            s.Write(ok, 0, ok.Length);
        }

        static string Text(Dictionary<string, object> d, string a, string b)
        {
            object o;
            Dictionary<string, object> x = d.TryGetValue(a, out o) ? o as Dictionary<string, object> : null;
            return x != null && x.TryGetValue(b, out o) ? o as string : null;
        }

        // A post from the game at that time; false if it is not JSON or lacks the token. Without "map" the player is in the menu.
        // A match lasts from a map until the map changes or the menu comes back.
        internal bool Post(string body, DateTime utc)
        {
            Dictionary<string, object> d = null;
            try { d = new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string, object>; }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            if (d == null || Text(d, "auth", "token") != token) return false;
            string name = Text(d, "map", "name");
            if (name == "") name = null;
            lock (gate)
            {
                heard = true;
                Phase = Text(d, "map", "phase");
                if (name == map) return true;
                if (map != null) spans.Add(new MapSpan { Map = Pretty(map), From = from, To = utc });
                map = name;
                from = utc;
            }
            return true;
        }

        // The maps played until end, the open one included; null if the game never posted in this session.
        public List<MapSpan> Spans(DateTime end)
        {
            lock (gate)
            {
                if (!heard) return null;
                List<MapSpan> r = spans.ToList();
                if (map != null) r.Add(new MapSpan { Map = Pretty(map), From = from, To = end });
                return r;
            }
        }

        // de_dust2 -> Dust II; an unknown one without its prefix: cs_office -> Office.
        public static string Pretty(string name)
        {
            string p;
            if (Names.TryGetValue(name, out p)) return p;
            string s = name.Substring(name.IndexOf('_') + 1);
            return s.Length == 0 ? name : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        public void Dispose() { listener.Stop(); }

        // ---- The .cfg in the game folder ----

        // ...\game\bin\win64\cs2.exe -> ...\game\csgo\cfg
        static string Folder(string exe) { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(exe), @"..\..\csgo\cfg")); }

        // CS2 sends this back in every post, so no other local program can feed maps. Kept in the app folder.
        public static string Token(string dir)
        {
            string f = Path.Combine(dir, "cs2-gsi.txt");
            try { string t = File.ReadAllText(f).Trim(); if (Regex.IsMatch(t, "^[0-9a-f]{32}$")) return t; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            byte[] b = new byte[16];
            using (RandomNumberGenerator r = RandomNumberGenerator.Create()) r.GetBytes(b);
            string n = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            Directory.CreateDirectory(dir);
            File.WriteAllText(f, n);
            return n;
        }

        static string Cfg(string token)
        {
            return "\"Tweakeroo\"\r\n{\r\n\t\"uri\" \"http://127.0.0.1:" + Port + "/\"\r\n\t\"timeout\" \"5.0\"\r\n\t\"buffer\" \"0.1\"\r\n\t\"throttle\" \"0.5\"\r\n\t\"heartbeat\" \"10.0\"\r\n"
                + "\t\"auth\"\r\n\t{\r\n\t\t\"token\" \"" + token + "\"\r\n\t}\r\n\t\"data\"\r\n\t{\r\n\t\t\"provider\" \"1\"\r\n\t\t\"map\" \"1\"\r\n\t}\r\n}\r\n";
        }

        // CS2 reads it when it starts: written only with CS2 closed, only if installed, and only if missing or different.
        public static void Ensure(string dir)
        {
            string exe = Game.Cs2.Exe;
            if (Game.Cs2.Running() || !File.Exists(exe) || !Directory.Exists(Folder(exe))) return;
            string f = Path.Combine(Folder(exe), FileName), want = Cfg(Token(dir));
            if (!File.Exists(f) || File.ReadAllText(f) != want) File.WriteAllText(f, want);
        }

        // On uninstall, with CS2 closed. A file left behind only makes CS2 post to a closed port, so a failure is not an error.
        public static void Remove()
        {
            try
            {
                string f = Path.Combine(Folder(Game.Cs2.Exe), FileName);
                if (!Game.Cs2.Running() && File.Exists(f)) File.Delete(f);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // "Mis FPS" of CS2: while cs2 runs, frames by ETW and maps by GSI; when it closes, its matches go to fps-cs2.json.
    public sealed class Cs2Fps : IDisposable
    {
#if SHOT
        static readonly bool Real = false; // test build: no ETW, no listener and nothing written in the game folder
#else
        static readonly bool Real = true;
#endif
        readonly string dir;
        readonly object gate = new object();
        FpsRecorder rec;
        Cs2Gsi gsi;
        DateTime start;
        bool ensured, gone;

        public Cs2Fps(string dir)
        {
            this.dir = dir;
            // A crash leaves the ETW session running with nobody reading it.
            if (Real) EtwSession.Stop(FpsRecorder.Session);
        }

        // Every 2 s, off the UI thread. Returns CS2's whole history when a session has just been saved, else null.
        public List<Match> Tick()
        {
            lock (gate)
            {
                if (gone) return null;
                Process[] ps = Process.GetProcessesByName("cs2");
                int[] pids = ps.Select(p => p.Id).ToArray();
                foreach (Process p in ps) p.Dispose();
                if (pids.Length > 0)
                {
                    if (rec == null) Begin();
                    rec.Watch(pids);
                    return null;
                }
                List<Match> r = rec == null ? null : End();
                if (!ensured && Real)
                {
                    ensured = true;
                    // Best effort, retried after the next session: this tick must never stop the app's game checks.
                    try { Cs2Gsi.Ensure(dir); } catch (Exception) { }
                }
                return r;
            }
        }

        void Begin()
        {
            start = DateTime.UtcNow;
            ensured = false;
            rec = Real ? FpsRecorder.Live() : new FpsRecorder();
            if (!Real) return;
            try { Cs2Gsi.Now = gsi = new Cs2Gsi(Cs2Gsi.Token(dir)); }
            catch (SocketException) { }       // port taken: the session is one "Partida"
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        List<Match> End()
        {
            DateTime end = DateTime.UtcNow;
            rec.Dispose(); // first: ETW stops and delivers what is left
            List<MapSpan> maps = gsi == null ? null : gsi.Spans(end);
            if (gsi != null) gsi.Dispose();
            Cs2Gsi.Now = null;
            List<Match> got = rec.Matches(start, maps, Cs2.FpsMax()), all = null;
            rec = null;
            gsi = null;
            try { foreach (Match m in got) all = FpsLog.Add(dir, Game.Cs2, m); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return all;
        }

        // When the app closes: the real-time session outlives the process, so it is always stopped. A session in progress is saved.
        public void Dispose()
        {
            lock (gate)
            {
                if (gone) return;
                gone = true;
                if (rec != null) End();
            }
        }
    }
}
