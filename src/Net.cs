using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Tablero
{
    // Result of a batch of pings to a host. Ms = -1 is a lost packet.
    public class PingStats
    {
        public int Sent { get; set; }
        public int Lost { get; set; }
        public int Median { get; set; }
        public int Max { get; set; }
        public double Jitter { get; set; }  // how much it changes from one packet to the next
        public int Spikes { get; set; }     // packets 30 ms above normal
        public double Loss { get { return Sent == 0 ? 0 : 100.0 * Lost / Sent; } }
        public bool Silent { get { return Sent > 0 && Lost == Sent; } }

        public static PingStats Of(IList<int> ms)
        {
            PingStats r = new PingStats { Sent = ms.Count, Lost = ms.Count(x => x < 0) };
            List<int> ok = ms.Where(x => x >= 0).ToList();
            if (ok.Count == 0) return r;
            r.Max = ok.Max();
            r.Median = ok.OrderBy(x => x).ElementAt(ok.Count / 2);
            r.Jitter = ok.Count < 2 ? 0 : ok.Skip(1).Select((x, i) => (double)Math.Abs(x - ok[i])).Average();
            r.Spikes = ok.Count(x => x > r.Median + 30);
            return r;
        }
        public bool Bad { get { return Loss >= 1 || Jitter >= 8 || Spikes >= 3; } }
    }

    public class NetResult
    {
        public string Date { get; set; }
        public PingStats Router { get; set; }
        public PingStats Internet { get; set; }
        public PingStats Epic { get; set; }
        public string Region { get; set; }
        public bool Wifi { get; set; }
    }

    public static class NetTest
    {
        // Epic measurement servers, by region.
        public static readonly string[,] Regions = { { "br", "Brasil" }, { "nae", "Norteamérica este" }, { "nac", "Norteamérica centro" }, { "naw", "Norteamérica oeste" }, { "eu", "Europa" }, { "me", "Oriente Medio" }, { "asia", "Asia" }, { "oce", "Oceanía" } };
        public const string Internet = "1.1.1.1";
        public static string Host(string region) { return "ping-" + region + ".ds.on.epicgames.com"; }

        // Replaceable in tests. -1 = lost.
        public static Func<string, int, Task<int>> Ping = async (host, timeout) =>
        {
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply r = await p.SendPingAsync(host, timeout);
                    return r.Status == IPStatus.Success ? (int)r.RoundtripTime : -1;
                }
            }
            catch (PingException) { return -1; }
            catch (SocketException) { return -1; }
        };
        public static Func<string> Router = () =>
        {
            foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                GatewayIPAddressInformation g = n.GetIPProperties().GatewayAddresses.FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !x.Address.Equals(IPAddress.Any));
                if (g != null) return g.Address.ToString();
            }
            return null;
        };

        // The region with the lowest ping among those that answer; null if none.
        public static async Task<string> Nearest()
        {
            string best = null;
            int low = int.MaxValue;
            List<string> ids = Enumerable.Range(0, Regions.GetLength(0)).Select(i => Regions[i, 0]).ToList();
            Task<int>[] t = ids.Select(async id =>
            {
                int a = await Ping(Host(id), 1000), b = await Ping(Host(id), 1000);
                return a < 0 ? b : b < 0 ? a : Math.Min(a, b);
            }).ToArray();
            int[] ms = await Task.WhenAll(t);
            for (int i = 0; i < ms.Length; i++) if (ms[i] >= 0 && ms[i] < low) { low = ms[i]; best = ids[i]; }
            return best;
        }
        public static string Name(string region)
        {
            for (int i = 0; i < Regions.GetLength(0); i++) if (Regions[i, 0] == region) return Regions[i, 1];
            return "";
        }

        // One host: one ping every 200 ms; step reports progress.
        public static async Task<PingStats> Run(string host, int count, int gap, Action<PingStats> step)
        {
            List<int> ms = new List<int>();
            for (int i = 0; i < count; i++)
            {
                Stopwatch w = Stopwatch.StartNew();
                ms.Add(host == null ? -1 : await Ping(host, 1000));
                if (step != null && i % 5 == 4) step(PingStats.Of(ms));
                int rest = gap - (int)w.ElapsedMilliseconds;
                if (rest > 0) await Task.Delay(rest);
            }
            return PingStats.Of(ms);
        }

        // Where the problem is, from inside out.
        public static string Verdict(NetResult r)
        {
            string home = Trouble(r);
            if (home != null) return home;
            if (r.Epic.Silent) return "Los servidores de Epic no contestan a la prueba. El resto de la conexión va bien.";
            if (r.Epic.Bad) return "La red de casa y el proveedor van bien; las pérdidas o picos están en la ruta hasta Epic.";
            if (r.Epic.Median > 80) return "Conexión estable. El ping alto viene de la distancia al servidor: " + r.Epic.Median + " ms.";
            return "Conexión estable.";
        }
        // No game servers to measure.
        public static string Home(NetResult r) { return r.Internet.Silent ? "Sin respuesta de internet. Comprueba la conexión." : Trouble(r) ?? "Conexión estable."; }
        static string Trouble(NetResult r)
        {
            if (r.Internet.Silent && r.Epic.Silent) return "Sin respuesta de internet. Comprueba la conexión.";
            if (!r.Router.Silent && r.Router.Bad)
                return r.Wifi ? "El problema empieza en el Wi-Fi: hay pérdidas o picos hasta el router. Un cable, o acercarse al router, es lo que más ayuda."
                              : "El problema empieza en el cable o el router: hay pérdidas o picos dentro de casa. Prueba otro cable u otro puerto.";
            if (!r.Internet.Silent && r.Internet.Bad)
                return (r.Router.Silent ? "El router no contesta a la prueba, así que puede ser tu red o tu proveedor." : "La red de casa va bien; las pérdidas o picos están en el proveedor de internet.") + " Cierra descargas y, si sigue, avisa al proveedor con estos datos.";
            return null;
        }

        public static string Compare(NetResult old, NetResult now)
        {
            if (old == null) return "";
            return "Prueba anterior (" + old.Date.Substring(0, 16) + "): " + old.Epic.Median + " ms, variación " + Num(old.Epic.Jitter) + " ms, pérdida " + Num(old.Epic.Loss) + " %.";
        }
        public static string Num(double v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        static string FileOf(string dir) { return Path.Combine(dir, "red.json"); }
        public static NetResult Load(string dir)
        {
            try
            {
                NetResult r = File.Exists(FileOf(dir)) ? new JavaScriptSerializer().Deserialize<NetResult>(File.ReadAllText(FileOf(dir))) : null;
                return r == null || r.Epic == null || r.Router == null || r.Internet == null || r.Date == null || r.Date.Length < 16 ? null : r;
            }
            catch (Exception) { return null; }
        }
        public static void Save(string dir, NetResult r)
        {
            try { Engine.WriteFile(FileOf(dir), new JavaScriptSerializer().Serialize(r)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Programs that often download or upload without warning.
        static readonly string[] Heavy = { "OneDrive", "GoogleDriveFS", "Dropbox", "steam", "qbittorrent", "uTorrent", "BitTorrent", "Battle.net", "EADesktop", "Medal", "obs64" };
        public static List<string> Busy() { return Heavy.Where(n => { Process[] p = Process.GetProcessesByName(n); foreach (Process x in p) x.Dispose(); return p.Length > 0; }).ToList(); }
    }

    // Wi-Fi through the Windows API. Changes last while the app keeps the session open: when released it reverts by itself.
    public static class Wifi
    {
        [DllImport("wlanapi.dll")] static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern uint WlanSetInterface(IntPtr handle, ref Guid id, int op, int size, ref int value, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, int op, IntPtr reserved, out int size, out IntPtr data, IntPtr type);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);
        const int Scan = 2, Stream = 3, Current = 7, Channel = 8, Connected = 1;

        static List<Guid> Cards(IntPtr h, bool connected)
        {
            List<Guid> r = new List<Guid>();
            IntPtr list;
            if (WlanEnumInterfaces(h, IntPtr.Zero, out list) != 0) return r;
            try
            {
                int n = Marshal.ReadInt32(list);
                for (int i = 0; i < n; i++)
                {
                    IntPtr p = new IntPtr(list.ToInt64() + 8 + i * 532); // GUID, 256-character name and state
                    if (!connected || Marshal.ReadInt32(p, 528) == Connected) r.Add((Guid)Marshal.PtrToStructure(p, typeof(Guid)));
                }
            }
            finally { WlanFreeMemory(list); }
            return r;
        }

        static IntPtr held = IntPtr.Zero;
        public static bool On { get { return held != IntPtr.Zero; } }
        // Without scanning for other networks and in streaming mode. Replaceable in tests.
        public static Func<bool, bool> Steady = want =>
        {
            if (want == On) return true;
            if (!want) { WlanCloseHandle(held, IntPtr.Zero); held = IntPtr.Zero; return true; }
            uint v;
            IntPtr h;
            if (WlanOpenHandle(2, IntPtr.Zero, out v, out h) != 0) return false;
            bool any = false;
            foreach (Guid g in Cards(h, true))
            {
                Guid id = g;
                int off = 0, yes = 1;
                any |= WlanSetInterface(h, ref id, Scan, 4, ref off, IntPtr.Zero) == 0;
                any |= WlanSetInterface(h, ref id, Stream, 4, ref yes, IntPtr.Zero) == 0;
            }
            if (!any) { WlanCloseHandle(h, IntPtr.Zero); return false; }
            held = h;
            return true;
        };

        // Signal (0-100) and channel of the current connection; false if there is no Wi-Fi.
        public static bool Info(out int signal, out int channel)
        {
            signal = 0; channel = 0;
            uint v;
            IntPtr h;
            try { if (WlanOpenHandle(2, IntPtr.Zero, out v, out h) != 0) return false; }
            catch (DllNotFoundException) { return false; }
            try
            {
                foreach (Guid g in Cards(h, true))
                {
                    Guid id = g;
                    int size;
                    IntPtr data;
                    if (WlanQueryInterface(h, ref id, Current, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0) continue;
                    // state, mode, profile (512) + SSID (36) + type + BSSID (6, aligned to 8) + PHY type and index = 576
                    signal = size >= 580 ? Marshal.ReadInt32(data, 576) : 0;
                    WlanFreeMemory(data);
                    if (WlanQueryInterface(h, ref id, Channel, IntPtr.Zero, out size, out data, IntPtr.Zero) == 0) { channel = Marshal.ReadInt32(data); WlanFreeMemory(data); }
                    return true;
                }
                return false;
            }
            finally { WlanCloseHandle(h, IntPtr.Zero); }
        }

        // Tips based on the signal, the band and what is open.
        public static List<Check> Advice(bool wifi, int signal, int channel, List<string> busy)
        {
            List<Check> r = new List<Check>();
            if (wifi)
            {
                r.Add(new Check { Kind = Kind.Gratis, Title = "Conexión por cable recomendada", Text = "El Wi-Fi provoca picos de ping que ningún tweak elimina. Sin cable, es mejor la banda de 5 o 6 GHz." });
                if (channel >= 1 && channel <= 14) r.Add(new Check { Kind = Kind.Gratis, Title = "Wi-Fi en 2,4 GHz", Text = "La banda de 5 GHz tiene menos interferencias. Si el router la ofrece, es mejor conectarse a ella." });
                else if (channel > 14) r.Add(new Check { Kind = Kind.Bien, Title = "Wi-Fi en 5 o 6 GHz", Text = "Es la banda con menos interferencias." });
                if (signal > 0 && signal < 70) r.Add(new Check { Kind = Kind.Gratis, Title = "Señal Wi-Fi: " + signal + " %", Text = "Por debajo del 70 % aparecen pérdidas. Acercar el PC o el router ayuda más que cualquier ajuste." });
                else if (signal >= 70) r.Add(new Check { Kind = Kind.Bien, Title = "Señal Wi-Fi: " + signal + " %", Text = "La señal es suficiente." });
            }
            else r.Add(new Check { Kind = Kind.Bien, Title = "Conexión por cable", Text = "Es la conexión más estable." });
            if (busy.Count > 0) r.Add(new Check { Kind = Kind.Gratis, Title = "Programas que pueden usar la red: " + string.Join(", ", busy), Text = "Una descarga o subida llena la conexión y sube el ping. Conviene cerrarlos o pausarlos antes de jugar." });
            else r.Add(new Check { Kind = Kind.Bien, Title = "Ningún programa conocido de descargas abierto", Text = "Si hay tirones, mira también otros equipos de la casa." });
            return r;
        }
    }
}
