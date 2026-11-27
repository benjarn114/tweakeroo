using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace Tablero
{
    public enum Kind { Gratis, Comprar, Bien }

    public class Check
    {
        public string N { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public Kind Kind;
        public string Url;
        public Action Fix;    // fixes it right away; without it, the button opens Url
        public string Button; // button text if it is not the usual one
        public string Tag { get { return Kind == Kind.Gratis ? "Gratis" : Kind == Kind.Comprar ? "Compras opcionales" : "Correcto"; } }
        public bool Ok { get { return Kind == Kind.Bien; } }
        public bool Todo { get { return !Ok; } }
        public bool HasUrl { get { return Url != null || Fix != null; } }
        public string Act { get { return Button ?? (Fix != null ? "Arreglar" : Url != null && Url.StartsWith("https:") ? "Abrir web" : "Abrir ajustes"); } }
        public string Small { get { return Button ?? (Fix != null ? "Arreglar" : "Abrir"); } } // the short button of Help rows
        public bool HasAct { get { return HasUrl && !Ok; } }
        public string Detail { get; set; } // shown on hover: numbered sub-steps, one per line
        public string Photo { get; set; }  // embedded jpg from src\pasos; it may be missing
        public bool HasDetail { get { return !string.IsNullOrEmpty(Detail); } }
        public System.Windows.Media.ImageSource Picture { get { return Pictures(Photo); } }
        public bool HasPicture { get { return Picture != null; } }

        static readonly Dictionary<string, System.Windows.Media.ImageSource> pictures = new Dictionary<string, System.Windows.Media.ImageSource>();
        static System.Windows.Media.ImageSource Pictures(string name)
        {
            if (name == null) return null;
            System.Windows.Media.ImageSource p;
            lock (pictures)
            {
                if (pictures.TryGetValue(name, out p)) return p;
                System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (s != null)
                {
                    System.Windows.Media.Imaging.BitmapImage b = new System.Windows.Media.Imaging.BitmapImage();
                    b.BeginInit();
                    b.StreamSource = s;
                    b.DecodePixelWidth = 720;
                    b.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    b.EndInit();
                    b.Freeze();
                    p = b;
                }
                return pictures[name] = p;
            }
        }
    }

    // Reviews the PC and says what to improve: free things first, then what costs money.
    public static class Review
    {
        static IEnumerable<ManagementObject> Q(string cls, string scope)
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(scope, "SELECT * FROM " + cls))
                foreach (ManagementObject o in s.Get()) yield return o;
        }
        static void Try(Action a)
        {
            // What cannot be read stays without data and generates no advice.
            try { a(); } catch (Exception) { }
        }

        public static void Read(Hw h)
        {
            Try(() =>
            {
                List<ManagementObject> m = Q("Win32_PhysicalMemory", @"root\cimv2").ToList();
                h.RamModules = m.Count;
                h.Ddr = Convert.ToInt32(m[0]["SMBIOSMemoryType"]) == 34 ? 5 : Convert.ToInt32(m[0]["SMBIOSMemoryType"]) == 26 ? 4 : 0;
            });
            Try(() => h.Socket = ((string)Q("Win32_Processor", @"root\cimv2").First()["SocketDesignation"] ?? "").Trim());
            Try(() => h.Board = ((string)Q("Win32_BaseBoard", @"root\cimv2").First()["Product"] ?? "").Trim());
            Try(() => h.DriverDate = ManagementDateTimeConverter.ToDateTime((string)Q("Win32_VideoController", @"root\cimv2").First(g => (string)g["Name"] == h.Gpu)["DriverDate"]));
            Try(() => h.Driver = (string)Q("Win32_VideoController", @"root\cimv2").First(g => (string)g["Name"] == h.Gpu)["DriverVersion"] ?? "");
            Try(() => { Res r = Display.Saved(); h.ScreenW = r.W; h.ScreenH = r.H; h.Hz = r.Hz; h.MaxHz = Display.MaxHz(r); });
            Try(() => { string v = RegOp.ReadValue(@"HKLM\SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled"); if (v != Op.None) h.SecureBoot = v == "d:1"; });
            Try(() =>
            {
                ManagementObject t = Q("Win32_Tpm", @"root\cimv2\Security\MicrosoftTpm").FirstOrDefault();
                h.Tpm2 = t != null && (bool)t["IsEnabled_InitialValue"] && ((string)t["SpecVersion"]).StartsWith("2");
            });
            Try(() => h.Iommu = ((uint[])Q("Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard").First()["AvailableSecurityProperties"]).Contains(3u));
        }

        // FPS the PC really delivers, or 0 if matches are capped below the display.
        public static int Real(List<Match> m, int hz)
        {
            List<Match> last = m.Take(5).Where(x => x.Cap == 0 || x.Cap >= hz * 0.95).ToList();
            return last.Count == 0 ? 0 : (int)last.Average(x => (double)x.Median);
        }

        public static List<Check> For(Hw h, List<Match> matches)
        {
            List<Check> r = new List<Check>();
            Action<Kind, string, string> add = (k, t, x) => r.Add(new Check { Kind = k, Title = t, Text = x });
            int hz = Math.Max(h.Hz, h.MaxHz);

            if (h.MaxHz > 0)
            {
                if (h.Hz < h.MaxHz - 1)
                    r.Add(new Check { Kind = Kind.Gratis, Title = "La pantalla usa " + h.Hz + " Hz y admite " + h.MaxHz, Text = "Es la mejora gratis más notable. Se cambia en Frecuencia de actualización.", Url = "ms-settings:display-advanced" });
                else add(Kind.Bien, "Pantalla a " + h.Hz + " Hz", "Es el máximo disponible a " + h.ScreenW + " × " + h.ScreenH + ".");
            }
            Check gpuDebug = FortniteDebug(IniOp.File);
            if (gpuDebug != null) r.Add(gpuDebug);
            if (!h.Laptop && h.Ddr > 0)
            {
                bool slow = h.Ddr == 5 ? h.RamMhz <= 4800 : h.RamMhz <= 2666;
                if (slow) add(Kind.Gratis, "RAM a " + h.RamMhz + " MHz", "Si el kit admite más velocidad, se activa EXPO o XMP en la BIOS.");
                else add(Kind.Bien, "RAM DDR" + h.Ddr + " a " + h.RamMhz + " MHz", "Supera la velocidad básica: el kit está bien configurado.");
            }
            if (h.RamModules == 1) add(Kind.Comprar, "Un solo módulo de RAM", "Con dos iguales la memoria trabaja en doble canal. En Fortnite ayuda sobre todo a los bajones.");
            if (h.RamGb > 0 && h.RamGb < 16) add(Kind.Comprar, "Ampliar a 16 GB de RAM", "Con " + h.RamGb + " GB, Windows y el juego compiten por memoria.");
            Check driver = Drivers.Advice(h);
            if (driver != null) r.Add(driver);
            else if (h.DriverDate != DateTime.MinValue)
            {
                int months = (int)((DateTime.Now - h.DriverDate).TotalDays / 30);
                string web = h.Gpu.Contains("Radeon") ? "https://www.amd.com/en/support/download/drivers.html" : h.Gpu.Contains("Intel") ? "https://www.intel.com/content/www/us/en/support/detect.html" : null;
                if (months >= 6) r.Add(new Check { Kind = Kind.Gratis, Title = "Antigüedad del driver de la gráfica: " + months + " meses", Text = "Se actualiza desde la app de " + (h.Nvidia ? "NVIDIA" : "la gráfica") + ". No es urgente si todo funciona bien.", Url = web });
                else add(Kind.Bien, "Driver de la gráfica reciente", "Fecha: " + h.DriverDate.ToString("dd/MM/yyyy") + ".");
            }
            if (h.Wifi && !h.Ethernet) add(Kind.Gratis, "Conexión por cable recomendada", "El Wi-Fi provoca picos de ping que ningún tweak elimina. Sin cable, es mejor la banda de 5 o 6 GHz.");
            else if (h.Ethernet) add(Kind.Bien, "Conexión por cable", "Es la conexión más estable.");
            if (h.DiskName.Length > 0)
            {
                if (!h.Ssd) add(Kind.Comprar, "Mover Fortnite a un SSD", "Cargas más rápidas y menos tirones al aparecer el mapa. No sube los FPS.");
                if (h.DiskFreeGb < 30) add(Kind.Gratis, "Quedan " + h.DiskFreeGb + " GB libres en el disco de Windows", "Se recomiendan al menos 30 GB para cachés y actualizaciones.");
            }
            List<string> off = new List<string>(), unread = new List<string>();
            foreach (KeyValuePair<string, bool?> p in new Dictionary<string, bool?> { { "Secure Boot", h.SecureBoot }, { "TPM 2.0", h.Tpm2 }, { "IOMMU", h.Iommu } })
                if (p.Value == null) unread.Add(p.Key); else if (p.Value == false) off.Add(p.Key);
            if (off.Count > 0) add(Kind.Gratis, "Requisito de torneos pendiente: " + string.Join(", ", off), "Epic lo exige en torneos de PC. Se activa en la BIOS; no es necesario fuera de torneos.");
            else if (unread.Count == 0) add(Kind.Bien, "Listo para torneos", "Secure Boot, TPM 2.0 e IOMMU están activos.");

            Buy(h, matches, hz, add);
            return r.OrderBy(c => (int)c.Kind).ToList();
        }

        // Fortnite's GPU crash debugging, on in [D3DRHIPreference]; null if off or unread. Only reads: the player may keep the file
        // read-only on purpose, so neither its contents nor its attributes are touched here.
        public static Check FortniteDebug(string ini)
        {
            try
            {
                if (!System.IO.File.Exists(ini)) return null;
                string section = "", v = null;
                using (System.IO.StreamReader rd = new System.IO.StreamReader(new System.IO.FileStream(ini, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete)))
                    for (string l = rd.ReadLine(); l != null; l = rd.ReadLine())
                    {
                        string t = l.Trim();
                        if (t.StartsWith("[")) section = t;
                        else if (section == "[D3DRHIPreference]" && t.StartsWith("bUseGPUCrashDebugging=", StringComparison.OrdinalIgnoreCase)) v = t.Substring(22).Trim();
                    }
                if (!string.Equals(v, "True", StringComparison.OrdinalIgnoreCase)) return null;
            }
            catch (System.IO.IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            return new Check
            {
                Kind = Kind.Gratis, Title = "Fortnite: depuración de fallos de GPU activada",
                Text = "Cuesta rendimiento y solo sirve para diagnosticar cierres del juego.",
                Detail = "1. Cierra Fortnite.\n2. Abre GameUserSettings.ini en %LOCALAPPDATA%\\FortniteGame\\Saved\\Config\\WindowsClient.\n3. En [D3DRHIPreference], cambia bUseGPUCrashDebugging=True por False y guarda.\nSi el archivo es de solo lectura, se quita esa marca en Propiedades, se guarda y se vuelve a marcar."
            };
        }

        static void Buy(Hw h, List<Match> matches, int hz, Action<Kind, string, string> add)
        {
            if (hz <= 0) return;
            int real = Real(matches, hz);
            Estimate e = Estimator.For(h, h.ScreenW, h.ScreenH);
            bool enough = real > 0 ? real >= hz * 0.95 : e != null && e.Low >= hz;
            if (hz <= 75 && (real >= 120 || (real == 0 && e != null && e.Low >= 120)))
                add(Kind.Comprar, "Un monitor de 144 Hz o más", "El PC genera bastantes más FPS de los " + hz + " que muestra la pantalla. Es la compra más notable.");
            if (enough)
            {
                add(Kind.Bien, "Ninguna compra necesaria para subir FPS", (real > 0 ? "Las últimas partidas rondan " + real + " FPS" : "Se estiman entre " + e.Low + " y " + e.High + " FPS") + " y la pantalla muestra " + hz + " Hz. Más FPS apenas se notarían.");
                return;
            }
            if (e == null) return;
            string why = (real > 0 ? "Las últimas partidas rondan " + real + " FPS" : "Se estiman entre " + e.Low + " y " + e.High + " FPS") + " y la pantalla muestra " + hz + " Hz. ";
            if (h.Laptop) { add(Kind.Gratis, "Bajar la resolución o usar stretched", why + "En un portátil la gráfica y el procesador no se cambian: es lo que más FPS aporta."); return; }
            if (e.GpuLimits) add(Kind.Comprar, "Cuello de botella: la gráfica", why + "Antes de comprar, conviene bajar la resolución. Compra recomendada: RTX 5060, RX 9060 XT o superior.");
            else add(Kind.Comprar, "Cuello de botella: el procesador", why + Cpu(h.Socket));
        }

        // What to improve next, even if not needed today: from most to least noticeable.
        static readonly string[,] Ladder = { { "RTX 5060", "RX 9060 XT" }, { "RTX 5060 Ti", "RX 9060 XT de 16 GB" }, { "RTX 5070", "RX 9070" }, { "RTX 5070 Ti", "RX 9070 XT" }, { "RTX 5080", "RX 9070 XT" } };
        public static List<Check> Plan(Hw h, List<Match> matches)
        {
            List<Check> r = new List<Check>();
            Action<string, string> add = (t, x) => r.Add(new Check { Kind = Kind.Comprar, Title = t, Text = x, N = (r.Count + 1).ToString() });
            int hz = Math.Max(h.Hz, h.MaxHz);
            string at = h.ScreenW + " × " + h.ScreenH;
            Estimate now = Estimator.For(h, h.ScreenW, h.ScreenH);
            List<Match> free = matches.Where(m => m.Cap == 0).Take(5).ToList();
            int fps = free.Count > 0 ? (int)free.Average(m => (double)m.Median) : now == null ? 0 : (now.Low + now.High) / 2;

            Action gpu = () =>
            {
                for (int i = 0; i < Ladder.GetLength(0); i++)
                {
                    Estimate e = Estimator.For(h.With("NVIDIA GeForce " + Ladder[i, 0], h.Cpu), h.ScreenW, h.ScreenH);
                    if (e.High < now.High * 1.35) continue;
                    add("Gráfica: " + Ladder[i, 0] + " o " + Ladder[i, 1], (now.GpuLimits ? "Es el cuello de botella a " + at + ". " : "Ahora no es el cuello de botella. ") + "Estimación: de unos " + now.Low + "–" + now.High + " a unos " + e.Low + "–" + e.High + " FPS.");
                    return;
                }
            };
            bool x3d = Regex.IsMatch(h.Cpu, "X3D", RegexOptions.IgnoreCase);
            Action cpu = () => { if (!x3d) add("Procesador: " + BestCpu(h.Socket), (now.GpuLimits ? "Ahora no limita, pero mejoraría los peores momentos, los más notables en las peleas." : "Es el cuello de botella a " + at + ".") + " " + BoardText(h)); };
            if (now != null && !h.Laptop) { if (now.GpuLimits) { gpu(); cpu(); } else { cpu(); gpu(); } }

            int next = new[] { 144, 240, 360, 480 }.LastOrDefault(x => x > hz * 1.15 && fps >= x * 0.95);
            if (hz > 0 && next > 0) add("Monitor de " + next + " Hz", "Sin límite el PC ronda " + fps + " FPS y la pantalla solo muestra " + hz + ". No sube los FPS, pero muestra los que ya genera.");
            else if (hz > 0 && fps > 0) add("Monitor: sin cambios recomendados", "Para aprovechar más de " + hz + " Hz primero hay que subir los FPS.");
            if (h.RamGb > 0 && h.RamGb < 32) add("RAM: " + (h.RamGb < 16 ? "16 GB" : "32 GB"), "Permite jugar con Discord y el navegador abiertos sin tirones.");
            if (h.DiskName.Length > 0 && !h.Nvme) add("Disco: un SSD NVMe", "Cargas más rápidas. No sube los FPS.");
            if (h.Wifi && !h.Ethernet) add("Red: un cable hasta el router", "Económico y notable en el ping. Si no llega, un adaptador por la instalación eléctrica puede servir.");
            if (!h.Laptop && h.Socket.Length > 0) add("Placa: " + (h.Board.Length > 0 ? h.Board : h.Socket), BoardText(h));
            return r;
        }
        static string BestCpu(string socket)
        {
            return Regex.IsMatch(socket, "AM5") ? "Ryzen 7 7800X3D o 9800X3D" : Regex.IsMatch(socket, "AM4") ? "Ryzen 7 5700X3D o 5800X3D" : Regex.IsMatch(socket, "1700") ? "un i5 o i7 de 13.ª o 14.ª generación" : "uno más rápido";
        }
        static string BoardText(Hw h)
        {
            return Regex.IsMatch(h.Socket, "AM5|1851") ? "La placa no limita: admite procesadores más rápidos."
                : Regex.IsMatch(h.Socket, "AM4|1700") ? "La placa está al final de su plataforma: admite un último salto antes de cambiar placa y RAM."
                : "Con esta placa, cambiar de procesador probablemente requiere cambiar también placa y RAM.";
        }

        static string Cpu(string socket)
        {
            if (Regex.IsMatch(socket, "AM5")) return "Placa AM5: un Ryzen X3D (7800X3D o 9800X3D) es el mayor salto sin cambiar nada más.";
            if (Regex.IsMatch(socket, "AM4")) return "Placa AM4: un Ryzen 7 5700X3D o 5800X3D es lo mejor que admite, quizá tras actualizar la BIOS.";
            if (Regex.IsMatch(socket, "1700")) return "Placa LGA1700: un i5 o i7 de 13.ª o 14.ª generación, quizá tras actualizar la BIOS.";
            return "Comprueba qué procesadores admite la placa; si es antigua, puede requerir cambiar también placa y RAM.";
        }
    }
}
