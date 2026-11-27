using System;
using System.Collections.Generic;
using System.Linq;

namespace Tablero
{
    // A published figure or official statement for one tweak. Text applies when If holds (or there is no scan), Else when it does not; either may be null.
    public class GainEstimate
    {
        public string Text, Else, Source;
        public Func<Hw, bool> If;
        public string For(Hw h) { return h == null || If == null || If(h) ? Text : Else; }
    }

    // What each tweak improves and, only where a measurement or official figure exists, how much. Kept in one table so it can be audited:
    // a tweak without an estimate shows only its kinds. Never a number that its source does not give.
    public static class Gains
    {
        public static readonly string[] Kinds = { "FPS", "Retraso", "Ping", "Estabilidad de red", "Tirones", "Arranque", "Privacidad", "Interrupciones", "Escritorio", "Visibilidad", "Puntería", "Temperatura", "Limpieza" };
        public static readonly Dictionary<string, string[]> Of = new Dictionary<string, string[]>();
        public static readonly Dictionary<string, GainEstimate> Table = new Dictionary<string, GainEstimate>();

        static void K(string kinds, string ids) { foreach (string id in ids.Split(' ')) Of.Add(id, kinds.Split(new[] { ", " }, StringSplitOptions.None)); }
        static void E(string ids, string text, string source, Func<Hw, bool> cond = null, string other = null)
        {
            foreach (string id in ids.Split(' ')) Table.Add(id, new GainEstimate { Text = text, Source = source, If = cond, Else = other });
        }
        public static string Domain(string url) { string h = new Uri(url).Host; return h.StartsWith("www.") ? h.Substring(4) : h; }

        public static void Fill(IEnumerable<Tweak> list)
        {
            foreach (Tweak t in list)
            {
                string[] k; GainEstimate e;
                if (Of.TryGetValue(t.Id, out k)) t.Gain = k;
                if (Table.TryGetValue(t.Id, out e)) t.Estimate = e;
            }
        }

        static Gains()
        {
            // Kinds come from each tweak's mechanism. The tag says what it aims at; the estimate, where there is one, says what was measured.
            K("FPS", "fn-render fn-rt fn-nanite fn-mesh fn-shadow fn-globalillumination fn-reflection fn-postprocess fn-effects fn-antialiasing fn-foliage fn-shading fn-landscape fn-viewdistance fn-gpu val-gpu cs2-gpu fn-aniso val-aniso cs2-aniso val-filter cs2-filter nv-filter gfx-autohdr gfx-dvr gfx-capture gfx-history gfx-dvrpolicy pw-boost pw-epp pw-cool pw-throttle pw-hvci");
            K("FPS, Visibilidad", "fn-blur fn-grass");
            K("FPS, Tirones", "fn-texture gfx-gamemode pw-plan");
            K("Visibilidad", "fn-dynres");
            K("Puntería", "fn-mouse");
            K("Temperatura", "fn-lobby");
            K("Retraso", "fn-vsync fn-reflex fn-framegen val-vsync cs2-vsync cs2-reflex fn-low val-low cs2-low fn-hz val-hz cs2-hz fn-fso val-fso cs2-fso gfx-hz gfx-hags gfx-windowed lt-fse lt-msi pw-usb pw-usb3 pw-timer");
            K("Retraso, Tirones", "val-power cs2-power nv-power");
            K("Tirones", "fn-prio val-prio cs2-prio gfx-vrr pw-min pw-park pw-aspm pw-disk lt-quantum lt-games lt-resp lt-paging lt-mpo bg-apps bg-widgets bg-edge bg-edgetab bg-chrome bg-brave sv-sysmain sv-search sv-pca sv-chroma tk-appraiser tk-winsat");
            K("Tirones, Arranque", "nv-shader");
            K("Arranque", "lt-access lt-short bg-restart bg-edgeboost ui-startup");
            K("Ping", "net-im lt-net");
            K("Ping, Estabilidad de red", "pw-wifi net-qos");
            K("Estabilidad de red", "net-p2p net-bg net-eee net-green net-giga net-flow net-roam net-roam-intel bg-maps sv-maps");
            K("Interrupciones", "gfx-nexus gfx-panel pw-sleep pw-wake net-wol in-sticky in-filter in-toggle in-mousekeys in-contrast in-narrator in-lang bg-maint tk-maps nt-toast nt-suggest nt-backup nt-scoobe nt-welcome nt-tips nt-account nt-account2 nt-feedback nt-ducking nt-autoplay nt-chime");
            K("Escritorio", "in-accel in-delay in-speed ui-transp ui-anim ui-task ui-menu ui-shadow ui-select ui-peek");
            K("Escritorio, Privacidad", "se-bing");
            K("Privacidad", "nv-optin bg-recall bg-click bg-copilot bg-resume bg-mobile bg-clip sv-diag sv-inv sv-geo sv-ai sv-wer sv-push sv-remote tk-disk tk-net tk-mare tk-autochk tk-ceip tk-device tk-siuf tk-usage tk-applist tk-wer ad-id ad-tailored ad-sites se-cloud se-history tl-level tl-name tl-logs tl-dumps tl-logger tl-ait tl-inv tl-steps tl-ceip tl-wer tl-activity tl-typing tl-contacts tl-speech tl-voice tl-input");
            K("Limpieza", "fn-ansel val-ansel cs2-ansel pw-fast bg-drivers bg-coinst bg-wpbt sv-trk sv-print sv-dusm sv-cortex sv-lghub sv-retail sv-phone sv-xbox tk-pca tk-sdb tk-cloud tk-startup tk-power tk-family tk-theme nt-sync ad-silent ad-pre ad-consumer ad-delivery ad-start ad-settings ad-lock ad-third se-highlights");

            const string noNvidia = "Sin efecto: Reflex solo funciona con gráficas NVIDIA.";
            Func<Hw, bool> nvidia = h => h.Nvidia, wifi = h => h.Wifi || !h.Ethernet, cable = h => h.Ethernet || !h.Wifi;

            // NVIDIA, 2024-11-07: "PC latency is reduced by up to 46% in the battle royale" (RTX 40, Reflex vs off).
            E("fn-reflex", "Hasta un 46 % menos de retraso que sin Reflex, según NVIDIA (RTX 40). Desde On, Boost solo ayuda si manda la CPU.",
                "https://www.nvidia.com/en-us/geforce/news/reflex-low-latency-apex-legends-valorant-fortnite-cs2/", nvidia, noNvidia);
            // NVIDIA, CS2 launch: "reduce system latency by up to 35%"; Boost lowers it further when CPU-bound, at the cost of power.
            E("cs2-reflex", "Hasta un 35 % menos de retraso que sin Reflex, según NVIDIA. Desde Activado, Boost solo ayuda si manda la CPU.",
                "https://www.nvidia.com/en-us/geforce/news/counter-strike-2-released-featuring-nvidia-reflex/", nvidia, noNvidia);
            // NVIDIA latency guide: "If both are enabled, Reflex overrides" the driver's low latency mode; the three games have Reflex.
            E("fn-low val-low cs2-low", "Con Reflex activado en el juego no cambia nada: Reflex lo sustituye, según NVIDIA.",
                "https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/");
            // Same guide: "Higher Hz reduces the scanout latency", the best way to cut display latency. 1000 / Hz is arithmetic, not a measurement.
            E("gfx-hz", "Cada imagen dura 1000 ÷ Hz ms: 16,7 a 60 Hz y 6,9 a 144 Hz. Según NVIDIA, es lo que más baja el retraso de pantalla.",
                "https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/", h => h.MaxHz == 0 || h.Hz < h.MaxHz, "Sin cambio: la pantalla ya va a su frecuencia máxima.");
            // Tom's Hardware (i9-13900K, RTX 4090, Memory Integrity off): about 5 % on average over 15 games at 1080p/1440p, up to 10 % in some, 2 % at 4K.
            E("pw-hvci", "Un 5 % más de FPS de media y hasta un 10 % en algunos juegos, según Tom's Hardware (i9-13900K, RTX 4090). En 4K, un 2 %.",
                "https://www.tomshardware.com/news/windows-vbs-harms-performance-rtx-4090");
            // PCWorld, Windows 10, Surface Book (2 cores, GTX 965M): no change without background load; with YouTube, Spotify and antivirus running, minimums doubled.
            E("gfx-gamemode", "Sin tareas de fondo casi no cambia. Con tareas pesadas de fondo, los FPS mínimos se doblaron en un portátil modesto (PCWorld, Windows 10).",
                "https://www.pcworld.com/article/406291/tested-windows-10s-game-mode-makes-unplayable-games-playable-sometimes.html");
            // Tom's Hardware: results "mixed", some configurations slightly worse (Gamers Nexus also saw negative scaling in some games).
            E("gfx-hags", "Efecto pequeño y desigual en las pruebas de Tom's Hardware: unos juegos ganan y otros pierden.",
                "https://www.tomshardware.com/news/gpu-hardware-scheduling-tested");
            // Microsoft: DirectX 10 and 11 games in windowed and borderless modes, "reducing frame latency". Direct3D 12 only has flip-model swap chains.
            E("gfx-windowed", "Según Microsoft, baja el retraso de los juegos DirectX 10 y 11 en ventana o sin bordes; los DirectX 12 ya funcionan así.",
                "https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952");
            // Microsoft: "Auto HDR does take some GPU compute power ... we don't expect it to significantly impact your gaming experience".
            E("gfx-autohdr", "Microsoft: Auto HDR usa algo de GPU, pero no espera que se note al jugar.",
                "https://devblogs.microsoft.com/directx/auto-hdr-preview-for-pc-available-today/");
            // Xbox Wire, 2016: "background recording will likely affect your game performance except on the highest-end GPUs";
            // without it, "Game DVR only runs when you activate it, and has no performance impact".
            const string dvr = "https://news.xbox.com/en-us/2016/10/14/tips-on-windows-10-and-game-dvr-optimization/";
            E("gfx-history", "Microsoft: grabar en segundo plano resta rendimiento salvo en las gráficas más potentes.", dvr);
            E("gfx-dvr gfx-capture", "Microsoft: sin la grabación en segundo plano, solo actúa al usarlo y no resta rendimiento.", dvr);
            // Microsoft: "almost all users who use Fullscreen Optimizations have equal performance to Full Screen Exclusive".
            E("fn-fso val-fso cs2-fso lt-fse", "Microsoft: con sus optimizaciones, casi todos los PC rinden igual que en pantalla completa exclusiva.",
                "https://devblogs.microsoft.com/directx/demystifying-full-screen-optimizations/");
            // Windows blog, 2017: background work goes to the efficient CPU modes; foreground apps count as important work.
            E("pw-throttle", "Microsoft: solo frena el trabajo en segundo plano; el juego en primer plano no se frena.",
                "https://blogs.windows.com/windows-insider/2017/04/18/introducing-power-throttling/");
            // Retired tweaks (siguiente-sesion\tweaks-nuevos.md): 0x26 = short, variable, 3:1 quantum, the client default.
            E("lt-quantum", "Sin efecto: 38 da el mismo reparto de CPU que Windows ya usa para programas, según Microsoft.",
                "https://learn.microsoft.com/en-us/previous-versions/cc976120(v=technet.10)");
            // MMCSS: SFIO Priority "is not used"; with Scheduling Category High, Priority "is always treated as 2".
            E("lt-games", "Sin efecto: Windows no usa SFIO Priority y, con categoría High, trata Priority como 2, según Microsoft.",
                "https://learn.microsoft.com/en-us/windows/win32/procthread/multimedia-class-scheduler-service");
            // Mark Russinovich: while multimedia plays, NDIS passes "at most 10 packets per millisecond (10,000 packets per second)".
            E("lt-net", "Sin efecto en el ping: el tope es de 10.000 paquetes por segundo y solo con audio sonando, según Microsoft.",
                "https://learn.microsoft.com/en-us/archive/blogs/markrussinovich/vista-multimedia-playback-and-network-throughput");
            // IEEE 802.3 (Bennett, 2012): maximum PHY wake time 16.5 us for 1000BASE-T, 30 us for 100BASE-TX.
            E("net-eee", "Despertar el puerto tarda como mucho 16,5 µs a 1 Gb/s, según IEEE 802.3: no se nota en el ping.",
                "https://www.ieee802.org/3/NGBASET/public/sep12/bennett_01a_0912.pdf", cable, "Sin efecto por Wi-Fi: solo cambia la tarjeta de cable.");
            // Microsoft: with moderation a received packet does not raise an immediate interrupt. No figure for games: only the cable/Wi-Fi switch.
            E("net-im", null, "https://learn.microsoft.com/en-us/windows-hardware/drivers/network/interrupt-moderation", cable, "Sin efecto por Wi-Fi: solo cambia la tarjeta de cable.");
            // Wi-Fi only. No published figure in ms: Android turns power save off in its low-latency mode; Intel describes roaming without numbers.
            E("pw-wifi", null, "https://source.android.com/docs/core/connect/wifi-low-latency", wifi, "Sin efecto con cable: solo cambia la antena Wi-Fi.");
            E("net-roam net-roam-intel", null, "https://www.intel.com/content/www/us/en/support/articles/000005546/wireless.html", wifi, "Sin efecto con cable: solo cambia la tarjeta Wi-Fi.");
            // Epic's examples at 720p, low settings vs Performance mode: i5-8265U + UHD 620, 24 -> 61 FPS; A10-5745M + HD 8610G, 18 -> 45 FPS.
            E("fn-render", "En portátiles modestos a 720p pasó de 24 a 61 FPS (Intel UHD 620) y de 18 a 45 (Radeon HD 8610G), según Epic.",
                "https://www.techradar.com/news/fortnite-performance-mode-can-more-than-double-your-frame-rate-on-an-old-pc");
        }
    }
}
