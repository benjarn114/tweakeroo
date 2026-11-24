// FPS of a game counted from its Present calls with ETW (Windows event tracing), as PresentMon does.
// Nothing of the game is touched: no memory, no files, no injection. Real-time ETW needs administrator (the app has it).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Tablero
{
    // Real-time ETW session that hands each event record to a callback. Raw offsets, x64 only (the app is /platform:x64).
    public sealed class EtwSession : IDisposable
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int StartTraceW(out ulong handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int ControlTraceW(ulong handle, string name, IntPtr props, int code);
        [DllImport("advapi32.dll")] static extern int EnableTraceEx2(ulong handle, ref Guid provider, int code, byte level, ulong any, ulong all, int timeout, IntPtr parameters);
        [DllImport("advapi32.dll", SetLastError = true)] static extern ulong OpenTraceW(IntPtr logfile);
        [DllImport("advapi32.dll")] static extern int ProcessTrace(ulong[] handles, int count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern int CloseTrace(ulong handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void RecordCallback(IntPtr record);

        const int PropsSize = 120, NameRoom = 1024, LogfileSize = 448;
        const int RealTime = 0x100, PrivateLogger = 0x800, PrivateInProc = 0x20000, TracedGuid = 0x20000, StopCode = 1, Enable = 1;
        const int ModeRealTime = 0x100, ModeEventRecord = 0x10000000, AlreadyExists = 183;
        const ulong Invalid = ulong.MaxValue;

        readonly IntPtr props, logfile, namePtr;
        readonly RecordCallback keep; // the delegate must outlive ProcessTrace
        ulong session, trace = Invalid;
        Thread pump;
        bool freed;

        // etl: a private session of this process written to that file (only for the self-test: private sessions cannot be real-time).
        public EtwSession(string name, RecordCallback onEvent, string etl = null, Guid privateProvider = default(Guid))
        {
            keep = onEvent;
            props = Zeroed(PropsSize + NameRoom);
            logfile = Zeroed(LogfileSize);
            namePtr = Marshal.StringToHGlobalUni(name);
            Props(etl, privateProvider);
            // A session left by a crash keeps the name busy: it is stopped first.
            int e = StartTraceW(out session, name, props);
            if (e == AlreadyExists) { Stop(name); Props(etl, privateProvider); e = StartTraceW(out session, name, props); }
            if (e != 0) { Free(); throw new Win32Exception(e, "No se pudo abrir la sesión ETW (código " + e + ")."); }
        }
        static IntPtr Zeroed(int size) { IntPtr p = Marshal.AllocHGlobal(size); for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0); return p; }
        void Props(string etl, Guid provider)
        {
            for (int i = 0; i < PropsSize + NameRoom; i++) Marshal.WriteByte(props, i, 0);
            Marshal.WriteInt32(props, 0, PropsSize + NameRoom);   // Wnode.BufferSize
            bool priv = etl != null;
            if (priv) Marshal.Copy(provider.ToByteArray(), 0, props + 24, 16); // Wnode.Guid: a private session belongs to its provider
            Marshal.WriteInt32(props, 40, 1);                     // Wnode.ClientContext: QPC clock
            Marshal.WriteInt32(props, 44, TracedGuid);            // Wnode.Flags
            Marshal.WriteInt32(props, 48, 64);                    // BufferSize, KB
            Marshal.WriteInt32(props, 64, priv ? PrivateLogger | PrivateInProc : RealTime); // LogFileMode
            Marshal.WriteInt32(props, 68, 1);                     // FlushTimer: a second at most between event and callback
            Marshal.WriteInt32(props, 116, PropsSize);            // LoggerNameOffset
            if (!priv) return;
            Marshal.WriteInt32(props, 112, PropsSize + NameRoom / 2); // LogFileNameOffset
            char[] c = (etl + "\0").ToCharArray();
            Marshal.Copy(c, 0, props + PropsSize + NameRoom / 2, c.Length);
        }

        // Stops a session by name, e.g. one left running by a crash (a real-time session outlives its process). Quiet if there is none.
        public static void Stop(string name)
        {
            IntPtr p = Zeroed(PropsSize + NameRoom);
            try { Marshal.WriteInt32(p, 0, PropsSize + NameRoom); Marshal.WriteInt32(p, 116, PropsSize); ControlTraceW(0, name, p, StopCode); }
            finally { Marshal.FreeHGlobal(p); }
        }

        // Reads a finished .etl file on this thread (self-test only).
        public static void Replay(string etl, RecordCallback onEvent)
        {
            IntPtr lf = Zeroed(LogfileSize), file = Marshal.StringToHGlobalUni(etl);
            try
            {
                Marshal.WriteIntPtr(lf, 0, file);                                // LogFileName
                Marshal.WriteInt32(lf, 28, ModeEventRecord);                     // ProcessTraceMode
                Marshal.WriteIntPtr(lf, 424, Marshal.GetFunctionPointerForDelegate(onEvent));
                ulong t = OpenTraceW(lf);
                if (t == Invalid) { int e = Marshal.GetLastWin32Error(); throw new Win32Exception(e, "No se pudo abrir " + etl + " (código " + e + ")."); }
                try { int e = ProcessTrace(new[] { t }, 1, IntPtr.Zero, IntPtr.Zero); if (e != 0) throw new Win32Exception(e, "ProcessTrace devolvió " + e + "."); }
                finally { CloseTrace(t); }
            }
            finally { Marshal.FreeHGlobal(lf); Marshal.FreeHGlobal(file); GC.KeepAlive(onEvent); }
        }

        public void EnableProvider(Guid provider, byte level, ulong keywords)
        {
            int e = EnableTraceEx2(session, ref provider, Enable, level, keywords, 0, 0, IntPtr.Zero);
            if (e != 0) throw new Win32Exception(e, "ETW no activó el proveedor " + provider + " (código " + e + ").");
        }

        // Events arrive on a background thread until Dispose.
        public void Start()
        {
            Marshal.WriteIntPtr(logfile, 8, namePtr);                          // LoggerName
            Marshal.WriteInt32(logfile, 28, ModeRealTime | ModeEventRecord);   // ProcessTraceMode
            Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(keep)); // EventRecordCallback
            trace = OpenTraceW(logfile);
            if (trace == Invalid) { int e = Marshal.GetLastWin32Error(); throw new Win32Exception(e, "No se pudo leer la sesión ETW (código " + e + ")."); }
            pump = new Thread(() => ProcessTrace(new[] { trace }, 1, IntPtr.Zero, IntPtr.Zero)) { IsBackground = true, Name = "ETW" };
            pump.Start();
        }

        public void Dispose()
        {
            if (freed) return;
            // Stopping the session ends ProcessTrace; the remaining buffers are delivered first.
            ControlTraceW(session, null, props, StopCode);
            if (pump != null) pump.Join(3000);
            if (trace != Invalid) CloseTrace(trace);
            trace = Invalid;
            Free();
        }
        void Free() { if (freed) return; freed = true; Marshal.FreeHGlobal(props); Marshal.FreeHGlobal(logfile); Marshal.FreeHGlobal(namePtr); }

        // EVENT_RECORD (x64): what the counter needs from the header.
        public static int Pid(IntPtr r) { return Marshal.ReadInt32(r, 12); }
        public static long Time(IntPtr r) { return Marshal.ReadInt64(r, 16); } // FILETIME (UTC), already converted by ETW
        public static Guid Provider(IntPtr r) { byte[] b = new byte[16]; Marshal.Copy(r + 24, b, 0, 16); return new Guid(b); }
        public static int Id(IntPtr r) { return (ushort)Marshal.ReadInt16(r, 40); }
        public static int HeaderFlags(IntPtr r) { return (ushort)Marshal.ReadInt16(r, 4); }
        public static int DataLength(IntPtr r) { return (ushort)Marshal.ReadInt16(r, 86); }
        public static IntPtr Data(IntPtr r) { return Marshal.ReadIntPtr(r, 96); }
    }

    // Frames presented by the watched processes, from DXGI: DirectX 9Ex/10/11/12 (CS2 uses DirectX 11 unless -vulkan).
    public sealed class Presents : IDisposable
    {
        public static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        const int PresentStart = 42, MpoStart = 55, Is64 = 0x40, PresentTest = 0x1;
        const ulong DxgiEvents = 0x2;

        readonly EtwSession etw;
        readonly Action<long> frame;
        volatile HashSet<int> pids = new HashSet<int>();

        // frame receives each frame's time as FILETIME (UTC), on ETW's thread.
        public Presents(string session, Action<long> frame)
        {
            this.frame = frame;
            etw = new EtwSession(session, OnEvent);
            try { etw.EnableProvider(Dxgi, 4, DxgiEvents); etw.Start(); }
            catch { etw.Dispose(); throw; }
        }
        public void Watch(IEnumerable<int> ids) { pids = new HashSet<int>(ids); }

        void OnEvent(IntPtr r) { if (pids.Contains(EtwSession.Pid(r)) && Counts(r, Dxgi)) frame(EtwSession.Time(r)); }

        // Present or PresentMultiplaneOverlay (start) of that provider. A test present (DXGI_PRESENT_TEST, an occlusion check) draws nothing.
        // Payload as PresentMon reads it: swap chain pointer, Flags, SyncInterval.
        internal static bool Counts(IntPtr r, Guid provider)
        {
            int id = EtwSession.Id(r);
            if (id != PresentStart && id != MpoStart || EtwSession.Provider(r) != provider) return false;
            int ptr = (EtwSession.HeaderFlags(r) & Is64) != 0 ? 8 : 4;
            return id != PresentStart || EtwSession.DataLength(r) < ptr + 4 || (Marshal.ReadInt32(EtwSession.Data(r), ptr) & PresentTest) == 0;
        }

        public void Dispose() { etw.Dispose(); }
    }

    // Frames per second (UTC) of one game session, summarized like Fortnite's logs when it ends.
    public sealed class FpsRecorder : IDisposable
    {
        public const string Session = "Tweakeroo-FPS";
        public const string Admin = "Los FPS de CS2 necesitan permisos de administrador.";
        // Why the last recorder counts nothing, for the interface; null when ETW works.
        public static string Status { get; private set; }

        readonly Dictionary<long, int> seconds = new Dictionary<long, int>();
        Presents etw;

        // Without ETW: counts only what Frame receives (tests and the test build).
        public FpsRecorder() { }

        // Counts the DXGI presents of the watched processes. Without administrator, or if ETW fails, it counts nothing and Status says why.
        public static FpsRecorder Live()
        {
            FpsRecorder r = new FpsRecorder();
            try { r.etw = new Presents(Session, t => r.Frame(DateTime.FromFileTimeUtc(t))); Status = null; }
            catch (Win32Exception ex) { Status = ex.NativeErrorCode == 5 ? Admin : "No se pudieron medir los FPS de CS2 (código " + ex.NativeErrorCode + ")."; }
            return r;
        }

        public void Watch(IEnumerable<int> pids) { if (etw != null) etw.Watch(pids); }

        public void Frame(DateTime utc)
        {
            long s = utc.Ticks / TimeSpan.TicksPerSecond;
            lock (seconds) { int n; seconds.TryGetValue(s, out n); seconds[s] = n + 1; }
        }

        // The whole session as one match, when the map is not known.
        public Match Finish(DateTime start, int? cap) { return Cut(start, DateTime.MaxValue, "Partida", cap); }

        // Whole seconds from..to without the first 20 (loading) and without those with no frames (minimized); null under a minute.
        // cap: the game's FPS limit (0 = none); null = guessed from the FPS, like Fortnite.
        public Match Cut(DateTime from, DateTime to, string map, int? cap)
        {
            long first = (from.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond + FpsLog.Skip, end = to.Ticks / TimeSpan.TicksPerSecond;
            List<double> w;
            lock (seconds) w = seconds.Where(x => x.Key >= first && x.Key < end).Select(x => (double)x.Value).ToList();
            if (w.Count < FpsLog.MinSeconds) return null;
            Match m = FpsLog.Sum(map, from, w);
            if (cap != null) m.Cap = cap.Value;
            return m;
        }

        // maps: one match per map, the menu left out; null (the game never told its map) = the whole session as one.
        public List<Match> Matches(DateTime start, List<MapSpan> maps, int? cap)
        {
            IEnumerable<Match> r = maps == null ? new[] { Finish(start, cap) } : maps.Select(s => Cut(s.From, s.To, s.Map, cap));
            return r.Where(m => m != null).ToList();
        }

        public void Dispose() { if (etw != null) etw.Dispose(); }
    }
}
