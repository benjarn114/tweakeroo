<p align="center"><img src="logo.png" width="128" alt="Tweakeroo logo, a kangaroo"></p>

<h1 align="center">Tweakeroo</h1>

<p align="center">A free Windows app that tunes your PC for Fortnite, opens the game in stretched resolution and lets you undo every tweak.</p>

<p align="center"><a href="https://github.com/benjarn114/tweakeroo/releases/latest/download/Tweakeroo.exe"><b>Download Tweakeroo.exe</b></a> · <a href="https://github.com/benjarn114/tweakeroo/releases">All versions</a></p>

![The Play screen](docs/play.png)

## What it is

Tweakeroo is a single `.exe` with more than 170 Windows and Fortnite settings, each with its own switch. It scans your parts, tells you which settings suit your PC and why, and saves a copy of your original state before it changes anything.

I vibe-coded it: I had the ideas, tested it on my own PC, and built it with AI. Making it cost me little, so I'm giving it away. There are no ads, no accounts, no analytics and no locked features.

## What it does

### Play
- Opens Fortnite at the resolution you pick. When the game closes, your screen and Fortnite's settings are restored.
- Stretched resolutions from a list, or your own on NVIDIA cards. A new one is tested first: if you don't confirm it within 15 seconds, it goes back.
- Before and after photos show how much wider characters get.
- FPS limit, estimated FPS, your last match and the programs using the most memory.

### My PC
- Reads your GPU, CPU, memory, disk, network and screen, then recommends only what fits. For example, it skips battery-draining settings on laptops and wired settings on Wi-Fi.
- The scan is saved and refreshed in the background each time you open the app.

### My FPS
- Real FPS for each match, read from the log files Fortnite already writes. It never touches the game's memory.
- Compares your FPS before and after your last big change.

### Upgrades
- Free fixes you may be missing: a monitor running below its maximum Hz, memory without EXPO/XMP, an old driver.
- What to buy next, based on your bottleneck. If nothing is worth buying, it says so.

### Tweaks
| Category | Examples |
| --- | --- |
| Fortnite | Performance mode, VSync off, Reflex, shadows and effects on low |
| Graphics and capture | Game Mode, Game DVR off, NVIDIA maximum performance for Fortnite |
| Power and CPU | High performance plan, core parking off, USB sleep off |
| Mouse and keyboard | Pointer acceleration off, Sticky Keys shortcut off |
| Network | Card power saving off, background download limits |
| Background, services and tasks | Less running while you play |
| Interruptions, ads, search, telemetry | Fewer pop-ups, suggestions and reports |

Each tweak shows its expected impact. Settings marked **Risk** lower a Windows defense, such as memory integrity, and are never turned on in bulk.

![The list of tweaks](docs/tweaks.png)

### Settings
- Open with Windows, start minimized, or stay next to the clock when closed.
- Language, opening section, animations, FPS limit and how long you have to confirm a resolution.
- Update checks, export and import, clear data, go back to Original, and the full version history.

### Macros (Creative only)
Optional edit macros for freebuilding: quick edit, double edit, edit and reset, and reset. They are off by default and only send keys when Fortnite is in front and its log says you are in Creative.

> **Use at your own risk.** Epic forbids macros in tournaments, and using them, also in Creative, can lead to account penalties. Tweakeroo doesn't hide them or avoid detection.

## Is it safe?

Don't take my word for it:

- **Scan it.** Upload the `.exe` to [VirusTotal](https://www.virustotal.com) before running it. Some engines may flag any unsigned app that asks for administrator rights, so look at what they say and not only at the number.
- **Read it.** All the source code is here in [`src`](src).
- **Build it.** One command with the compiler that comes with Windows (see below). Nothing to install.
- **Check the file.** GitHub shows a SHA-256 next to each download. Compare it with `Get-FileHash Tweakeroo.exe`.
- **Undo it.** Turn off any switch, use *Undo*, or restore the *Original* copy. The uninstaller also offers to restore it.

**Why does Windows warn me?** The app isn't signed with a paid certificate, so SmartScreen says *unknown publisher*. Choose *More info → Run anyway*, after checking the file if you want.

**Why does it ask for administrator rights?** It changes the registry, services, scheduled tasks, the power plan, the network card and the NVIDIA profile.

**What does it connect to?** Only GitHub, to look for and download updates. Every update is signed and the app checks the signature before installing it. You see what's new and decide. Your data stays in `%LOCALAPPDATA%\Tweakeroo`. Launching Fortnite uses Epic's launcher.

## Install

1. Download [Tweakeroo.exe](https://github.com/benjarn114/tweakeroo/releases/latest/download/Tweakeroo.exe) and open it.
2. Read what it does and the terms, then install.
3. In **My PC**, press *Scan PC* and choose what to turn on.

Windows 10 or 11, 64-bit. English by default; switch to Spanish in **Settings**, or in the installer. It has only been tested on one PC (Windows 11, NVIDIA), so other setups may behave differently.

## Honest limits

- No FPS gain is promised. Estimates are approximate and some tweaks do little on a fast PC.
- The 0.5 ms timer tweak is there because people ask for it. Its effect on delay isn't proven.
- Tournaments may force 16:9 or forbid some settings. Check the rules before you compete.
- It isn't a replacement for a backup.

## Build it yourself

```powershell
git clone https://github.com/benjarn114/tweakeroo
cd tweakeroo
powershell -ExecutionPolicy Bypass -File build.ps1
```

It uses `csc.exe` from .NET Framework 4.8, included in Windows 10 and 11. The code is C# and WPF, with no external packages. Comments and text keys are in Spanish; the English text is in `src/en.txt`.

| File | What it does |
| --- | --- |
| `Catalog.cs` | Every tweak and the exact values it changes |
| `Ops.cs`, `Engine.cs` | Reading, writing, checking and restoring settings |
| `Advisor.cs`, `Review.cs` | Scan and recommendations |
| `Stretch.cs` | Resolutions and launching the game |
| `Fps.cs` | FPS from the logs |
| `Macros.cs` | Creative macros |
| `Update.cs`, `Setup.cs` | Signed updates, install and uninstall |
| `SelfTest.cs`, `ClickTest.cs` | Automatic tests |

## License

The source is public so anyone can read it, check it and build it for personal use. Every version you get for free is yours to use forever. Selling it or republishing it isn't allowed. See [LICENSE](LICENSE).

## Disclaimer

Tweakeroo is not affiliated with or endorsed by Epic Games, Microsoft, NVIDIA, AMD or Intel. Fortnite and its images belong to Epic Games. Changing system settings can cause problems; you use the app at your own responsibility.
