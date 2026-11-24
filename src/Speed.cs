using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Tablero
{
    public class SpeedResult
    {
        public string Date { get; set; }
        public double Down { get; set; }   // Mbps
        public double Up { get; set; }
        public int Idle { get; set; }      // unloaded ping
        public int Loaded { get; set; }    // the worst of download and upload
    }

    // Speed with Cloudflare's public servers: several downloads at once for a few seconds.
    public static class SpeedTest
    {
        const string Base = "https://speed.cloudflare.com/";
        const int Lines = 6, WarmMs = 1500, Seconds = 7;
        const long DownCap = 500L << 20, UpCap = 250L << 20; // data cap per test

        public class Meter
        {
            public long Bytes, AtWarm = -1;
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            long warmMs;
            // The ramp-up does not count: connections take a while to reach speed.
            public void Add(int n)
            {
                long total = Interlocked.Add(ref Bytes, n);
                if (Clock.ElapsedMilliseconds >= WarmMs && Interlocked.CompareExchange(ref AtWarm, total, -1) == -1) warmMs = Clock.ElapsedMilliseconds;
            }
            public double Mbps(long nowMs)
            {
                double ms = nowMs - warmMs;
                return AtWarm < 0 || ms < 200 ? 0 : (Interlocked.Read(ref Bytes) - AtWarm) * 8 / 1000.0 / ms;
            }
        }

        static HttpWebRequest Request(string path)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 16);
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(Base + path);
            r.UserAgent = "Tweakeroo"; r.Timeout = 8000; r.ReadWriteTimeout = 8000; r.ServicePoint.Expect100Continue = false; r.ServicePoint.ConnectionLimit = 16;
            r.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            return r;
        }
        static void Get(Meter m, Func<bool> go)
        {
            HttpWebRequest r = Request("__down?bytes=25000000");
            using (WebResponse a = r.GetResponse())
            using (Stream s = a.GetResponseStream())
            {
                byte[] b = new byte[1 << 16]; int n;
                while (go() && (n = s.Read(b, 0, b.Length)) > 0) m.Add(n);
                r.Abort();
            }
        }
        static void Post(Meter m, Func<bool> go)
        {
            const int size = 10000000;
            HttpWebRequest r = Request("__up");
            r.Method = "POST"; r.ContentType = "application/octet-stream"; r.ContentLength = size; r.AllowWriteStreamBuffering = false;
            byte[] b = new byte[1 << 15];
            int left = size;
            using (Stream s = r.GetRequestStream())
                while (left > 0)
                {
                    if (!go()) { r.Abort(); return; }
                    int n = Math.Min(left, b.Length);
                    s.Write(b, 0, n); m.Add(n); left -= n;
                }
            using (r.GetResponse()) { }
        }

        // Replaceable in tests.
        public static Func<bool, Action<double>, double> Measure = (down, step) =>
        {
            Meter m = new Meter();
            long end = Seconds * 1000, cap = down ? DownCap : UpCap;
            Func<bool> go = () => m.Clock.ElapsedMilliseconds < end && Interlocked.Read(ref m.Bytes) < cap;
            string err = null;
            int failed = 0;
            Thread[] t = Enumerable.Range(0, Lines).Select(i => new Thread(() =>
            {
                while (go())
                {
                    try { if (down) Get(m, go); else Post(m, go); }
                    catch (WebException ex) { if (!go()) return; err = ex.Message; Interlocked.Increment(ref failed); return; }
                    catch (IOException ex) { if (!go()) return; err = ex.Message; Interlocked.Increment(ref failed); return; }
                }
            }) { IsBackground = true }).ToArray();
            foreach (Thread x in t) x.Start();
            while (t.Any(x => x.IsAlive) && m.Clock.ElapsedMilliseconds < end + 300) { Thread.Sleep(300); if (step != null) step(m.Mbps(m.Clock.ElapsedMilliseconds)); }
            double r = m.Mbps(Math.Min(m.Clock.ElapsedMilliseconds, end));
            if (r <= 0) throw new InvalidOperationException(err ?? "La prueba no recibió datos.");
            return r;
        };

        // Downloads, uploads and checks how much ping rises with the connection saturated. Runs off the UI thread.
        public static async Task<SpeedResult> Run(Action<string, double> step)
        {
            SpeedResult r = new SpeedResult { Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
            r.Idle = (await NetTest.Run(NetTest.Internet, 10, 100, null)).Median;
            foreach (bool down in new[] { true, false })
            {
                bool done = false;
                List<int> pings = new List<int>();
                Task watch = Task.Run(async () => { while (!done) { int p = await NetTest.Ping(NetTest.Internet, 1000); if (p >= 0) pings.Add(p); await Task.Delay(150); } });
                double v;
                try { v = await Task.Run(() => Measure(down, x => step(down ? "Down" : "Up", x))); }
                finally { done = true; }
                await watch;
                if (down) r.Down = v; else r.Up = v;
                step(down ? "Down" : "Up", v);
                r.Loaded = Math.Max(r.Loaded, PingStats.Of(pings).Median);
            }
            return r;
        }

        public static string Verdict(SpeedResult r)
        {
            int up = r.Idle > 0 && r.Loaded > 0 ? Math.Max(0, r.Loaded - r.Idle) : -1;
            string a = r.Down < 10 || r.Up < 2 ? "La velocidad es baja: otra descarga en casa puede llenarla y dar tirones." : "Para jugar sobra velocidad; lo que importa es el ping.";
            return up < 0 ? a : a + (up >= 30 ? " Con la conexión llena el ping sube " + up + " ms: descargas o subidas de cualquier equipo de la casa dan tirones. Un router con control de colas (SQM o QoS) lo reduce."
                                              : " Con la conexión llena el ping sube solo " + up + " ms, así que una descarga no debería dar tirones.");
        }

        static string FileOf(string dir) { return Path.Combine(dir, "velocidad.json"); }
        public static SpeedResult Load(string dir)
        {
            try { return File.Exists(FileOf(dir)) ? new JavaScriptSerializer().Deserialize<SpeedResult>(File.ReadAllText(FileOf(dir))) : null; }
            catch (Exception) { return null; }
        }
        public static void Save(string dir, SpeedResult r)
        {
            try { Engine.WriteFile(FileOf(dir), new JavaScriptSerializer().Serialize(r)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
