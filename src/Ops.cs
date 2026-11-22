using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace Tablero
{
    // Un Op es un ajuste atomico del sistema. Los valores viajan como texto con prefijo de tipo;
    // None significa "no existe", para poder restaurarlo borrandolo.
    public abstract class Op
    {
        public const string None = "<none>";
        public string Target;
        public string Default;
        public abstract bool Available();
        public abstract List<string> CurrentKeys();
        public virtual List<string> AllKeys() { return CurrentKeys(); }
        public abstract string Read(string key);
        public abstract void Write(string key, string value);
        public virtual string DefaultFor(string key) { return Default; }
        public virtual bool Took(string key, string value) { return Read(key) == value; }
        protected static List<string> One(string k) { return new List<string> { k }; }
    }

    public class RegOp : Op
    {
        public readonly string Path, Name;
        public Func<bool> Condition;
        public RegOp(string path, string name, string target, string def) { Path = path; Name = name; Target = target; Default = def; }
        public string Key { get { return "reg|" + Path + "|" + Name; } }
        public override bool Available() { return Condition == null || Condition(); }
        public override List<string> CurrentKeys() { return One(Key); }
        public override string Read(string key) { return ReadValue(Path, Name); }
        public override void Write(string key, string value) { WriteValue(Path, Name, value); }

        public static RegistryKey Hive(string path, out string sub)
        {
            int i = path.IndexOf('\\');
            string h = path.Substring(0, i);
            sub = path.Substring(i + 1);
            if (h == "HKLM") return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            if (h == "HKCU") return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            throw new ArgumentException("Rama de registro no soportada: " + h);
        }

        public static string ReadValue(string path, string name)
        {
            string sub;
            using (RegistryKey h = Hive(path, out sub))
            using (RegistryKey k = h.OpenSubKey(sub))
            {
                if (k == null) return None;
                object v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (v == null) return None;
                switch (k.GetValueKind(name))
                {
                    case RegistryValueKind.DWord: return "d:" + ((uint)(int)v).ToString(CultureInfo.InvariantCulture);
                    case RegistryValueKind.QWord: return "q:" + ((ulong)(long)v).ToString(CultureInfo.InvariantCulture);
                    case RegistryValueKind.String: return "s:" + (string)v;
                    case RegistryValueKind.ExpandString: return "x:" + (string)v;
                    case RegistryValueKind.MultiString: return "m:" + string.Join("\n", (string[])v);
                    case RegistryValueKind.Binary: return "b:" + BitConverter.ToString((byte[])v).Replace("-", "");
                    default: throw new InvalidDataException("Tipo de registro no soportado en " + path + "\\" + name);
                }
            }
        }

        public static void WriteValue(string path, string name, string value)
        {
            string sub;
            using (RegistryKey h = Hive(path, out sub))
            {
                if (value == None)
                {
                    using (RegistryKey k = h.OpenSubKey(sub, true)) { if (k != null) k.DeleteValue(name, false); }
                    return;
                }
                string d = value.Substring(2);
                using (RegistryKey k = h.CreateSubKey(sub))
                {
                    switch (value[0])
                    {
                        case 'd': k.SetValue(name, unchecked((int)uint.Parse(d, CultureInfo.InvariantCulture)), RegistryValueKind.DWord); break;
                        case 'q': k.SetValue(name, unchecked((long)ulong.Parse(d, CultureInfo.InvariantCulture)), RegistryValueKind.QWord); break;
                        case 's': k.SetValue(name, d, RegistryValueKind.String); break;
                        case 'x': k.SetValue(name, d, RegistryValueKind.ExpandString); break;
                        case 'm': k.SetValue(name, d.Length == 0 ? new string[0] : d.Split('\n'), RegistryValueKind.MultiString); break;
                        case 'b': k.SetValue(name, Enumerable.Range(0, d.Length / 2).Select(i => Convert.ToByte(d.Substring(i * 2, 2), 16)).ToArray(), RegistryValueKind.Binary); break;
                        default: throw new InvalidDataException("Valor guardado no valido: " + value);
                    }
                }
            }
        }
    }

    // Cambia solo un "token=valor;" dentro de un texto que comparte con otros ajustes.
    public class TokenOp : Op
    {
        readonly string path, name, token;
        public TokenOp(string path, string name, string token, string target, string def) { this.path = path; this.name = name; this.token = token; Target = target; Default = def; }
        public override bool Available() { return !name.Contains("\\") || File.Exists(name); }
        public override List<string> CurrentKeys() { return One("tok|" + path + "|" + name + "|" + token); }
        List<string> Parts()
        {
            string v = RegOp.ReadValue(path, name);
            return v == None ? new List<string>() : v.Substring(2).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        public override string Read(string key)
        {
            string p = Parts().FirstOrDefault(x => x.StartsWith(token + "=", StringComparison.OrdinalIgnoreCase));
            return p == null ? None : "s:" + p.Substring(token.Length + 1);
        }
        public override void Write(string key, string value)
        {
            List<string> p = Parts();
            p.RemoveAll(x => x.StartsWith(token + "=", StringComparison.OrdinalIgnoreCase));
            if (value != None) p.Add(token + "=" + value.Substring(2));
            RegOp.WriteValue(path, name, p.Count == 0 ? None : "s:" + string.Join(";", p) + ";");
        }
    }

    // Enciende o apaga un solo bit de un numero guardado como texto (atajos de accesibilidad).
    public class BitOp : Op
    {
        readonly string path, name; readonly uint bit;
        public BitOp(string path, string name, uint bit) { this.path = path; this.name = name; this.bit = bit; Target = "d:0"; Default = "d:1"; }
        uint Value() { return uint.Parse(RegOp.ReadValue(path, name).Substring(2)); }
        public override bool Available() { uint v; string s = RegOp.ReadValue(path, name); return s != None && uint.TryParse(s.Substring(2), out v); }
        public override List<string> CurrentKeys() { return One("bit|" + path + "|" + name + "|" + bit); }
        public override string Read(string key) { return (Value() & bit) != 0 ? "d:1" : "d:0"; }
        public override void Write(string key, string value) { RegOp.WriteValue(path, name, "s:" + (value == "d:1" ? Value() | bit : Value() & ~bit)); }
    }

    public class SvcOp : Op
    {
        readonly string name;
        public SvcOp(string name, int def) { this.name = name; Target = "d:4"; Default = "d:" + def; }
        string Path { get { return "HKLM\\SYSTEM\\CurrentControlSet\\Services\\" + name; } }
        public override bool Available() { return RegOp.ReadValue(Path, "Start") != None; }
        public override List<string> CurrentKeys() { return One("svc|" + name); }
        public override string Read(string key) { return RegOp.ReadValue(Path, "Start"); }
        public override void Write(string key, string value)
        {
            uint start = uint.Parse(value.Substring(2));
            IntPtr scm = OpenSCManager(null, null, 1);
            if (scm == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            try
            {
                IntPtr s = OpenService(scm, name, 2);
                if (s == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                try
                {
                    if (!ChangeServiceConfig(s, uint.MaxValue, start, uint.MaxValue, null, null, IntPtr.Zero, null, null, null, null))
                        throw new System.ComponentModel.Win32Exception();
                }
                finally { CloseServiceHandle(s); }
            }
            finally { CloseServiceHandle(scm); }
            // El tipo de inicio ya quedo cambiado; parar o arrancar ahora es solo para no esperar al reinicio.
            try
            {
                using (ServiceController c = new ServiceController(name))
                {
                    if (start == 4 && c.CanStop) c.Stop();
                    if (start == 2 && c.Status == ServiceControllerStatus.Stopped) c.Start();
                }
            }
            catch (InvalidOperationException) { }
        }
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenSCManager(string m, string d, uint a);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr OpenService(IntPtr h, string n, uint a);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ChangeServiceConfig(IntPtr h, uint type, uint start, uint err, string bin, string grp, IntPtr tag, string dep, string user, string pw, string disp);
        [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr h);
    }

    public class TaskOp : Op
    {
        readonly string path;
        public TaskOp(string path) { this.path = path; Target = "d:0"; Default = "d:1"; }
        dynamic Get()
        {
            dynamic s = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            s.Connect();
            int i = path.LastIndexOf('\\');
            return s.GetFolder(i == 0 ? "\\" : path.Substring(0, i)).GetTask(path.Substring(i + 1));
        }
        public override bool Available()
        {
            try { Get(); return true; }
            catch (IOException) { return false; }
            catch (COMException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        public override List<string> CurrentKeys() { return One("task|" + path); }
        public override string Read(string key) { return (bool)Get().Enabled ? "d:1" : "d:0"; }
        public override void Write(string key, string value) { Get().Enabled = value == "d:1"; }
    }

    public class SchemeOp : Op
    {
        public SchemeOp(string target, string def) { Target = "s:" + target; Default = "s:" + def; }
        public override bool Available() { return Power.Schemes().Contains(new Guid(Target.Substring(2))); }
        public override List<string> CurrentKeys() { return One("scheme|active"); }
        public override string Read(string key) { return "s:" + Power.Active(); }
        // Cortex y similares borran su plan al cerrar el juego: si el guardado ya no existe, se usa Equilibrado.
        static bool Exists(string value) { return Power.Schemes().Contains(new Guid(value.Substring(2))); }
        public override void Write(string key, string value) { Power.Activate(new Guid((Exists(value) ? value : Default).Substring(2))); }
        public override bool Took(string key, string value) { return Read(key) == (Exists(value) ? value : Default); }
    }

    // Los valores de energia viven dentro de cada plan: se lee el plan activo y se guardan todos.
    public class PwrOp : Op
    {
        readonly Guid sub, set;
        public PwrOp(string sub, string set, uint target, uint def) { this.sub = new Guid(sub); this.set = new Guid(set); Target = "d:" + target; Default = "d:" + def; }
        string K(Guid s) { return "pwr|" + s + "|" + sub + "|" + set; }
        static Guid SchemeOf(string key) { return new Guid(key.Split('|')[1]); }
        public override bool Available() { uint v; return Power.ReadAc(Power.Active(), sub, set, out v); }
        public override List<string> CurrentKeys() { return One(K(Power.Active())); }
        public override List<string> AllKeys() { return Power.Schemes().Select(s => K(s)).ToList(); }
        public override string Read(string key) { uint v; return Power.ReadAc(SchemeOf(key), sub, set, out v) ? "d:" + v : None; }
        public override void Write(string key, string value)
        {
            if (value == None) return;
            Power.WriteAc(SchemeOf(key), sub, set, uint.Parse(value.Substring(2)));
        }
    }

    public static class Power
    {
        public static Guid Active()
        {
            IntPtr p;
            Check(PowerGetActiveScheme(IntPtr.Zero, out p));
            try { return (Guid)Marshal.PtrToStructure(p, typeof(Guid)); }
            finally { LocalFree(p); }
        }
        public static void Activate(Guid g) { Check(PowerSetActiveScheme(IntPtr.Zero, ref g)); }
        public static List<Guid> Schemes()
        {
            List<Guid> r = new List<Guid>();
            for (uint i = 0; ; i++)
            {
                byte[] b = new byte[16];
                uint n = 16;
                if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, b, ref n) != 0) return r;
                r.Add(new Guid(b));
            }
        }
        public static bool ReadAc(Guid s, Guid sub, Guid set, out uint v) { return PowerReadACValueIndex(IntPtr.Zero, ref s, ref sub, ref set, out v) == 0; }
        public static void WriteAc(Guid s, Guid sub, Guid set, uint v)
        {
            Check(PowerWriteACValueIndex(IntPtr.Zero, ref s, ref sub, ref set, v));
            if (s == Active()) Activate(s);
        }
        static void Check(uint e) { if (e != 0) throw new System.ComponentModel.Win32Exception((int)e); }
        [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr k, out IntPtr g);
        [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr k, ref Guid g);
        [DllImport("powrprof.dll")] static extern uint PowerEnumerate(IntPtr k, IntPtr s, IntPtr sub, uint flags, uint i, byte[] b, ref uint n);
        [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr k, ref Guid s, ref Guid sub, ref Guid set, out uint v);
        [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr k, ref Guid s, ref Guid sub, ref Guid set, uint v);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    }

    // Propiedad avanzada de red, en cada tarjeta PCI que la tenga.
    public class NicOp : Op
    {
        const string Class = "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e972-e325-11ce-bfc1-08002be10318}";
        readonly string keyword;
        public NicOp(string keyword, string target) { this.keyword = keyword; Target = "s:" + target; }
        public override bool Available() { return CurrentKeys().Count > 0; }
        public override List<string> CurrentKeys()
        {
            List<string> r = new List<string>();
            string sub;
            using (RegistryKey h = RegOp.Hive(Class, out sub))
            using (RegistryKey c = h.OpenSubKey(sub))
                foreach (string n in c.GetSubKeyNames().Where(x => x.Length == 4))
                {
                    string comp = RegOp.ReadValue(Class + "\\" + n, "ComponentId");
                    if (comp.StartsWith("s:PCI\\", StringComparison.OrdinalIgnoreCase) && RegOp.ReadValue(Class + "\\" + n, keyword) != None)
                        r.Add("nic|" + n + "|" + keyword);
                }
            return r;
        }
        static string PathOf(string key) { return Class + "\\" + key.Split('|')[1]; }
        public override string Read(string key) { return RegOp.ReadValue(PathOf(key), keyword); }
        public override void Write(string key, string value) { RegOp.WriteValue(PathOf(key), keyword, value); }
        public override string DefaultFor(string key)
        {
            string d = RegOp.ReadValue(PathOf(key) + "\\Ndi\\Params\\" + keyword, "default");
            return d == None || d == Target ? "s:1" : d;
        }
    }

    public class IniOp : Op
    {
        public static string File = Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%\\FortniteGame\\Saved\\Config\\WindowsClient\\GameUserSettings.ini");
        readonly string section, name;
        public IniOp(string section, string name, string target, string def) { this.section = section; this.name = name; Target = "s:" + target; Default = "s:" + def; }
        public static Func<bool> GameRunning = () => Process.GetProcessesByName("FortniteClient-Win64-Shipping").Length > 0;
        public override bool Available() { return System.IO.File.Exists(File); }
        public override List<string> CurrentKeys() { return One("ini|" + section + "|" + name); }
        static List<string> Lines(out Encoding enc)
        {
            using (StreamReader r = new StreamReader(File, new UTF8Encoding(false), true))
            {
                string t = r.ReadToEnd();
                enc = r.CurrentEncoding;
                if (System.IO.File.ReadAllBytes(File).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) enc = new UTF8Encoding(true);
                return t.Replace("\r\n", "\n").Split('\n').ToList();
            }
        }
        // Devuelve la linea de la clave, o -1; end es donde termina la seccion (-1 si no existe).
        int Find(List<string> l, out int end)
        {
            end = -1;
            int start = l.FindIndex(x => x.Trim() == "[" + section + "]");
            if (start < 0) return -1;
            int next = l.FindIndex(start + 1, x => x.StartsWith("["));
            end = next < 0 ? l.Count : next;
            int e = end;
            return l.FindIndex(start, e - start, x => x.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase));
        }
        public override string Read(string key)
        {
            Encoding enc; int end;
            List<string> l = Lines(out enc);
            int i = Find(l, out end);
            return i < 0 ? None : "s:" + l[i].Substring(name.Length + 1).Trim();
        }
        public override void Write(string key, string value)
        {
            if (GameRunning()) throw new InvalidOperationException("Cierra Fortnite primero: el juego sobrescribe su configuración al salir.");
            Encoding enc; int end;
            List<string> l = Lines(out enc);
            int i = Find(l, out end);
            string line = value == None ? null : name + "=" + value.Substring(2);
            if (i >= 0) { if (line == null) l.RemoveAt(i); else l[i] = line; }
            else if (line != null)
            {
                if (end < 0) { l.Add("[" + section + "]"); l.Add(line); }
                else
                {
                    while (end > 0 && l[end - 1].Trim().Length == 0) end--;
                    l.Insert(end, line);
                }
            }
            FileAttributes a = System.IO.File.GetAttributes(File);
            string tmp = File + ".tablero";
            System.IO.File.WriteAllText(tmp, string.Join("\r\n", l), enc);
            System.IO.File.SetAttributes(File, a & ~FileAttributes.ReadOnly);
            System.IO.File.Replace(tmp, File, null);
            System.IO.File.SetAttributes(File, a);
        }
    }

    // Perfil del controlador NVIDIA. None = el valor de fabrica del controlador.
    public class NvOp : Op
    {
        readonly string profile; readonly uint id;
        public NvOp(string profile, uint id, uint target) { this.profile = profile; this.id = id; Target = "d:" + target; Default = None; }
        public override bool Available() { return Nv.HasProfile(profile); }
        public override List<string> CurrentKeys() { return One("nv|" + profile + "|" + id.ToString("X8")); }
        public override string Read(string key) { return Nv.Get(profile, id); }
        public override void Write(string key, string value) { Nv.Set(profile, id, value); }
    }

    public static class Nv
    {
        const int NotFound = -160, Size = 12320;
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr Query(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int F0();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FOut(out IntPtr a);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int F1(IntPtr a);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FBase(IntPtr s, out IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FFind(IntPtr s, byte[] name, out IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FGet(IntPtr s, IntPtr p, uint id, byte[] st);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FSet(IntPtr s, IntPtr p, byte[] st);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FId(IntPtr s, IntPtr p, uint id);
        static T Fn<T>(uint id) where T : class
        {
            IntPtr p = Query(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException("El controlador NVIDIA no ofrece esta función.");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        static void Check(int e) { if (e != 0) throw new InvalidOperationException("El controlador NVIDIA devolvió el error " + e + "."); }

        static TR With<TR>(string profile, Func<IntPtr, IntPtr, TR> body)
        {
            Check(Fn<F0>(0x0150E828)());
            IntPtr s, p;
            Check(Fn<FOut>(0x0694D52E)(out s));
            try
            {
                Check(Fn<F1>(0x375DBD6B)(s));
                if (profile.Length == 0) Check(Fn<FBase>(0xDA8466A0)(s, out p));
                else
                {
                    byte[] n = new byte[4096];
                    Encoding.Unicode.GetBytes(profile, 0, profile.Length, n, 0);
                    Check(Fn<FFind>(0x7E4A9A0B)(s, n, out p));
                }
                return body(s, p);
            }
            finally { Fn<F1>(0x0DAD9CFF8)(s); }
        }
        public static bool HasProfile(string profile)
        {
            try { return With(profile, (s, p) => true); }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        static byte[] Blank() { byte[] b = new byte[Size]; BitConverter.GetBytes(Size | (1 << 16)).CopyTo(b, 0); return b; }

        public static string Get(string profile, uint id)
        {
            return With(profile, (s, p) =>
            {
                byte[] b = Blank();
                int e = Fn<FGet>(0x73BF8338)(s, p, id, b);
                if (e == NotFound) return Op.None;
                Check(e);
                // location 0 = definido en este perfil; predefinido = valor de fabrica
                if (BitConverter.ToInt32(b, 4108) != 0 || BitConverter.ToInt32(b, 4112) != 0) return Op.None;
                return "d:" + BitConverter.ToUInt32(b, 8220);
            });
        }
        public static void Set(string profile, uint id, string value)
        {
            With(profile, (s, p) =>
            {
                if (value == Op.None)
                {
                    int e = Fn<FId>(0x53F0381E)(s, p, id);
                    if (e != 0) e = Fn<FId>(0xE4A26362)(s, p, id);
                    if (e != 0 && e != NotFound) Check(e);
                }
                else
                {
                    byte[] b = Blank();
                    BitConverter.GetBytes(id).CopyTo(b, 4100);
                    BitConverter.GetBytes(uint.Parse(value.Substring(2))).CopyTo(b, 8220);
                    Check(Fn<FSet>(0x577DD202)(s, p, b));
                }
                Check(Fn<F1>(0xFCBC7E14)(s));
                return true;
            });
        }
    }
}
