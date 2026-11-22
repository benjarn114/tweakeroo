using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Tablero
{
    public class Hw
    {
        public string Cpu = "", Gpu = "", DiskName = "", NetName = "";
        public int Cores, Threads, RamGb, RamMhz, GpuCount, VramGb, Hz, DiskFreeGb;
        public bool Nvidia, Ssd, Nvme, Wifi, Ethernet, Laptop;
        // Solo los llena Review.Read; null o 0 = no se pudo leer.
        public int RamModules, Ddr, MaxHz, ScreenW, ScreenH;
        public string Socket = "", Board = "";
        public Hw With(string gpu, string cpu) { Hw h = (Hw)MemberwiseClone(); h.Gpu = gpu; h.Cpu = cpu; return h; }
        public DateTime DriverDate;
        public bool? SecureBoot, Tpm2, Iommu;
        public bool Dual3d { get { return Regex.IsMatch(Cpu, @"\b(79[05]0|99[05]0)X3D", RegexOptions.IgnoreCase); } }
        public bool Weak { get { return RamGb <= 8 || Cores <= 4; } }
    }

    // Lo que se guarda de un escaneo para no repetirlo al abrir. Text = lo que enseña cada tarjeta.
    public class ScanData
    {
        public Hw Hw { get; set; }
        public Dictionary<string, string> Text { get; set; }
    }

    // Detecta los componentes y decide que conviene. Cada paso llena una parte de Hw.
    public static class Scanner
    {
        public class Step { public string Key; public Func<Hw, string> Run; }
        static IEnumerable<ManagementObject> Q(string cls, string scope = @"root\cimv2")
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(scope, "SELECT * FROM " + cls))
                foreach (ManagementObject o in s.Get()) yield return o;
        }
        static int Int(object v) { return v == null ? 0 : Convert.ToInt32(v); }

        public static readonly List<Step> Steps = new List<Step>
        {
            new Step { Key = "Cpu", Run = h => {
                ManagementObject c = Q("Win32_Processor").First();
                h.Cpu = Regex.Replace(((string)c["Name"]).Trim(), @"\s*\d+-Core Processor|\(R\)|\(TM\)|CPU @.*$", "").Trim();
                h.Cores = Int(c["NumberOfCores"]); h.Threads = Int(c["NumberOfLogicalProcessors"]);
                h.Laptop = Q("Win32_Battery").Any();
                return h.Cpu + "\n" + h.Cores + " núcleos, " + h.Threads + " hilos" + (h.Laptop ? ", portátil" : ""); } },
            new Step { Key = "Gpu", Run = h => {
                List<string> names = Q("Win32_VideoController").Select(g => new { n = (string)g["Name"], id = (string)g["PNPDeviceID"] })
                    .Where(g => g.id != null && g.id.StartsWith("PCI", StringComparison.OrdinalIgnoreCase)).Select(g => g.n).ToList();
                h.GpuCount = names.Count;
                h.Gpu = names.OrderByDescending(n => Regex.IsMatch(n, "NVIDIA|GeForce|RTX")).ThenByDescending(n => Regex.IsMatch(n, "RX|Arc")).FirstOrDefault() ?? "Sin gráfica dedicada";
                h.Nvidia = h.Gpu.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0;
                h.VramGb = Vram(h.Gpu);
                return h.Gpu.Replace("NVIDIA ", "") + "\n" + (h.VramGb > 0 ? h.VramGb + " GB de vídeo" : "Memoria de vídeo sin leer") + (h.GpuCount > 1 ? ", " + h.GpuCount + " gráficas" : ""); } },
            new Step { Key = "Ram", Run = h => {
                List<ManagementObject> m = Q("Win32_PhysicalMemory").ToList();
                h.RamGb = (int)(m.Sum(x => Convert.ToInt64(x["Capacity"])) / (1L << 30));
                h.RamMhz = m.Max(x => Int(x["ConfiguredClockSpeed"]));
                return h.RamGb + " GB\n" + h.RamMhz + " MHz, " + m.Count + (m.Count == 1 ? " módulo" : " módulos"); } },
            new Step { Key = "Disk", Run = h => {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                h.DiskFreeGb = (int)(new DriveInfo(root).AvailableFreeSpace / (1L << 30));
                ManagementObject d = Q("MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage").OrderByDescending(x => Int(x["BusType"]) == 17).First();
                h.DiskName = ((string)d["FriendlyName"]).Trim();
                h.Nvme = Int(d["BusType"]) == 17; h.Ssd = h.Nvme || Int(d["MediaType"]) == 4;
                return h.DiskName + "\n" + (h.Nvme ? "NVMe" : h.Ssd ? "SSD" : "Disco duro") + ", " + h.DiskFreeGb + " GB libres en " + root.TrimEnd('\\'); } },
            new Step { Key = "Net", Run = h => {
                // Solo cuentan tarjetas fisicas con puerta de enlace: descarta VPN y adaptadores virtuales.
                HashSet<string> pci = new HashSet<string>(Q("Win32_NetworkAdapter").Where(a => a["PNPDeviceID"] != null && Regex.IsMatch((string)a["PNPDeviceID"], @"^(PCI|USB)\\")).Select(a => (string)a["GUID"] ?? ""), StringComparer.OrdinalIgnoreCase);
                foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up || !pci.Contains(n.Id) || n.GetIPProperties().GatewayAddresses.Count == 0) continue;
                    if (n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) h.Wifi = true; else h.Ethernet = true;
                    h.NetName = n.Description;
                }
                return (h.Ethernet ? "Por cable" : h.Wifi ? "Por Wi-Fi" : "Sin conexión") + "\n" + h.NetName; } },
            new Step { Key = "Screen", Run = h => {
                h.Hz = Q("Win32_VideoController").Max(g => Int(g["CurrentRefreshRate"]));
                return h.Hz + " Hz\n" + System.Windows.SystemParameters.PrimaryScreenWidth + " × " + System.Windows.SystemParameters.PrimaryScreenHeight; } },
        };

        // Todo de una vez y sin pantalla; null si falla algo.
        public static ScanData Run()
        {
            ScanData d = new ScanData { Hw = new Hw(), Text = new Dictionary<string, string>() };
            try { foreach (Step s in Steps) d.Text[s.Key] = s.Run(d.Hw); }
            catch (Exception) { return null; }
            Review.Read(d.Hw);
            return d;
        }
        static string FileOf(string dir) { return Path.Combine(dir, "scan.json"); }
        // Si no se puede guardar, la proxima vez se escanea otra vez.
        public static void Save(string dir, ScanData d)
        {
            try { Engine.WriteFile(FileOf(dir), new JavaScriptSerializer().Serialize(d)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public static void Delete(string dir) { File.Delete(FileOf(dir)); }
        // null = no hay escaneo guardado o no sirve.
        public static ScanData Load(string dir)
        {
            ScanData d;
            try { d = File.Exists(FileOf(dir)) ? new JavaScriptSerializer().Deserialize<ScanData>(File.ReadAllText(FileOf(dir))) : null; }
            catch (Exception) { return null; }
            if (d == null || d.Hw == null || d.Text == null) return null;
            Hw h = d.Hw;
            if (Steps.Any(s => !d.Text.ContainsKey(s.Key) || d.Text[s.Key] == null) || new[] { h.Cpu, h.Gpu, h.DiskName, h.NetName, h.Socket, h.Board }.Contains(null)) return null;
            h.DriverDate = h.DriverDate.Year < 1990 ? DateTime.MinValue : h.DriverDate.ToLocalTime();
            return d;
        }

        static int Vram(string gpu)
        {
            const string cls = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\";
            for (int i = 0; i < 10; i++)
            {
                string k = cls + i.ToString("0000");
                try
                {
                    if (RegOp.ReadValue(k, "DriverDesc") != "s:" + gpu) continue;
                    string v = RegOp.ReadValue(k, "HardwareInformation.qwMemorySize");
                    if (v.StartsWith("q:")) return (int)Math.Round(ulong.Parse(v.Substring(2)) / (double)(1L << 30));
                }
                catch (System.Security.SecurityException) { }
            }
            return 0;
        }
    }

    public class Advice { public bool Yes; public string Why; }

    public static class Advisor
    {
        static Advice A(bool yes, string why) { return new Advice { Yes = yes, Why = why }; }

        public static Advice For(Tweak t, Hw h)
        {
            if (t.Level == Level.Riesgo) return A(false, "Con riesgo: activación solo manual.");
            switch (t.Id)
            {
                case "net-eee": case "net-green": case "net-giga":
                    return h.Ethernet ? A(true, "Conexión por cable.") : A(false, "Sin conexión por cable.");
                case "net-im": return h.Ethernet && h.Cores >= 6 ? A(true, "Conexión por cable y CPU con margen.") : A(false, h.Ethernet ? "CPU con poco margen." : "Sin conexión por cable.");
                case "pw-wifi": return h.Wifi && !h.Ethernet ? A(true, "Conexión por Wi-Fi.") : A(false, "Sin conexión por Wi-Fi.");
                case "pw-plan": case "pw-park":
                    if (h.Dual3d) return A(false, "La CPU X3D doble necesita el plan Equilibrado.");
                    return h.Laptop ? A(false, "En un portátil gasta batería y calienta.") : A(true, "PC de mesa con " + h.Cores + " núcleos.");
                case "pw-timer": return A(false, "Opcional: su efecto en el retraso no está demostrado.");
                case "pw-min": return A(false, "La CPU ya sube de frecuencia sola; solo daría más calor.");
                case "pw-boost": case "pw-epp": case "pw-cool": case "pw-aspm": case "pw-usb": case "pw-usb3": case "pw-sleep": case "pw-disk": case "pw-throttle":
                    return h.Laptop ? A(false, "En un portátil gasta batería.") : A(true, "PC de mesa: no hay batería que cuidar.");
                case "fn-gpu": return h.GpuCount > 1 ? A(true, "Detectadas " + h.GpuCount + " gráficas.") : A(false, "Una sola gráfica.");
                case "fn-reflex": return h.Nvidia ? A(true, "Compatible con la gráfica NVIDIA.") : A(false, "Solo funciona con NVIDIA.");
                case "fn-texture": return h.VramGb > 0 && h.VramGb < 6 ? A(true, "Detectados " + h.VramGb + " GB de vídeo.") : A(false, h.VramGb > 0 ? "Con " + h.VramGb + " GB de vídeo apenas cambia los FPS." : "No se pudo leer la memoria de vídeo.");
                case "fn-viewdistance": return h.Cores < 6 ? A(true, "Alivia la CPU de " + h.Cores + " núcleos.") : A(false, "Los " + h.Cores + " núcleos son suficientes.");
                case "gfx-vrr": return A(false, "Solo con monitor G-Sync o FreeSync; no se puede detectar.");
                case "nv-shader": return h.Ssd && h.DiskFreeGb >= 60 ? A(true, "SSD con " + h.DiskFreeGb + " GB libres.") : A(false, "Falta espacio o velocidad de disco.");
                case "sv-sysmain": return h.Ssd && h.RamGb >= 16 ? A(true, (h.Nvme ? "NVMe" : "SSD") + " y " + h.RamGb + " GB: precarga innecesaria.") : A(false, "Con este disco y memoria todavía ayuda.");
                case "bg-apps": return h.RamGb < 16 ? A(true, "Con " + h.RamGb + " GB conviene liberar memoria.") : A(false, "Con " + h.RamGb + " GB no es necesario.");
                case "ui-startup": return h.Ssd ? A(true, "El disco es rápido.") : A(false, "En un disco duro haría más lento el arranque.");
                case "ui-menu": return A(true, "Se nota en cualquier PC.");
            }
            if (t.Cat == "Interfaz") return h.Weak ? A(true, "Un escritorio ligero ayuda en este PC.") : A(false, "Innecesario en este PC.");
            if (t.Level == Level.Avanzado) return A(false, "Opcional: quita una función de Windows.");
            return A(true, t.Cat == "Fortnite" ? "Mejor respuesta en partida." : t.Cat.StartsWith("Gráficos") ? "Adecuado para esta gráfica." : "Seguro en cualquier PC.");
        }

        // Frases con lo que decidio el escaneo.
        public static List<string> Notes(Hw h)
        {
            List<string> n = new List<string>();
            if (h.Wifi && !h.Ethernet) n.Add("Conexión por Wi-Fi: se quita el ahorro de la antena y se omiten los ajustes de cable. Un cable de red ayuda más que cualquier tweak.");
            if (h.Ethernet) n.Add("Conexión por cable: se quitan los ahorros de energía de la tarjeta de red.");
            n.Add(h.Dual3d ? "CPU X3D doble: se mantiene el plan Equilibrado, que usa los núcleos correctos." : h.Laptop ? "Portátil: se omite lo que gasta batería." : "PC de mesa: energía en rendimiento.");
            if (h.Nvidia) n.Add("Gráfica NVIDIA: se incluyen Reflex y el perfil de máximo rendimiento para Fortnite.");
            n.Add(h.Weak ? "Equipo justo: también se aligera el escritorio." : "Equipo potente: la apariencia de Windows no cambia, no aporta FPS.");
            return n;
        }
    }
}
