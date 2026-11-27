using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tablero
{
    public static class Catalog
    {
        const string U = @"HKCU\Software\Microsoft\Windows\CurrentVersion\";
        const string P = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\";
        const string PU = @"HKCU\Software\Policies\Microsoft\Windows\";
        const string Cdm = U + "ContentDeliveryManager";
        const string Adv = U + @"Explorer\Advanced";
        const string Acc = @"HKCU\Control Panel\Accessibility\";
        const string Ccs = @"HKLM\SYSTEM\CurrentControlSet\";
        const string Game = "/Script/FortniteGame.FortGameUserSettings";
        const string Cpu = "54533251-82be-4824-96c1-47b60b740d00";
        const string Usb = "2a737441-1930-4402-8d77-b2bebba308a3";
        const string Sleep = "238c9fa8-0aad-41ed-83f4-97be242c8f20";
        const string TaskRoot = @"\Microsoft\Windows\";

        static List<Tweak> list;
        static string cat;

        static void T(string id, string name, string desc, Level l, string effect, string restart, params Op[] ops)
        {
            list.Add(new Tweak { Id = id, Cat = cat, Name = name, Desc = desc, Level = l, Effect = effect, Restart = restart, Ops = ops.ToList() });
        }
        static string Num(int? v) { return v == null ? Op.None : "d:" + v; }
        static RegOp D(string path, string name, int on, int? def) { return new RegOp(path, name, Num(on), Num(def)); }
        static RegOp S(string path, string name, string on, string def) { return new RegOp(path, name, "s:" + on, def == null ? Op.None : "s:" + def); }
        static RegOp IfFile(RegOp op, string file) { op.Condition = () => File.Exists(Environment.ExpandEnvironmentVariables(file)); return op; }
        static RegOp IfValue(RegOp op) { op.Condition = () => RegOp.ReadValue(op.Path, op.Name) != Op.None; return op; }
        static IniOp I(string name, string on, string def) { return new IniOp(Game, name, on, def); }
        static TaskOp K(string path) { return new TaskOp(TaskRoot + path); }

        static string GpuPath()
        {
            try
            {
                using (System.Management.ManagementObjectSearcher q = new System.Management.ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_VideoController"))
                    foreach (System.Management.ManagementObject o in q.Get())
                    {
                        string id = o["PNPDeviceID"] as string;
                        if (id != null && (id.StartsWith(@"PCI\VEN_10DE") || id.StartsWith(@"PCI\VEN_1002"))) return Ccs + @"Enum\" + id;
                    }
            }
            catch (System.Management.ManagementException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            return null;
        }

        // The same for every game, in its own profile: they do not touch its files.
        static void Own(string p, string game, string exe, string path, string profile = null)
        {
            if (profile == null) profile = game;
            const Level s = Level.Seguro, a = Level.Avanzado;
            const string ifeo = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";
            T(p + "-low", "Baja latencia de NVIDIA en " + game, "Un solo fotograma en espera. Con Reflex encendido en el juego apenas cambia.", s, "Medio", "el juego", new NvOp(profile, 0x007BA09E, 1));
            T(p + "-aniso", "Texturas con muestreo rápido en " + game, "Optimización de muestreo anisótropo de NVIDIA.", s, "Bajo", "el juego", new NvOp(profile, 0x00E73211, 1));
            T(p + "-ansel", "Ansel apagado en " + game, "El capturador de NVIDIA no se carga con el juego.", s, "Bajo", "el juego", new NvOp(profile, 0x1075D972, 0));
            T(p + "-hz", "Máxima frecuencia de pantalla en " + game, "El juego usa los Hz más altos del monitor.", s, "Bajo", "el juego", new NvOp(profile, 0x0064B541, 1));
            T(p + "-prio", "Prioridad alta de CPU para " + game, "Windows abre el juego con prioridad alta de CPU y disco. Es un ajuste de Windows.", a, "Medio", "el juego", IfFile(D(ifeo + exe + @"\PerfOptions", "CpuPriorityClass", 3, null), path), IfFile(D(ifeo + exe + @"\PerfOptions", "IoPriority", 3, null), path));
            T(p + "-fso", "Sin optimización de pantalla completa en " + game, "Ayuda en algunos PC y en otros empeora: conviene comparar.", a, "Bajo", "el juego", IfFile(S(@"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", path, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE", null), path));
        }

        public static List<Tweak> Build()
        {
            list = new List<Tweak>();
            const Level s = Level.Seguro, a = Level.Avanzado, r = Level.Riesgo;

            cat = "Fortnite";
            T("fn-render", "Modo Rendimiento", "El renderizador más ligero del juego.", s, "Alto", "", new IniOp("D3DRHIPreference", "PreferredFeatureLevel", "es31", "sm6"));
            T("fn-vsync", "Apagar VSync", "Quita la espera al monitor, que suma retraso.", s, "Alto", "", I("bUseVSync", "False", "True"));
            T("fn-reflex", "NVIDIA Reflex en On + Boost", "Menos retraso entre el clic y la pantalla. Confírmalo en los ajustes del juego.", s, "Medio", "", I("LatencyTweak2", "2", "1"));
            T("fn-blur", "Apagar desenfoque de movimiento", "Imagen más nítida al girar.", s, "Medio", "", I("bMotionBlur", "False", "True"));
            T("fn-rt", "Apagar ray tracing", "El ajuste gráfico más caro.", s, "Alto", "", I("bRayTracing", "False", "True"));
            T("fn-nanite", "Apagar Nanite", "Geometría simple en vez de detalle infinito.", s, "Alto", "", I("bUseNanite", "False", "True"));
            T("fn-framegen", "Apagar generación de fotogramas", "Sube los FPS que ves, pero añade retraso.", s, "Medio", "", I("bEnableDLSSFrameGeneration", "False", "True"));
            T("fn-dynres", "Apagar resolución dinámica", "La nitidez no cambia en mitad de una pelea.", s, "Bajo", "", I("bUseDynamicResolution", "False", "True"));
            T("fn-grass", "Apagar hierba", "Menos que dibujar y mejor visibilidad.", s, "Bajo", "", I("bShowGrass", "False", "True"));
            T("fn-mouse", "Apagar aceleración del ratón en el juego", "El mismo gesto siempre mueve lo mismo.", a, "Medio", "", I("bDisableMouseAcceleration", "True", "False"));
            T("fn-lobby", "Lobby a 60 FPS", "El PC descansa y llega más frío a la partida.", s, "Bajo", "", I("FrontendFrameRateLimit", "60.000000", "120.000000"));
            T("fn-mesh", "Mallas en bajo", "Modelos más simples en modo Rendimiento.", s, "Medio", "", new IniOp("PerformanceMode", "MeshQuality", "0", "1"));
            string[,] sg = {
                { "Shadow", "Sombras en bajo", "Alto" }, { "GlobalIllumination", "Iluminación global en bajo", "Alto" },
                { "Reflection", "Reflejos en bajo", "Alto" }, { "PostProcess", "Posprocesado en bajo", "Medio" },
                { "Effects", "Efectos en bajo", "Medio" }, { "AntiAliasing", "Suavizado en bajo", "Medio" },
                { "Texture", "Texturas en bajo", "Bajo" }, { "Foliage", "Vegetación en bajo", "Medio" },
                { "Shading", "Sombreado en bajo", "Medio" }, { "Landscape", "Terreno en bajo", "Bajo" },
                { "ViewDistance", "Distancia de dibujado en bajo", "Medio" } };
            for (int i = 0; i < sg.GetLength(0); i++)
                T("fn-" + sg[i, 0].ToLowerInvariant(), sg[i, 1], sg[i, 0] == "ViewDistance" ? "Los jugadores y construcciones se ven igual de lejos." : "Calidad gráfica mínima del juego.", s, sg[i, 2], "", new IniOp("ScalabilityGroups", "sg." + sg[i, 0] + "Quality", "0", "2"));
            T("fn-gpu", "Fortnite usa la gráfica potente", "Con dos gráficas, Windows usa siempre la potente.", s, "Bajo", "", new TokenOp(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", Tablero.Game.Fortnite.Exe, "GpuPreference", "s:2", Op.None));

            Own("fn", "Fortnite", "FortniteClient-Win64-Shipping.exe", Tablero.Game.Fortnite.Exe);

            cat = "Valorant";
            T("val-gpu", "Valorant usa la gráfica potente", "Con dos gráficas, Windows usa siempre la potente.", s, "Bajo", "", new TokenOp(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", Tablero.Game.Valorant.Exe, "GpuPreference", "s:2", Op.None));
            T("val-power", "NVIDIA a máximo rendimiento en Valorant", "La gráfica no baja de frecuencia durante la partida.", s, "Medio", "el juego", new NvOp("Valorant", 0x1057EB71, 1));
            T("val-filter", "Filtrado de texturas rápido en Valorant", "Menos trabajo de textura, solo en Valorant.", s, "Bajo", "el juego", new NvOp("Valorant", 0x00CE2691, 20));
            T("val-vsync", "VSync apagado en Valorant", "Quita la espera al monitor desde el perfil de NVIDIA, sin tocar el juego.", s, "Medio", "el juego", new NvOp("Valorant", 0x00A879CF, 0x08416747));

            Own("val", "Valorant", "VALORANT-Win64-Shipping.exe", Tablero.Game.Valorant.Exe);

            cat = "Counter-Strike 2"; // = Game.Name: switching games keeps this section open
            const string cs2 = "Counter-strike 2"; // exact NVIDIA profile name, lowercase s
            T("cs2-gpu", "CS2 usa la gráfica potente", "Con dos gráficas, Windows usa siempre la potente.", s, "Bajo", "", new TokenOp(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", Tablero.Game.Cs2.Exe, "GpuPreference", "s:2", Op.None));
            T("cs2-power", "NVIDIA a máximo rendimiento en CS2", "La gráfica no baja de frecuencia durante la partida.", s, "Medio", "el juego", new NvOp(cs2, 0x1057EB71, 1));
            T("cs2-filter", "Filtrado de texturas rápido en CS2", "Menos trabajo de textura, solo en CS2.", s, "Bajo", "el juego", new NvOp(cs2, 0x00CE2691, 20));
            T("cs2-vsync", "VSync apagado en CS2", "Quita la espera al monitor desde el perfil de NVIDIA, sin tocar el juego.", s, "Medio", "el juego", new NvOp(cs2, 0x00A879CF, 0x08416747));
            // 0 = off, 1 = on, 2 = on + boost (string in the game files, reported by the dataminer Thour, Sep 2023).
            T("cs2-reflex", "NVIDIA Reflex en On + Boost", "Menos retraso entre el clic y la pantalla. Confírmalo en los ajustes del juego.", s, "Medio", "", new Cs2VideoOp("setting.r_low_latency", "2", "1"));

            Own("cs2", "CS2", "cs2.exe", Tablero.Game.Cs2.Exe, cs2);

            cat = "Gráficos y captura";
            T("gfx-hz", "Pantalla a su frecuencia máxima", "El escritorio usa los Hz más altos del monitor. En ventana sin bordes es la frecuencia que manda. Hay que confirmarlo en 15 segundos.", s, "Alto", "", new HzOp());
            T("gfx-gamemode", "Modo Juego", "Windows aparta tareas y actualizaciones mientras juegas.", s, "Medio", "", D(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, 0), D(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1, null));
            T("gfx-hags", "Programación de GPU por hardware", "La tarjeta gestiona su propia cola.", s, "Medio", "el PC", D(Ccs + @"Control\GraphicsDrivers", "HwSchMode", 2, 1));
            T("gfx-windowed", "Optimización para juegos en ventana", "Menos retraso en ventana y sin bordes.", s, "Medio", "el juego", new TokenOp(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable", "s:1", "s:0"));
            T("gfx-vrr", "Frecuencia variable para juegos", "Extiende G-Sync y FreeSync a juegos que no lo piden.", s, "Bajo", "el juego", new TokenOp(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "VRROptimizeEnable", "s:1", "s:0"));
            T("gfx-autohdr", "Apagar Auto HDR", "Se ahorra un paso de conversión de color.", s, "Bajo", "el juego", new TokenOp(@"HKCU\Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "AutoHDREnable", "s:0", "s:1"));
            T("gfx-dvr", "Apagar Game DVR", "El grabador de Windows deja de engancharse al juego.", s, "Medio", "", D(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0, 1));
            T("gfx-capture", "Apagar captura de Game Bar", "Sin captura de clips de Windows. Medal sigue igual.", s, "Medio", "", D(U + "GameDVR", "AppCaptureEnabled", 0, 1));
            T("gfx-history", "Apagar grabación en segundo plano", "Windows no graba los últimos segundos todo el tiempo.", s, "Medio", "", D(U + "GameDVR", "HistoricalCaptureEnabled", 0, 1));
            T("gfx-dvrpolicy", "Game DVR bloqueado", "Evita que una actualización lo vuelva a activar.", s, "Bajo", "el juego", D(P + "GameDVR", "AllowGameDVR", 0, null));
            T("gfx-nexus", "Game Bar no se abre con el mando", "El botón Xbox queda para el juego.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\GameBar", "UseNexusForGameBarEnabled", 0, null));
            T("gfx-panel", "Apagar avisos de Game Bar", "Sin panel de bienvenida al abrir un juego.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\GameBar", "ShowStartupPanel", 0, null));
            T("nv-power", "NVIDIA a máximo rendimiento en Fortnite", "La gráfica no baja de frecuencia durante la partida.", s, "Medio", "el juego", new NvOp("Fortnite", 0x1057EB71, 1));
            T("nv-shader", "Caché de sombreadores sin límite", "Menos tirones por recompilar. Usa más disco.", s, "Medio", "el juego", new NvOp("", 0x00AC8497, 0xFFFFFFFF));
            T("nv-filter", "Filtrado de texturas en alto rendimiento", "Menos trabajo de textura, solo en Fortnite.", s, "Bajo", "el juego", new NvOp("Fortnite", 0x00CE2691, 20));
            T("nv-optin", "Apagar telemetría del panel NVIDIA", "El panel de control no envía datos de uso.", s, "Bajo", "", D(@"HKLM\SOFTWARE\NVIDIA Corporation\NvControlPanel2\Client", "OptInOrOutPreference", 0, null));

            cat = "Energía y CPU";
            T("pw-plan", "Plan Alto rendimiento", "La CPU sube de frecuencia sin esperar. Razer Cortex lo cambia al jugar; apaga también su servicio.", s, "Medio", "", new SchemeOp("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "381b4222-f694-41f0-9685-ff5bb260df2e"));
            T("pw-min", "CPU sin bajar de frecuencia", "Estado mínimo al 100 %. Más consumo en reposo.", a, "Bajo", "", new PwrOp(Cpu, "893dee8e-2bef-41e0-89c6-b55d0929964c", 100, 5));
            T("pw-park", "Apagar aparcado de núcleos", "Todos los núcleos listos.", s, "Bajo", "", new PwrOp(Cpu, "0cc5b647-c1df-4637-891a-dec35c318583", 100, 10));
            T("pw-boost", "Turbo agresivo", "La CPU acelera antes.", s, "Bajo", "", new PwrOp(Cpu, "be337238-0d82-4146-a960-4f3749d470c7", 2, 1));
            T("pw-epp", "Preferencia de CPU en rendimiento", "El procesador prioriza velocidad sobre ahorro.", s, "Bajo", "", new PwrOp(Cpu, "36687f9e-e3a5-4dbf-b1dc-15eb381c6863", 0, 25));
            T("pw-cool", "Refrigeración activa", "Sube ventiladores antes que bajar frecuencia.", s, "Bajo", "", new PwrOp(Cpu, "94d3a615-a899-4ac5-ae2b-e4d8f634367f", 1, 0));
            T("pw-aspm", "Apagar ahorro PCIe", "La gráfica y el SSD no entran en ahorro.", s, "Bajo", "", new PwrOp("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0, 1));
            T("pw-usb", "Apagar suspensión de USB", "Ratón y teclado nunca se duermen.", s, "Bajo", "", new PwrOp(Usb, "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0, 1));
            T("pw-usb3", "Apagar ahorro de enlace USB 3", "Igual que el anterior, para puertos USB 3.", s, "Bajo", "", new PwrOp(Usb, "d4e98f31-5ffe-4ce1-be31-1b38b384c009", 0, 2));
            T("pw-wifi", "Wi-Fi sin ahorro de energía", "La antena no se duerme entre paquetes.", s, "Medio", "", new PwrOp("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1", "12bbebe6-58d6-4636-95bb-3217ef867c1a", 0, 2));
            T("pw-disk", "El disco nunca se apaga", "Sin pausas por despertar el disco.", s, "Bajo", "", new PwrOp("0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", 0, 1200));
            T("pw-sleep", "El PC no se suspende solo", "No se duerme en colas o descargas largas.", s, "Bajo", "", new PwrOp(Sleep, "29f6c1db-86da-48c5-9fdb-f2b67b1f44da", 0, 1800));
            T("pw-wake", "Apagar temporizadores de activación", "Nada despierta el PC por su cuenta.", s, "Bajo", "", new PwrOp(Sleep, "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", 0, 1));
            T("pw-throttle", "Apagar limitación de energía", "Windows no frena procesos para ahorrar.", s, "Bajo", "el PC", D(Ccs + @"Control\Power\PowerThrottling", "PowerThrottlingOff", 1, null));
            RegOp timer = D(Precise.Key, Precise.Name, 1, null);
            timer.Condition = () => { int b; return int.TryParse(RegOp.ReadValue(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber").Replace("s:", ""), out b) && b >= 22000; };
            T("pw-timer", "Temporizador de 0,5 ms al jugar", "Se aplica al abrir el juego desde Jugar, con Tweakeroo abierto. Su efecto en el retraso no está demostrado.", a, "Bajo", "el PC", timer);
            T("pw-fast", "Apagar inicio rápido", "Cada encendido arranca limpio, sin restos de la sesión anterior.", s, "Bajo", "el PC", D(Ccs + @"Control\Session Manager\Power", "HiberbootEnabled", 0, 1));
            T("pw-hvci", "Apagar integridad de memoria", "Sube FPS en juegos limitados por CPU, pero quita una defensa de Windows. Algunos anticheat la exigen.", r, "Alto", "el PC", D(Ccs + @"Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, 1));

            cat = "Retraso";
            const string sys = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", sm = Ccs + @"Control\Session Manager\Memory Management", fs = Ccs + @"Control\FileSystem", gcs = @"HKCU\System\GameConfigStore";
            T("lt-quantum", "Más CPU para la ventana activa", "Turnos de CPU cortos y con prioridad para el juego. Efecto pequeño.", a, "Bajo", "", D(Ccs + @"Control\PriorityControl", "Win32PrioritySeparation", 38, 2));
            T("lt-games", "Prioridad alta para juegos en MMCSS", "Windows atiende antes a los programas que se registran como juego. Efecto no demostrado.", a, "Bajo", "el PC", S(sys + @"\Tasks\Games", "Scheduling Category", "High", "Medium"), S(sys + @"\Tasks\Games", "SFIO Priority", "High", "Normal"), D(sys + @"\Tasks\Games", "Priority", 6, 2));
            T("lt-resp", "Menos CPU reservada para el fondo", "Baja del 20 al 10 % lo que Windows guarda para otras tareas. Efecto no demostrado.", a, "Bajo", "el PC", D(sys, "SystemResponsiveness", 10, 20));
            T("lt-net", "Sin límite de red multimedia", "Quita un tope de paquetes mientras suena audio. Puede subir el uso de CPU.", a, "Bajo", "el PC", new RegOp(sys, "NetworkThrottlingIndex", "d:4294967295", "d:10"));
            string msi = GpuPath();
            if (msi != null) T("lt-msi", "Interrupciones MSI en la gráfica", "La gráfica avisa a la CPU por un canal propio. Muchas ya vienen así.", a, "Bajo", "el PC", D(msi + @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties", "MSISupported", 1, null));
            T("lt-paging", "Núcleo de Windows siempre en memoria", "Windows no manda sus propios controladores al disco. Solo con 16 GB o más.", a, "Bajo", "el PC", D(sm, "DisablePagingExecutive", 1, 0));
            T("lt-access", "Sin anotar la fecha de lectura", "Menos escrituras al abrir archivos.", s, "Bajo", "el PC", new RegOp(fs, "NtfsDisableLastAccessUpdate", "d:2147483649", "d:2147483650"));
            T("lt-short", "Sin nombres cortos de archivo", "Windows deja de crear nombres de 8 letras para cada archivo nuevo.", a, "Bajo", "el PC", D(fs, "NtfsDisable8dot3NameCreation", 1, 2));
            T("lt-fse", "Pantalla completa clásica", "Los juegos en pantalla completa usan el modo exclusivo. No cambia los que van en ventana sin bordes.", a, "Bajo", "el juego", D(gcs, "GameDVR_FSEBehaviorMode", 2, 0), D(gcs, "GameDVR_HonorUserFSEBehaviorMode", 1, 0), D(gcs, "GameDVR_DXGIHonorFSEWindowsCompatible", 1, 0));
            T("lt-mpo", "Apagar MPO", "Arregla parpadeos y tirones con varias pantallas en algunas gráficas. Si no los hay, no aporta.", a, "Bajo", "el PC", D(@"HKLM\SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5, null));

            cat = "Ratón y teclado";
            T("in-accel", "Apagar aceleración del ratón", "Desactiva Mejorar precisión del puntero de Windows. Cambia el cursor del escritorio y de los menús, no la puntería dentro del juego, que lee el ratón directamente (raw input).", a, "Bajo", "", S(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0", "1"), S(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0", "6"), S(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0", "10"));
            T("in-sticky", "Sin Teclas especiales", "Pulsar Shift cinco veces no te saca del juego.", s, "Medio", "", new BitOp(Acc + "StickyKeys", "Flags", 4));
            T("in-filter", "Sin Teclas filtro", "Mantener Shift no activa el filtro.", s, "Medio", "", new BitOp(Acc + "Keyboard Response", "Flags", 4));
            T("in-toggle", "Sin Teclas de alternancia", "Mantener Bloq Num no abre un aviso.", s, "Bajo", "", new BitOp(Acc + "ToggleKeys", "Flags", 4));
            T("in-mousekeys", "Sin Teclas de ratón", "Alt + Shift + Bloq Num deja de abrir un aviso.", s, "Bajo", "", new BitOp(Acc + "MouseKeys", "Flags", 4));
            T("in-contrast", "Sin atajo de contraste alto", "Alt + Shift + ImprPant no cambia toda la pantalla.", s, "Bajo", "la sesión", new BitOp(Acc + "HighContrast", "Flags", 4));
            T("in-narrator", "Sin atajo del Narrador", "Win + Ctrl + Enter no lo abre.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\Narrator\NoRoam", "WinEnterLaunchEnabled", 0, null));
            T("in-lang", "Sin atajo de cambio de idioma", "Alt + Shift y Ctrl + Shift no cambian el teclado.", s, "Medio", "la sesión", S(@"HKCU\Keyboard Layout\Toggle", "Hotkey", "3", null), S(@"HKCU\Keyboard Layout\Toggle", "Language Hotkey", "3", null), S(@"HKCU\Keyboard Layout\Toggle", "Layout Hotkey", "3", null));
            T("in-delay", "Retraso de teclado mínimo", "La repetición empieza antes.", s, "Bajo", "", S(@"HKCU\Control Panel\Keyboard", "KeyboardDelay", "0", "1"));
            T("in-speed", "Repetición de teclado máxima", "Repite más rápido al mantener una tecla.", s, "Bajo", "", S(@"HKCU\Control Panel\Keyboard", "KeyboardSpeed", "31", "20"));

            cat = "Red";
            T("net-p2p", "Sin compartir actualizaciones", "Tu PC no sube actualizaciones a otros.", s, "Medio", "", D(P + "DeliveryOptimization", "DODownloadMode", 0, null));
            T("net-bg", "Descargas de fondo al 20 %", "Las actualizaciones no afectan al ping.", s, "Medio", "", D(P + "DeliveryOptimization", "DOPercentageMaxBackgroundBandwidth", 20, null));
            T("net-eee", "Ethernet: apagar ahorro EEE", "El puerto no se duerme. Solo por cable.", s, "Bajo", "el PC", new NicOp("*EEE", "0"));
            T("net-green", "Ethernet: apagar Green Ethernet", "Potencia de señal constante. Solo por cable.", s, "Bajo", "el PC", new NicOp("EnableGreenEthernet", "0"));
            T("net-giga", "Ethernet: apagar Giga Lite", "Evita enlaces degradados. Solo por cable.", s, "Bajo", "el PC", new NicOp("GigaLite", "0"));
            T("net-im", "Ethernet: apagar moderación de interrupciones", "Menos espera por paquete a cambio de CPU. Solo por cable.", a, "Bajo", "el PC", new NicOp("*InterruptModeration", "0"));
            T("net-flow", "Ethernet: apagar control de flujo", "El router no puede pausar tu tráfico. Solo por cable.", a, "Bajo", "el PC", new NicOp("*FlowControl", "0"));
            T("net-roam", "Wi-Fi: sin cambiar de antena", "La tarjeta busca menos otras antenas, que es cuando hay tirones. Solo tarjetas Realtek y si el PC no se mueve.", s, "Medio", "el PC", new NicOp("RegRoamLevel", "1"));
            T("net-roam-intel", "Wi-Fi Intel: sin cambiar de antena", "La tarjeta solo busca otra antena si la señal cae mucho. Solo tarjetas Intel y si el PC no se mueve.", s, "Medio", "el PC", new NicDescOp("8086", "Roaming Aggressiveness", "RoamAggressiveness", "1."));
            const string q = P + @"QoS\Tweakeroo Fortnite";
            T("net-qos", "Prioridad de red para Fortnite", "Marca los paquetes del juego como urgentes. Solo ayuda si el router respeta la marca.", a, "Bajo", "el PC",
                S(q, "Version", "1.0", null), S(q, "Application Name", "FortniteClient-Win64-Shipping.exe", null), S(q, "Protocol", "UDP", null), S(q, "Local Port", "*", null), S(q, "Local IP", "*", null), S(q, "Local IP Prefix Length", "*", null),
                S(q, "Remote Port", "*", null), S(q, "Remote IP", "*", null), S(q, "Remote IP Prefix Length", "*", null), S(q, "DSCP Value", "46", null), S(q, "Throttle Rate", "-1", null), S(Ccs + @"Services\Tcpip\QoS", "Do not use NLA", "1", null));
            T("net-wol", "Apagar activación por red", "La tarjeta no queda escuchando para despertar el PC.", s, "Bajo", "el PC", new NicOp("*WakeOnMagicPacket", "0"), new NicOp("*WakeOnPattern", "0"));

            cat = "Segundo plano";
            T("bg-apps", "Apps de la Tienda sin segundo plano", "Dejan de correr cerradas. Pueden no mostrar avisos.", a, "Medio", "la sesión", D(P + "AppPrivacy", "LetAppsRunInBackground", 2, null));
            T("bg-widgets", "Apagar widgets", "Quita el panel de noticias y sus procesos.", s, "Medio", "la sesión", D(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null));
            T("bg-edgeboost", "Edge no arranca con Windows", "Edge deja de cargarse antes de abrirlo.", s, "Medio", "Edge", D(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, null));
            T("bg-edge", "Edge no sigue abierto al cerrarlo", "Sin procesos de Edge de fondo.", s, "Medio", "Edge", D(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, null));
            T("bg-edgetab", "Edge no precarga pestañas", "Menos memoria usada por adelantado.", s, "Bajo", "Edge", D(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "NewTabPagePrerenderEnabled", 0, null));
            T("bg-chrome", "Chrome no sigue abierto al cerrarlo", "Sin procesos de Chrome de fondo.", s, "Medio", "Chrome", IfFile(D(@"HKLM\SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 0, null), @"%ProgramFiles%\Google\Chrome\Application\chrome.exe"));
            T("bg-brave", "Brave no sigue abierto al cerrarlo", "Sin procesos de Brave de fondo.", s, "Medio", "Brave", IfFile(D(@"HKLM\SOFTWARE\Policies\BraveSoftware\Brave", "BackgroundModeEnabled", 0, null), @"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe"));
            T("bg-restart", "No reabrir apps al iniciar sesión", "Windows no reabre lo que tenías antes.", s, "Medio", "", D(@"HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon", "RestartApps", 0, null));
            T("bg-recall", "Apagar Recall", "Windows no guarda capturas de lo que haces.", s, "Medio", "el PC", D(PU + "WindowsAI", "DisableAIDataAnalysis", 1, null), D(P + "WindowsAI", "AllowRecallEnablement", 0, null));
            T("bg-click", "Apagar Click to Do", "Sin análisis de pantalla por IA.", s, "Bajo", "la sesión", D(PU + "WindowsAI", "DisableClickToDo", 1, null));
            T("bg-copilot", "Apagar Copilot", "Quita Copilot de Windows.", s, "Bajo", "la sesión", D(PU + "WindowsCopilot", "TurnOffWindowsCopilot", 1, null));
            T("bg-resume", "Apagar continuar desde el móvil", "Windows no busca tu móvil para continuar tareas.", s, "Bajo", "la sesión", D(U + @"CrossDeviceResume\Configuration", "IsResumeAllowed", 0, null));
            T("bg-mobile", "Apagar vínculo con móvil", "Sin sincronización entre dispositivos.", s, "Bajo", "la sesión", D(U + "Mobility", "OptedIn", 0, null), D(U + "Mobility", "CrossDeviceEnabled", 0, null));
            T("bg-clip", "Portapapeles sin nube", "Lo que copias se queda en este PC.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\Clipboard", "CloudClipboardAutomaticUpload", 0, null));
            T("bg-maps", "Mapas sin descargas automáticas", "Sin tráfico de mapas sin conexión.", s, "Bajo", "", D(P + "Maps", "AutoDownloadAndUpdateMapData", 0, null), D(P + "Maps", "AllowUntriggeredNetworkTrafficOnSettingsPage", 0, null));
            T("bg-drivers", "Windows Update no cambia controladores", "No reemplaza el controlador de la gráfica. Se actualiza a mano.", a, "Medio", "", D(P + "WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1, null));
            T("bg-coinst", "Sin instaladores de periféricos", "Conectar un ratón no instala su programa solo.", s, "Bajo", "", D(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer", "DisableCoInstallers", 1, null));
            T("bg-wpbt", "La placa no instala programas", "Bloquea el software que la BIOS mete en Windows.", s, "Bajo", "el PC", D(Ccs + @"Control\Session Manager", "DisableWpbtExecution", 1, null));
            T("bg-maint", "El mantenimiento no despierta el PC", "Se hace igual, pero con el PC encendido.", s, "Bajo", "", D(P + @"Task Scheduler\Maintenance", "WakeUp", 0, null));

            cat = "Servicios";
            T("sv-sysmain", "Apagar SysMain", "Precarga programas en memoria. Con un SSD rápido aporta poco.", a, "Bajo", "", new SvcOp("SysMain", 2));
            T("sv-search", "Apagar índice de búsqueda", "Menos lecturas de disco; la búsqueda del menú va más lenta.", a, "Bajo", "", new SvcOp("WSearch", 2));
            T("sv-diag", "Apagar telemetría de Windows", "El servicio que recoge y envía diagnósticos.", s, "Bajo", "", new SvcOp("DiagTrack", 2));
            T("sv-inv", "Apagar inventario de compatibilidad", "Revisa qué programas tienes instalados.", s, "Bajo", "", new SvcOp("InventorySvc", 2));
            T("sv-trk", "Apagar seguimiento de vínculos", "Vigila accesos directos movidos entre discos.", s, "Bajo", "", new SvcOp("TrkWks", 2));
            T("sv-geo", "Apagar geolocalización", "Las apps no sabrán dónde estás.", s, "Bajo", "", new SvcOp("lfsvc", 3));
            T("sv-ai", "Apagar servicio de IA local", "Infraestructura de funciones de IA de Windows.", s, "Bajo", "", new SvcOp("WSAIFabricSvc", 2));
            T("sv-pca", "Apagar asistente de compatibilidad", "Vigila programas antiguos mientras se ejecutan.", a, "Bajo", "", new SvcOp("PcaSvc", 2));
            T("sv-print", "Apagar cola de impresión", "Solo si no usas impresora ni imprimes a PDF.", a, "Bajo", "", new SvcOp("Spooler", 2));
            T("sv-dusm", "Apagar contador de datos de red", "Ajustes deja de mostrar el consumo por app.", a, "Bajo", "", new SvcOp("DusmSvc", 2));
            T("sv-chroma", "Apagar Razer Chroma SDK", "Sin efectos de luces dentro de los juegos.", a, "Bajo", "", new SvcOp("Razer Chroma SDK Server", 2), new SvcOp("Razer Chroma SDK Service", 2), new SvcOp("Razer Chroma Stream Server", 2), new SvcOp("Razer Chroma SDK Diagnostic Service", 2));
            T("sv-cortex", "Razer Cortex no arranca solo", "Evita que Cortex aplique sus propios tweaks encima.", a, "Medio", "", new SvcOp("CortexLauncherService", 2));
            T("sv-lghub", "Apagar actualizador de G HUB", "Las actualizaciones pasan a ser a mano. Sin este servicio, G HUB puede no arrancar y entonces no aplica el DPI ni los perfiles del ratón.", r, "Bajo", "", new SvcOp("LGHUBUpdaterService", 2));

            T("sv-maps", "Apagar descarga de mapas", "Mapas sin conexión de Windows.", s, "Bajo", "", new SvcOp("MapsBroker", 2));
            T("sv-retail", "Apagar modo tienda", "Solo sirve en ordenadores de exposición.", s, "Bajo", "", new SvcOp("RetailDemo", 3));
            T("sv-wer", "Apagar servicio de informes de error", "Los fallos no se preparan para enviarse.", s, "Bajo", "", new SvcOp("WerSvc", 3));
            T("sv-push", "Apagar mensajes WAP", "Canal de telemetría para móviles.", s, "Bajo", "", new SvcOp("dmwappushservice", 3));
            T("sv-phone", "Apagar servicio de teléfono", "Solo para PC con tarjeta SIM.", s, "Bajo", "", new SvcOp("PhoneSvc", 3));
            T("sv-xbox", "Apagar guardado y red de Xbox", "Solo si no se usa Game Pass ni juegos de la Tienda.", a, "Bajo", "", new SvcOp("XblGameSave", 3), new SvcOp("XboxNetApiSvc", 3));
            T("sv-remote", "Apagar registro remoto", "Nadie cambia el registro desde otro equipo.", s, "Bajo", "", new SvcOp("RemoteRegistry", 3));

            cat = "Tareas programadas";
            T("tk-pca", "Apagar base de compatibilidad", "Actualiza datos de programas antiguos.", s, "Bajo", "", K(@"Application Experience\PcaPatchDbTask"));
            T("tk-sdb", "Apagar mezcla de compatibilidad", "Junta bases de datos de compatibilidad.", s, "Bajo", "", K(@"Application Experience\SdbinstMergeDbTask"));
            T("tk-disk", "Apagar recogida de datos de disco", "Reúne datos del estado del disco para enviarlos.", s, "Bajo", "", K(@"DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"));
            T("tk-net", "Apagar recogida de datos de red", "Guarda información de red para diagnósticos.", s, "Bajo", "", K(@"NetTrace\GatherNetworkInfo"));
            T("tk-cloud", "Apagar tarea de bienvenida en la nube", "Parte de la pantalla de configuración de Windows.", s, "Bajo", "", K(@"CloudExperienceHost\CreateObjectTask"));
            T("tk-appraiser", "Apagar evaluador de compatibilidad", "Analiza todo el PC y puede usar mucha CPU.", s, "Medio", "", K(@"Application Experience\Microsoft Compatibility Appraiser"), K(@"Application Experience\Microsoft Compatibility Appraiser Exp"));
            T("tk-mare", "Apagar copia de datos de compatibilidad", "Recoge datos de tus programas.", s, "Bajo", "", K(@"Application Experience\MareBackup"));
            T("tk-startup", "Apagar análisis de apps de inicio", "Revisa qué arranca con Windows.", s, "Bajo", "", K(@"Application Experience\StartupAppTask"));
            T("tk-autochk", "Apagar envío de datos de disco", "Manda datos de comprobación de disco.", s, "Bajo", "", K(@"Autochk\Proxy"));
            T("tk-ceip", "Apagar programa de experiencia del cliente", "Recoge y envía uso de Windows.", s, "Bajo", "", K(@"Customer Experience Improvement Program\Consolidator"), K(@"Customer Experience Improvement Program\UsbCeip"));
            T("tk-device", "Apagar censo del dispositivo", "Envía datos del hardware.", s, "Bajo", "", K(@"Device Information\Device"), K(@"Device Information\Device User"));
            T("tk-siuf", "Apagar encuestas de Windows", "Prepara las preguntas de opinión.", s, "Bajo", "", K(@"Feedback\Siuf\DmClient"), K(@"Feedback\Siuf\DmClientOnScenarioDownload"));
            T("tk-usage", "Apagar informes de uso de funciones", "Informes de uso de funciones.", s, "Bajo", "", K(@"Flighting\FeatureConfig\UsageDataReporting"), K(@"Flighting\FeatureConfig\UsageDataFlushing"), K(@"Flighting\FeatureConfig\UsageDataReceiver"));
            T("tk-winsat", "Apagar prueba de rendimiento WinSAT", "Un benchmark de fondo que puede saltar en cualquier momento.", s, "Medio", "", K(@"Maintenance\WinSAT"));
            T("tk-power", "Apagar análisis de eficiencia energética", "Mide el consumo del PC en segundo plano.", s, "Bajo", "", K(@"Power Efficiency Diagnostics\AnalyzeSystem"));
            T("tk-maps", "Apagar avisos de mapas", "Avisos de la app Mapas.", s, "Bajo", "", K(@"Maps\MapsToastTask"));
            T("tk-applist", "Apagar copia de la lista de apps", "Sube qué apps tienes a tu cuenta.", s, "Bajo", "", K(@"AppListBackup\Backup"), K(@"AppListBackup\BackupNonMaintenance"));
            T("tk-wer", "Apagar envío de informes de error", "Manda fallos acumulados.", s, "Bajo", "", K(@"Windows Error Reporting\QueueReporting"));
            T("tk-family", "Apagar tareas de control parental", "Tareas de seguridad familiar. No recomendado con Family Safety.", a, "Bajo", "", K(@"Shell\FamilySafetyMonitor"), K(@"Shell\FamilySafetyRefreshTask"));
            T("tk-theme", "Apagar descarga de imágenes de temas", "Baja fondos sincronizados.", s, "Bajo", "", K(@"Shell\ThemesSyncedImageDownload"));

            cat = "Interrupciones";
            T("nt-toast", "Apagar notificaciones emergentes", "Nada salta encima del juego. Discord usa las suyas.", s, "Medio", "la sesión", D(U + "PushNotifications", "ToastEnabled", 0, null));
            T("nt-suggest", "Sin notificaciones de sugerencias", "Windows no recomienda cosas.", s, "Bajo", "", D(U + @"Notifications\Settings\Windows.SystemToast.Suggested", "Enabled", 0, null));
            T("nt-backup", "Sin recordatorios de copia", "No insiste con OneDrive.", s, "Bajo", "", D(U + @"Notifications\Settings\Windows.SystemToast.BackupReminder", "Enabled", 0, null));
            T("nt-scoobe", "Sin pantalla de terminar configuración", "No aparece tras actualizar.", s, "Medio", "", D(U + "UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, null));
            T("nt-welcome", "Sin bienvenida tras actualizar", "No abre la experiencia de novedades.", s, "Bajo", "", D(Cdm, "SubscribedContent-310093Enabled", 0, null));
            T("nt-tips", "Sin consejos de Windows", "Quita trucos y consejos.", s, "Bajo", "", D(Cdm, "SubscribedContent-338389Enabled", 0, null), D(Cdm, "SoftLandingEnabled", 0, null));
            T("nt-account", "Sin avisos de cuenta en Inicio", "El menú no pide copias ni suscripciones.", s, "Bajo", "", D(Adv, "Start_AccountNotifications", 0, null));
            T("nt-account2", "Sin avisos de cuenta en Ajustes", "Ajustes no muestra ofertas de cuenta.", s, "Bajo", "", D(U + @"SystemSettings\AccountNotifications", "EnableAccountNotifications", 0, null));
            T("nt-sync", "Sin publicidad en el Explorador", "Quita avisos del proveedor de nube.", s, "Bajo", "", D(Adv, "ShowSyncProviderNotifications", 0, null));
            T("nt-feedback", "Sin preguntas de opinión", "Windows no pide valoraciones.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null), D(P + "DataCollection", "DoNotShowFeedbackNotifications", 1, null));
            T("nt-ducking", "El volumen no baja con llamadas", "El juego no se silencia cuando hablan.", s, "Medio", "", D(@"HKCU\Software\Microsoft\Multimedia\Audio", "UserDuckingPreference", 3, null));
            T("nt-autoplay", "Apagar reproducción automática", "Conectar un USB no abre ventanas.", s, "Bajo", "", D(U + @"Explorer\AutoplayHandlers", "DisableAutoplay", 1, null));
            T("nt-chime", "Sin sonido de inicio", "Arranque en silencio.", s, "Bajo", "", D(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "DisableStartupSound", 1, null));

            cat = "Publicidad";
            T("ad-silent", "Sin apps instaladas solas", "Windows no instala juegos promocionados.", s, "Medio", "", D(Cdm, "SilentInstalledAppsEnabled", 0, null));
            T("ad-pre", "Sin apps preinstaladas nuevas", "Las cuentas nuevas no traen extras.", s, "Bajo", "", D(Cdm, "PreInstalledAppsEnabled", 0, null), D(Cdm, "OemPreInstalledAppsEnabled", 0, null), D(Cdm, "PreInstalledAppsEverEnabled", 0, null));
            T("ad-consumer", "Sin contenido promocionado", "Bloquea el contenido para consumidores.", s, "Bajo", "la sesión", D(P + "CloudContent", "DisableWindowsConsumerFeatures", 1, null));
            T("ad-delivery", "Sin entrega de contenido", "Apaga el canal que trae sugerencias.", s, "Bajo", "", D(Cdm, "ContentDeliveryAllowed", 0, null));
            T("ad-start", "Sin sugerencias en Inicio", "El menú muestra solo lo tuyo.", s, "Bajo", "la sesión", D(Cdm, "SystemPaneSuggestionsEnabled", 0, null), D(Cdm, "SubscribedContent-338388Enabled", 0, null), D(Adv, "Start_IrisRecommendations", 0, null));
            T("ad-settings", "Sin sugerencias en Ajustes", "Ajustes deja de recomendar.", s, "Bajo", "", D(Cdm, "SubscribedContent-338393Enabled", 0, null), D(Cdm, "SubscribedContent-353694Enabled", 0, null), D(Cdm, "SubscribedContent-353696Enabled", 0, null));
            T("ad-lock", "Sin datos en la pantalla de bloqueo", "Quita curiosidades y consejos.", s, "Bajo", "", D(Cdm, "RotatingLockScreenOverlayEnabled", 0, null), D(Cdm, "SubscribedContent-338387Enabled", 0, null));
            T("ad-third", "Sin sugerencias de terceros", "Spotlight no recomienda otras marcas.", s, "Bajo", "la sesión", D(PU + "CloudContent", "DisableThirdPartySuggestions", 1, null));
            T("ad-id", "Apagar identificador de publicidad", "Las apps no te siguen para anuncios.", s, "Bajo", "", D(U + "AdvertisingInfo", "Enabled", 0, null), D(P + "AdvertisingInfo", "DisabledByGroupPolicy", 1, null));
            T("ad-tailored", "Sin experiencias personalizadas", "No usa tus diagnósticos para anuncios.", s, "Bajo", "", D(U + "Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, null));
            T("ad-sites", "Sin webs recomendadas en Inicio", "El menú no mira tu historial de Edge.", s, "Bajo", "la sesión", D(P + "Explorer", "HideRecommendedPersonalizedSites", 1, null));

            cat = "Búsqueda";
            T("se-bing", "Inicio no busca en Bing", "Resultados locales y más rápidos.", s, "Medio", "la sesión", D(U + "Search", "BingSearchEnabled", 0, null), D(PU + "Explorer", "DisableSearchBoxSuggestions", 1, null));
            T("se-highlights", "Sin destacados de búsqueda", "El cuadro no descarga imágenes del día.", s, "Bajo", "la sesión", D(U + "SearchSettings", "IsDynamicSearchBoxEnabled", 0, null));
            T("se-cloud", "La búsqueda no mira la nube", "No consulta tu cuenta al escribir.", s, "Bajo", "", D(U + "SearchSettings", "IsMSACloudSearchEnabled", 0, null), D(U + "SearchSettings", "IsAADCloudSearchEnabled", 0, null));
            T("se-history", "Sin historial de búsqueda", "No guarda lo que buscas.", s, "Bajo", "", D(U + "SearchSettings", "IsDeviceSearchHistoryEnabled", 0, null));

            cat = "Telemetría";
            T("tl-level", "Diagnóstico al mínimo", "Solo los datos obligatorios de tu edición.", s, "Bajo", "el PC", D(P + "DataCollection", "AllowTelemetry", 0, null));
            T("tl-name", "Sin nombre del PC en diagnósticos", "No envía el nombre del equipo.", s, "Bajo", "", D(P + "DataCollection", "AllowDeviceNameInTelemetry", 0, null));
            T("tl-logs", "Sin registros extra", "Limita registros de diagnóstico.", s, "Bajo", "", D(P + "DataCollection", "LimitDiagnosticLogCollection", 1, null));
            T("tl-dumps", "Sin volcados completos", "Limita volcados de memoria enviados.", s, "Bajo", "", D(P + "DataCollection", "LimitDumpCollection", 1, null));
            T("tl-logger", "Apagar rastreo DiagTrack", "No graba eventos desde el arranque.", s, "Bajo", "el PC", IfValue(D(Ccs + @"Control\WMI\Autologger\Diagtrack-Listener", "Start", 0, 1)));
            T("tl-ait", "Apagar telemetría de aplicaciones", "No mide qué programas usas.", s, "Bajo", "", D(P + "AppCompat", "AITEnable", 0, null));
            T("tl-inv", "Apagar recolector de inventario", "No envía la lista de programas.", s, "Bajo", "", D(P + "AppCompat", "DisableInventory", 1, null));
            T("tl-steps", "Apagar grabador de pasos", "No captura pasos del usuario.", s, "Bajo", "", D(P + "AppCompat", "DisableUAR", 1, null));
            T("tl-ceip", "Apagar experiencia del cliente", "Política del programa CEIP.", s, "Bajo", "", D(@"HKLM\SOFTWARE\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0, null));
            T("tl-wer", "Apagar informes de error", "Un juego que falla se cierra sin esperar al informe.", s, "Bajo", "", D(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, null));
            T("tl-activity", "Apagar historial de actividad", "No registra ni sube lo que abres.", s, "Bajo", "", D(P + "System", "EnableActivityFeed", 0, null), D(P + "System", "PublishUserActivities", 0, null), D(P + "System", "UploadUserActivities", 0, null));
            T("tl-typing", "Sin datos de escritura", "No mejora el teclado con lo que escribes.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\Input\TIPC", "Enabled", 0, null), D(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, null), D(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, null));
            T("tl-contacts", "Sin leer contactos para predicción", "El teclado no revisa tus contactos.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\InputPersonalization\TrainedDataStore", "HarvestContacts", 0, null));
            T("tl-speech", "Apagar voz en línea", "No envía audio a la nube.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0, null));
            T("tl-voice", "Apagar activación por voz", "Ninguna app escucha de fondo.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\Speech_OneCore\Settings\VoiceActivation\UserPreferenceForAllApps", "AgentActivationEnabled", 0, null));
            T("tl-input", "Apagar estadísticas de escritura", "Sin Typing Insights.", s, "Bajo", "", D(@"HKCU\Software\Microsoft\Input\Settings", "InsightsEnabled", 0, null));

            cat = "Interfaz";
            T("ui-transp", "Apagar transparencias", "Menos trabajo de GPU en el escritorio.", s, "Bajo", "", D(U + @"Themes\Personalize", "EnableTransparency", 0, 1));
            T("ui-anim", "Apagar animaciones de ventanas", "Minimizar y cambiar de ventana es instantáneo.", s, "Bajo", "", S(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1"));
            T("ui-task", "Apagar animaciones de la barra", "La barra de tareas responde sin animación.", s, "Bajo", "la sesión", D(Adv, "TaskbarAnimations", 0, 1));
            T("ui-menu", "Menús sin espera", "Se abren a los 50 ms en vez de 400.", s, "Bajo", "", S(@"HKCU\Control Panel\Desktop", "MenuShowDelay", "50", "400"));
            T("ui-shadow", "Sin sombras en iconos", "Escritorio más ligero.", s, "Bajo", "la sesión", D(Adv, "ListviewShadow", 0, 1));
            T("ui-select", "Selección sin transparencia", "Arrastrar en el escritorio va más ligero.", s, "Bajo", "la sesión", D(Adv, "ListviewAlphaSelect", 0, 1));
            T("ui-peek", "Apagar vista previa del escritorio", "Sin miniatura al rozar la esquina.", s, "Bajo", "la sesión", D(@"HKCU\Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0, 1));
            T("ui-startup", "Apps de inicio sin espera", "Windows no retrasa tus programas al arrancar.", s, "Bajo", "la sesión", D(U + @"Explorer\Serialize", "StartupDelayInMSec", 0, null));

            // No effect (siguiente-sesion\tweaks-nuevos.md): 38 is the client default of Win32PrioritySeparation; MMCSS ignores
            // SFIO Priority and treats Priority as 2 with High; the network limit is 10 packets/ms, far above a game's.
            // They are no longer offered, but whoever has one applied still sees it and can undo it.
            foreach (Tweak t in list.Where(x => x.Id == "lt-quantum" || x.Id == "lt-games" || x.Id == "lt-net")) t.Hidden = true;
            Gains.Fill(list);
            return list;
        }
    }
}
