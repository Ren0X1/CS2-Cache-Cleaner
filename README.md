# 🧹 CS2 Cache Cleaner

![C#](https://img.shields.io/badge/C%23-.NET%2010-512BD4?style=flat-square&logo=dotnet&logoColor=white) ![WPF](https://img.shields.io/badge/UI-WPF-5FD68A?style=flat-square) ![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat-square&logo=windows&logoColor=white) ![Counter-Strike 2](https://img.shields.io/badge/Counter--Strike%202-shader%20cache-F5A623?style=flat-square&logo=counterstrike&logoColor=white) ![Google Drive](https://img.shields.io/badge/Google%20Drive-userdata%20backup-4285F4?style=flat-square&logo=googledrive&logoColor=white) ![NVIDIA](https://img.shields.io/badge/NVIDIA-Profile%20Inspector-76B900?style=flat-square&logo=nvidia&logoColor=white)

**RNX Cache Cleaner Pro** frees disk space and gets rid of the stutter that builds up on a gaming PC. It wipes Windows temp files, caches and logs, clears the **CS2 shader cache** and the GPU driver cache, trims the SSD, applies a CS2-optimized **NVIDIA profile** and your mouse settings, and backs up your **Steam game configs to Google Drive** — one click, no digging through Windows settings.

Since **v5** it is a real Windows app: a single `.exe` with a graphical interface, an MSI installer and automatic updates. 🚀

---

## ⚡ Quick start

1. ⬇️ Go to [**Releases**](https://github.com/Ren0X1/CS2-Cache-Cleaner/releases/latest) and download **`RNX_Cache_Cleaner_<version>.msi`** (recommended) or the portable **`RNX_Cache_Cleaner.exe`**.
2. 🖱️ Install it (per-user, no admin needed to install). It opens by itself at the end and asks for administrator rights, which the cleanup needs.
3. 🧽 Pick **Quick**, **Full** or **Everything** and watch the progress live.
4. 🔁 When it finishes it tells you how much space was freed and recommends a restart.

No .NET install required: everything is bundled in the `.exe`.

> ⚠️ The installer is not code-signed yet, so Windows SmartScreen may warn the first time: **More info → Run anyway**.

---

## 🖥️ The app

| Section | What it does |
|---------|--------------|
| **Cleanup** | Three one-click modes: **Quick** (7 areas), **Full** (17 areas + SSD TRIM + NVIDIA profile) and **Everything** (Full + shader cache + mouse config + NVIDIA). Also the Google Drive backup switch and desktop shortcut buttons |
| **Custom** | Tick exactly what you want from the 18 areas and the 3 CS2 modules. Your selection is remembered |
| **CS2 modules** | Shader cache, mouse config (`raton.reg`) and NVIDIA CS2 profile one by one, plus a button to open NVIDIA Profile Inspector |
| **Backup** | Google Drive sign-in, folder, size limit and excluded folders. *Back up now* button |
| **History** | Chart of the space freed by each run, totals and a table of every run |
| **Log** | Detailed log of the last run |

While a cleanup runs you see every step with its status (`[ OK ]`, `[WARN]`, `[FAIL]`), a live log and the space freed so far. You can cancel at any time.

---

## 🛡️ Valve server blocker (CS2)

Don't want to play on Madrid, Paris or any other location? The **Servers** tab lists every Valve server location CS2 matchmaking uses, with its region, number of IPs, live ping and status, and a switch to block or allow each one.

- 📡 **Live list**: locations and IPs come straight from Steam's official API (`ISteamApps/GetSDRConfig`, app 730), the same source used by [CS2 Server Picker](https://github.com/FN-FAL113/cs2-server-picker) and [Server Picker X](https://github.com/FN-FAL113/server-picker-x). A copy is saved so it also works offline. Perfect World (China) locations are left out.
- 🧱 **Windows Firewall rules**: blocking a location creates one outbound block rule named `RNX_SDR_<code>` (for example `RNX_SDR_mad`) in the group *RNX Cache Cleaner*, with all of that location's IPs. Unblocking deletes it. CS2 then can't reach those relays and matchmaking skips them.
- ✅ **Status read from the firewall**: Blocked / Allowed is always taken from the rules that actually exist, not from a file, so it stays correct even if you edit the firewall by hand.
- 🔄 **Self-updating rules**: if Valve changes the IPs of a location you blocked, its rule is rebuilt the next time you open the tab.
- 🧹 **Unblock all** removes only the rules created by RNX Cache Cleaner; your other firewall rules are never touched.
- ⚡ **Quick setup**: keep only the 3, 5 or 10 best locations by ping (the app opens them all for a moment to measure, then asks before applying), or play only in one region.

Changes apply to your next matchmaking search.

---

## 🌍 Languages, colors and appearance

Besides the built-in palettes you can pick a **custom color** (color picker or hex code, with live preview); RNX adjusts its shade automatically for light and dark mode.

On first launch you choose your **language** (English by default, Spanish, Russian, German, French or Chinese), your **color** (Amber by default, Cobalt, Signal orange, Raspberry, Ocean or Graphite) and the **appearance**: Light, Dark or **System**, which follows Windows' light/dark mode live. All three can be changed later from the buttons at the bottom of the sidebar. Everything is translated, including the log.

---

## 🤖 Command-line modes / shortcuts

The same modes as the old `.bat` still work as arguments. With an argument the app runs straight away, shows the progress and closes by itself when done:

```bat
RNX_Cache_Cleaner.exe              :: graphical interface
RNX_Cache_Cleaner.exe /rapida      :: quick cleanup
RNX_Cache_Cleaner.exe /completa    :: full cleanup + TRIM + NVIDIA profile
RNX_Cache_Cleaner.exe /todo        :: full + shader cache + mouse + NVIDIA + power plan + network + OEM, no questions
RNX_Cache_Cleaner.exe /nobackup    :: same as /todo but without the Drive backup
RNX_Cache_Cleaner.exe /shader      :: shader cache only
RNX_Cache_Cleaner.exe /raton       :: mouse config only
RNX_Cache_Cleaner.exe /nvidia      :: NVIDIA CS2 profile only
RNX_Cache_Cleaner.exe /energia     :: RNX Performance power plan only
RNX_Cache_Cleaner.exe /red         :: network only (DNS 1.1.1.1 + 8.8.8.8, TCP auto-tuning)
RNX_Cache_Cleaner.exe /oem         :: RNX OEM information only
RNX_Cache_Cleaner.exe /backup      :: Steam backup to Google Drive only
```

The app can create a **/nobackup** shortcut on your desktop (CS2 modules tab). For other modes, add the argument to a shortcut's target yourself.

---

## ☁️ Steam config backup → Google Drive

With the backup switch **ON**, every cleanup (except `/nobackup`) first saves the whole Steam `userdata` folder — the configs of **all your games**, CS2 included — into one ZIP per Steam account, and uploads it to a **"RNX Cache Cleaner Backups"** folder in your Google Drive.

- 🔐 The first time, your browser opens to sign in with Google. The app only gets the **`drive.file`** permission: it can only see the files it created itself, never the rest of your Drive. The sign-in is remembered and renews itself.
- 🗂️ **One subfolder per day** (`RNX Cache Cleaner Backups\2026-11-17\<SteamID>.zip`), each one a full snapshot. Only the **current and the previous day** are kept: when a third day is created, the oldest folder goes to the Drive trash (recoverable for 30 days). Backing up again the same day updates that day's folder.
- ⚡ Nothing is re-uploaded if your configs did not change, and unchanged accounts are copied inside Drive instead of uploaded again.
- 🚫 Screenshots (`760`), clips (`gamerecordings`), rebuildable caches and files bigger than the size limit (500 MB by default) are skipped. All of that can be changed in the **Backup** tab.
- 💾 A local copy of the ZIPs is kept in `%LOCALAPPDATA%\RNX Cache Cleaner\backups`.

---

## 🔄 Automatic updates

On startup the app checks the latest GitHub release. When there is a newer version you get a notice with **Update**: it downloads the new MSI (checking size and SHA-256), installs it silently and reopens itself. The portable `.exe` replaces itself instead.

Your settings, Google sign-in, history and backups live in `%LOCALAPPDATA%\RNX Cache Cleaner`, outside the program folder, so updates and reinstalls never touch them.

---

## 🧽 What gets cleaned

| # | Area | What it clears | Quick | Full |
|---|------|----------------|:-----:|:----:|
| 1 | **Windows Temp** | `C:\Windows\Temp` | ✅ | ✅ |
| 2 | **User Temp** | `%TEMP%` | ✅ | ✅ |
| 3 | **Prefetch** | `C:\Windows\Prefetch` | ✅ | ✅ |
| 4 | **Recent files** | Loose shortcuts in `Recent` (Explorer pinned items are kept) | ✅ | ✅ |
| 5 | **Print queue** | `spool\PRINTERS` (restarts the spooler) | | ✅ |
| 6 | **DNS cache** | `ipconfig /flushdns` | ✅ | ✅ |
| 7 | **Windows Update** | `SoftwareDistribution\Download` (restarts the services) | | ✅ |
| 8 | **Thumbnails** | `thumbcache_*.db` (restarts Explorer) | | ✅ |
| 9 | **Icon cache** | `IconCache.db`, `iconcache_*.db` | | ✅ |
| 10 | **Microsoft Store** | `wsreset` (the Store window it opens is closed automatically) | | ✅ |
| 11 | **Delivery Optimization** | Windows Update P2P cache | | ✅ |
| 12 | **Memory dumps** | `Minidump`, `MEMORY.DMP`, CBS logs, WER reports | | ✅ |
| 13 | **Font cache** | `FontCache*.dat`, `FNTCACHE.DAT` | | ✅ |
| 14 | **Recycle Bin** | All drives | | ✅ |
| 15 | **Event logs** | Every event log + Windows/Panther/DISM logs | ✅ | ✅ |
| 16 | **GPU driver cache** | NVIDIA / AMD / Intel shader caches | ✅ | ✅ |
| 17 | **Discord cache** | Closes Discord and clears its cache | | |
| 18 | **SSD optimization** | `defrag X: /L` on every fixed drive (TRIM) | | ✅ |

### 🎮 CS2 modules

- **Shader cache** — found automatically in **every Steam library** (`libraryfolders.vdf`), on any drive. You can also set the folder by hand.
- **Mouse config** — imports the bundled `raton.reg`, which is **Windows' default mouse configuration** (stock speed, Enhance pointer precision and default curves), or your own `.reg`.
- **NVIDIA CS2 profile** — silently imports the bundled **RNX Performance** profile, optimized for CS2, with NVIDIA Profile Inspector (only with an NVIDIA GPU). The profile is fixed and can't be swapped.
- **RNX Performance power plan** — imports and activates a power plan tuned for maximum performance and minimum latency in games. It always uses the same ID, so it never duplicates: if it already exists it's just activated. If Windows rejects the full `.pow` import (it's all-or-nothing and fails when a single setting isn't supported on that PC), the plan is built setting by setting and unsupported settings are skipped and logged.

- **Network** — sets DNS **1.1.1.1** (Cloudflare) and **8.8.8.8** (Google) on the connected physical network adapters and TCP auto-tuning to `normal`. IPv6 is not touched.
- **RNX OEM information** — Manufacturer *RNX*, Model **RNX + 5 digits** (generated from the PC's own ID, always the same on that PC, e.g. `RNX 84805`), support hours, Discord, GitHub link and the RNX logo (`C:\ProgramData\RNX\logo.bmp`). Shown in Settings → System → About.

**Everything** (`/todo` and `/nobackup`) runs all of them: shader cache, mouse config, NVIDIA profile, power plan, network and OEM information. **Custom** has a card for each one too.

### 🔧 Repairs (Windows → Mods and fixes, by hand only)

In **Windows → Mods and fixes → Repairs**. These are never run by Everything:

- **Old and faulty devices** — like Driver Booster: removes devices Windows remembers but that are no longer connected (with `pnputil /remove-device`) and restarts connected devices that report an error. **Shows the full list before doing anything.** Drivers are kept, so anything you plug back in reinstalls by itself (it only loses settings saved for that device). System, CPU, firmware and TPM entries are never touched. A restore point is created first if enabled.
- **Fix Microsoft Store (AtlasOS)** — runs AtlasOS's `fixStoreIssues.cmd` (bundled unchanged) for when the Store or apps won't install or update: it rebuilds the app deployment database and removes Gaming Services. Only enabled on AtlasOS.

### 🖥️ CS2 video settings

The **CS2 video** tab edits `Steam\userdata\<account>\730\local\cfg\cs2_video.txt` **before you launch the game**, in plain words instead of CS2's internal names:

- **RNX's full CS2 config**: one button copies Ren0X1's binds, sensitivity, crosshair, game options and video settings into the chosen account (your in-game name is kept; nothing tied to his Steam account or inventory is copied). Your own config is saved first and **Restore mine** puts it back. CS2 is closed first if it's open.
- **Quick choice**: *Maximum FPS*, *Balanced* and *Best looking*.
- **Screen**: resolution and Hz (read from your monitor), 4:3 / 16:9 / 16:10, full screen / borderless / window, V-Sync.
- **Image quality**: shadows, moving shadows, textures, texture sharpness, lighting detail, effects, corner shading, HDR, anti-aliasing, FSR and NVIDIA Reflex — each with a one-line explanation.
- **Lock switch**: marks the file **read-only** so CS2 never overwrites it. While locked the form is disabled; unlock it to edit. **Every change is saved straight away**, keeping the rest of the file exactly as it was. The first change makes a backup (`cs2_video.txt.rnxbak`).
- Several Steam accounts on the PC? Pick which one at the top.

### 🧱 Firewall cleanup and PC report

In **Windows → Mods and fixes**:

- **Firewall rule cleanup** — programs and games leave Windows Firewall rules behind when they're uninstalled or moved. **Scan** lists orphaned rules (the program no longer exists) and exact duplicates; you choose which to remove. A full copy of the firewall is exported first and **Restore copy** brings it back exactly. Windows and RNX rules are never touched.
- **PC report** — builds an anonymous summary (hardware, Windows, drivers, disks, network, RNX settings and recent errors), saves it to the desktop and copies it to the clipboard to paste into Discord. User name, PC name, IP and MAC addresses are removed.

### 🕒 Custom taskbar clock

**Windows → Mods and fixes → Custom taskbar clock** replaces the Windows clock with RNX's: two lines with any time/date format and variables for CPU, GPU, temperatures, VRAM, RAM and network speed, templates, font, size, color and bold, with a live preview. Nothing is injected into Windows: the native clock is hidden with its official *Show time and date in the system tray* setting and RNX draws its clock just left of the tray icons. Clicking it opens the Windows calendar; it hides in fullscreen games; and the Windows clock always comes back when the option is turned off or RNX is closed. Inspired by *Taskbar Clock Customization* for Windhawk by m417z (GPL-3.0), implemented independently.

### 🎮 Open NAT for older Call of Duty games

**Windows → Mods and fixes → Online compatibility → Open port** opens port **3074 TCP + UDP** in the Windows Firewall and on the router through UPnP, so older Call of Duty titles such as **Black Ops II (T6)** don't get Strict or Moderate NAT (long searches, empty lobbies, no hosting). It never duplicates firewall rules, re-points the router mapping if your PC's IP changed and shows whether the port is open in the firewall and on the router. If the router has UPnP disabled, only the firewall is configured and the card tells you. **Close port** removes only RNX's rules and mappings.

### 🔧 PC drivers

**Drivers → PC drivers** works like Driver Booster: it reads every device and its driver and shows **Outdated drivers**, **Game components** and **Up to date**. Drivers come only from official, signed sources — **Windows Update** and the **Microsoft Update Catalog** (searched by hardware ID, installed with Windows' own `pnputil`, which only applies a driver that matches the device) — and game components (Java, Visual C++, .NET, DirectX, GameInput…) are updated with **winget**. A restore point is created first if enabled. **NVIDIA drivers are excluded**: they're handled in **Drivers → NVIDIA driver**.

### 🔑 Activation

The first time RNX opens on a PC it asks for a **6-digit activation code** (contact: Discord **ren0x1**). Each code is valid for one minute only. Once activated, the PC stays activated; nothing runs (window, command-line modes, scheduled cleanup or tray) until it is.

### 📊 Performance HUD

The **HUD** tab shows an overlay with **FPS, average, 1 % and 0.1 % lows, frametime graph, CPU, GPU and RAM** (load, clocks, temperatures, VRAM). It's a separate window — **nothing is injected into the game**, so it's anti-cheat safe. Three designs (Classic, RNX panel, Mixed), six positions or a free position (unlock and drag), size, background opacity, rows to show and a **show/hide key** that works in game. It's visible in borderless and in Windows 11 fullscreen. CPU temperature and clock need the free, signed **PawnIO** driver, which RNX can download (signature checked) and open for you.

### 🟩 NVIDIA driver

The **NVIDIA driver** tab (NVIDIA cards only) updates the graphics driver **without the NVIDIA App**, like NVCleanstall:

- Shows your installed version and the latest **Game Ready** or **Studio** drivers from NVIDIA's official service, in a drop-down with date and size. A **New** badge in the menu and a notice in the tab tell you when you're behind (checked at start and every 12 h, only inside the app).
- **Download and prepare** gets the official installer from nvidia.com and unpacks it; then choose the components (**Recommended**: driver, PhysX, HD Audio, Control Panel, runtimes; **Minimal**: driver only). **Telemetry, the NVIDIA App and installer ads are always removed.**
- **Install** runs it straight away with only NVIDIA's progress bar (optional **clean installation**); the HUD and Digital Vibrance pause while the driver is replaced. **Save clean package** keeps it for later or for another PC. You can also use an installer you already have.
- Downloads are deleted after installing and when RNX opens or closes.

### 🪟 Windows optimization

The **Windows** drop-down has four tabs:

- **Optimization** — 40 performance, GPU (NVIDIA / AMD / Intel), privacy and telemetry, AI (Recall, Copilot), user-experience and service settings, each with its risk level: **Safe**, **Moderate** or **Risky**. The state is read from the system (applied / not applied), the ones you apply are saved in your RNX settings, and **every setting applied from RNX can be undone exactly** (the previous value of every change is saved). **Apply all safe settings** does them in one go (with a restore point if enabled).
- **Apps** — Microsoft Store apps classified as safe to remove, use caution or other; select and uninstall.
- **Startup** — enable or disable programs that start with Windows, reversibly, like Task Manager.
- **Tasks** — enable or disable third-party scheduled tasks. Windows and RNX tasks are never listed.

### 🖥️ Custom Resolution

The **Custom Resolution** tab adds scaled resolutions to your graphics card (e.g. **1352×1080** stretched) for NVIDIA and AMD, removes them or goes back to the driver's list. A restart is recommended afterwards (button included). Same approach as ToastyX's [Scaled Resolution Editor](https://monitortests.com/scaled-resolution-editor) (written from scratch, compatible with what SRE saves).

### 🎨 Digital Vibrance

Like **vibranceGUI**, built in: when a game from your list is in front, its colors get more saturated, and optionally its resolution changes. When you leave it, Windows goes back to its normal color and resolution.

- **NVIDIA** (NVAPI, 50–100 %) and **AMD** (ADL saturation, 0–300 %, like vibranceGUI).
- **Windows color** level, **main monitor only** option, and a list of games with icon, color % and optional resolution. Add a game from the programs that are open, or pick its `.exe`.
- **Import from vibranceGUI** brings your games and levels in one click. If vibranceGUI is also open, the app warns you, since both would fight, and can close it.
- It's **always active while RNX is open**. Like vibranceGUI, **closing the window (X) keeps RNX in the tray** next to the clock (right-click → Exit); minimizing works as usual. Exiting RNX restores the normal color.
- **Settings → Start with Windows in the background** starts RNX hidden in the tray when you sign in, with admin rights and no UAC prompt.

### ☁️ Your RNX settings travel with you

Every Drive backup also saves your RNX settings (`rnx_settings.json` in the backup folder): language, colors, Custom selection, backup options, CS2 font size and **Digital Vibrance with your games**. On a new PC, connect Google and press **Backup → Restore my RNX settings**. Things tied to a PC (paths, power plan, scheduled tasks) are not included.

### 🔤 CS2 font manager

The **CS2 fonts** tab changes the font of the whole CS2 interface, **the way that actually works in CS2**: the font goes in `game\csgo\panorama\fonts` together with a `fonts.conf` that redirects every Stratum2 variant (plus Arial, Times, Courier and notosans) to it. The original game has no `fonts.conf` there, so nothing from Valve is modified.

- **RNX font** (Bebas Neue) with one click, or **any font installed on your PC** (search and live preview, 80 per page), or a `.ttf`/`.otf` file.
- **Size** 85–110 % (95 % by default).
- **Back to the original** removes the `fonts.conf` and the added font. Valve's fonts are never touched. CS2 is closed first if it's open.

### ⚙️ Settings: your own files

In **Settings** each user can use their own mouse **`.reg`** instead of Windows' default one:

- **Export my mouse** saves your current Windows mouse settings (`HKCU\Control Panel\Mouse`) to a `.reg` and uses it from then on — one click to make your own setup the one the app applies.
- **Choose file…** loads any `.reg` you already have.
- **Use default** goes back to Windows' default mouse configuration.

The CS2 mouse module, Full and Everything all use the chosen file. If it disappears, the bundled one is used and the log says so.

### 🧯 Safety: restore point and undo

- **Restore point** — before **Full** and **Everything** the app creates a Windows restore point (Settings → Safety, on by default). Windows only allows one every 24 hours; if there's a recent one, that one is kept. If System Protection is off on `C:`, the log says so.
- **Undo** — the first time RNX applies the mouse settings, the NVIDIA profile, the power plan, the network settings or the OEM information, what you had before is saved (for NVIDIA, the driver's profile database). **CS2 modules → Undo** puts it back.
- **CS2 open** — if CS2 is running when you clean the shader cache or apply the NVIDIA profile, the app asks to close it first.
- **Current state** — the CS2 modules tab shows the active power plan, whether your mouse settings match the `.reg` the app uses, the shader cache size and when the NVIDIA profile was last applied.

### ⏰ Scheduled cleanup

**Settings → Scheduled cleanup** creates a Windows Task Scheduler task (`RNX Cache Cleaner - Scheduled cleanup`) that runs Quick, Full or Everything **at sign-in** (3 minutes later), **daily** or **weekly** (Sundays) at 12:00. If the PC was off, it runs as soon as it's on. It starts minimized, asks nothing, skips the run if CS2 is open and shows a Windows notification when it's done. **Off** deletes the task.

Profile Inspector, the RNX Performance `.nip` and `.pow`, and `raton.reg` ship **inside the `.exe`** — nothing to download separately.

---

## 📦 Requirements

- 🪟 **Windows 10 or 11**, 64-bit
- 🔑 Administrator rights (asked when the app opens)
- 🎮 Steam (only for the shader cache and backup)
- 🟩 NVIDIA GPU (only for the NVIDIA module)
- ☁️ A Google account (only for the Drive backup)

## 🗂️ Where things are stored

| Path | Content |
|------|---------|
| `%LOCALAPPDATA%\Programs\RNX Cache Cleaner` | The program (MSI install) |
| `%LOCALAPPDATA%\RNX Cache Cleaner\settings.json` | Your settings |
| `%LOCALAPPDATA%\RNX Cache Cleaner\RNX_Cleaner.log` | Log of the last run |
| `%LOCALAPPDATA%\RNX Cache Cleaner\history.json` | Cleanup history |
| `%LOCALAPPDATA%\RNX Cache Cleaner\google\` | Google Drive sign-in token |
| `%LOCALAPPDATA%\RNX Cache Cleaner\backups\` | Local copy of the backup ZIPs |
| `%LOCALAPPDATA%\RNX Cache Cleaner\tools\` | Profile Inspector, `.nip`, `.reg` and the Store fix, extracted when used |
| `%LOCALAPPDATA%\RNX Cache Cleaner\undo\` | What Undo puts back (mouse, power plan, NVIDIA, DNS, OEM) |
| `C:\ProgramData\RNX\logo.bmp` | OEM logo |

---

## 🛠️ Building from source

```bash
dotnet publish src/RnxCleaner/RnxCleaner.csproj -c Release -o dist
dotnet build installer/RnxCleaner.Installer.wixproj -c Release -p:Version=5.0.0
```

Needs the **.NET 10 SDK**; the MSI uses WiX Toolset 5 (restored automatically from NuGet). For Google Drive, put your OAuth client (*Desktop app* type) at `src/RnxCleaner/Assets/google_client.json` — it is git-ignored. Releases are built by GitHub Actions, which reads it from the `GOOGLE_CLIENT_JSON` secret.

---

## ⚠️ Safety notes

- Deleted files **cannot** be recovered.
- Clearing event logs makes later troubleshooting harder.
- The Discord area closes Discord; the thumbnails area restarts Explorer.
- The mouse and NVIDIA modules write to the registry / driver profiles. The NVIDIA profile only affects CS2; the mouse one is global.
- Close CS2 and Steam before clearing the shader cache.

---

## 🙏 Credits

- [NVIDIA Profile Inspector](https://github.com/Orbmu2k/nvidiaProfileInspector) by Orbmu2k (bundled).
- Custom resolutions use the same registry approach as [Scaled Resolution Editor](https://monitortests.com/scaled-resolution-editor) by ToastyX.
- **Bebas Neue** font by Dharma Type, under the SIL Open Font License 1.1 (bundled).
- Digital Vibrance works like [vibranceGUI](https://github.com/juv/vibranceGUI) by juv (rewritten from scratch).
- Microsoft Store fix: `fixStoreIssues.cmd` from [AtlasOS](https://github.com/Atlas-OS/Atlas) (bundled unchanged).
- Windows optimization: selection and risk classification based on [optimizerDuck](https://github.com/itsfatduck/optimizerDuck) by itsfatduck (GPL-3.0), implemented independently in RNX.
- NVIDIA driver: idea from NVCleanstall by TechPowerUp; driver lookup like [TinyNvidiaUpdateChecker](https://github.com/HawaiiBeach/TinyNvidiaUpdateChecker); GPU ids from [nvidia-data](https://github.com/ZenitH-AT/nvidia-data); unpacked with [7-Zip](https://www.7-zip.org) `7zr.exe` by Igor Pavlov (public domain, bundled).
- Taskbar clock: inspired by [Taskbar Clock Customization](https://github.com/ramensoftware/windhawk-mods) for Windhawk by m417z (GPL-3.0), implemented independently without code injection.
- PC drivers: Microsoft Update Catalog approach from [OpenDriverUpdater](https://github.com/OpenDriverUpdater/OpenDriverUpdater) (MIT), implemented independently in RNX.
- HUD sensors: [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) and the [PawnIO](https://github.com/namazso/PawnIO.Setup) driver; FPS via ETW with Microsoft TraceEvent.

---

## 📜 Changelog

### v5.4.1 (current)
- 🧱 **Firewall rule cleanup** (Windows → Mods and fixes): finds rules left behind by programs and games that no longer exist and exact duplicates, lets you choose which to remove, and saves a **full copy of the firewall** first (`netsh advfirewall export`) so **Restore copy** puts it back exactly. Windows and RNX rules are never touched
- 📋 **PC report** (Windows → Mods and fixes → Support): one click builds an anonymous summary of hardware, Windows, drivers, disks, network, RNX settings and the latest errors in the Log, saves it to the desktop and copies it to the clipboard, ready to paste into Discord when asking for help. No user name, PC name, IP or MAC addresses

### v5.4.0
- 🕒 **Custom taskbar clock**, like Windhawk's *Taskbar Clock Customization* but without injecting anything: RNX hides the Windows clock with its official setting and shows its own next to the tray, with any time and date format, two lines, variables (`%time%`, `%date%`, `%weekday%`, `%weeknum%`, `%cpu%`, `%cpu_temp%`, `%gpu%`, `%gpu_temp%`, `%vram%`, `%ram%`, `%download%`, `%upload%`…), templates (time and date, seconds, performance, temperatures, network), font, size, color and bold, with live preview. Clicking it opens the Windows calendar, it hides in fullscreen games, and the Windows clock always comes back when you turn it off or close RNX
- 🧰 New **Windows → Mods and fixes** tab with the taskbar clock and, moved from Custom, the **Microsoft Store fix**, **disconnected and faulty devices** and **Open NAT for older Call of Duty games**

### v5.3.5
- 🎮 **Open NAT for older Call of Duty games** (Custom → Online compatibility): opens port **3074 TCP + UDP** in the Windows Firewall **and** on the router (UPnP) in one click, for Black Ops II (T6) and other CoD titles that need it to avoid Strict/Moderate NAT. Never duplicates firewall rules, refreshes the router mapping if the PC's IP changed, shows the current state and **Close port** removes only RNX's rules and mappings. Replaces the separate T6-Open-Nat-Script

### v5.3.4
- 🪟 **Single instance fixed**: opening the shortcut while RNX is already running (including when it started with Windows in the tray) now brings the open window to the front instead of starting a second copy
- 🔧 **PC drivers**: the scan only runs when you press **Scan**; the progress bar advances as each driver or component finishes and shows which one is being installed (“2 of 9”); and **Dismiss** hides a device or component from future scans (saved in your settings), with a **Dismissed** list to show them again

### v5.3.3
- 🧩 New **Drivers** drop-down with **NVIDIA driver** (NVIDIA cards only) and the new **PC drivers** tab
- 🔧 **PC drivers**, like Driver Booster: lists every device with its driver, finds newer drivers from official, signed sources (**Windows Update** and the **Microsoft Update Catalog** by hardware ID, like OpenDriverUpdater) and **game components** (Java, Visual C++, .NET, DirectX, GameInput) through winget, and updates the selected ones with a restore point first. **NVIDIA drivers are never touched here**

### v5.3.2
- ☁️ **Drive**: only one `rnx_settings.json` is kept in the backup folder; it's updated on every backup and any duplicates are moved to the Drive trash
- 🐛 **Restore my RNX settings** now finds the settings saved in Drive

### v5.3.1
- 🎨 **Custom color**: besides the built-in palettes, choose any color with a color picker (saturation/brightness square, hue bar and hex code) with live preview, from the color menu, Settings or the first launch. The shade is adjusted automatically so text stays readable in light and dark mode, and it's saved with your settings
- 🐛 Settings: the mouse `.reg` card no longer squeezes its description next to the buttons (buttons now go below, in every language)

### v5.3.0
- 🔑 **Activation**: the first time RNX opens on a PC it asks for a 6-digit code (valid for one minute) from the administrator; contact on Discord **ren0x1**
- 📊 **Performance HUD** in its own tab: FPS, 1 % / 0.1 % lows, frametime graph, CPU, GPU and RAM, three designs, six positions or free position with a lock, size, opacity and a show/hide key. Separate window, nothing injected into games
- 🟩 **NVIDIA driver** tab: latest Game Ready / Studio driver from NVIDIA without the NVIDIA App, components to choose, telemetry and installer ads always removed, direct install with optional clean installation, clean package to save, and a **New** badge when you're behind
- 🪟 **Windows** drop-down: **Optimization** (40 settings with Safe / Moderate / Risky level, current state, saved in your settings and exact undo), **Apps** (remove preinstalled apps), **Startup** and **Tasks**
- 🧹 NVIDIA downloads are cleaned up automatically; the HUD and Digital Vibrance pause while a driver is installed
- ✍️ All texts reviewed in the six languages for a professional tone

### v5.2.2
- 💜 New **Lavender** color palette (#B174F7), in Settings → Color and on first launch

### v5.2.1
- 📂 **New menu**: the CS2 tabs (**Modules, Video, Fonts**) are grouped in a **CS2** drop-down, plus new **Custom Resolution** and **Digital Vibrance** tabs
- 🖥️ **CS2 video**: change CS2's graphics before launching it, in plain words, with quick presets and a **lock switch** that makes `cs2_video.txt` read-only so CS2 never touches it
- 🎯 **RNX's full CS2 config** with one click (binds, sensitivity, crosshair, options and video), keeping your in-game name; **Restore my config** puts yours back
- 🔤 **CS2 fonts**: font manager that installs fonts where CS2 really reads them (`game\csgo\panorama\fonts`), with the RNX font (Bebas Neue), all your installed fonts with preview (80 per page) and a size option; **Back to the original** never touches Valve's files
- 🖥️ **Custom Resolution**: custom scaled resolutions (like 1352×1080) for NVIDIA and AMD, compatible with Scaled Resolution Editor, with **Restore defaults** and a restart prompt
- 🎨 **Digital Vibrance**: color saturation per game like vibranceGUI (NVIDIA and AMD), optional resolution per game, Windows level and main-monitor option, games added from open programs (shown as `cs2.exe`, `chrome.exe`... with their icon) or by `.exe`, and **import from vibranceGUI**. Always active while RNX is open
- 🗂️ **Tray**: closing the window (X) always keeps RNX in the tray next to the clock (right-click → Exit); minimizing works as usual. **Settings → Start with Windows in the background** starts it hidden at sign-in
- 🌐 **Network** in Everything and Custom: DNS 1.1.1.1 + 8.8.8.8 and TCP auto-tuning `normal`
- 🏷️ **RNX OEM information** in Everything and Custom: Manufacturer RNX, Model **RNX + 5 digits** unique to each PC, support hours, Discord and logo
- ⚡ Power plan, network and OEM cards added to Custom
- 🔧 **Repairs** in Custom (by hand only): remove old and faulty devices with a preview list, and AtlasOS's Microsoft Store fix
- ↩️ Undo also restores DNS, TCP auto-tuning and the OEM information
- ☁️ **Your RNX settings are saved with each Drive backup** and can be restored on another PC with **Restore my RNX settings**
- 🔑 Google sign-in for the Drive backup works for everyone (the Google app is published) and no longer expires every 7 days
- ✍️ Clearer, more professional wording across the app

### v5.1.2
- 💽 **TRIM on every drive**: SSD optimization now runs on every fixed drive, not just `C:` (drives without TRIM, like HDDs, are skipped and logged)
- ⚡ **Servers → Quick setup**: keep only the 3, 5 or 10 best locations by ping, or play only in one region (Europe, North America...), with one click
- ↩️ **Undo now includes the NVIDIA profile**: your previous driver profiles are saved before importing RNX Performance and Undo puts them back
- 🎮 **CS2 open warning**: if the game is running when you clean the shader cache or apply the NVIDIA profile, the app offers to close it first
- 📝 The log header shows the full version (e.g. v5.1.2)

### v5.1.1
- 🛡️ **New Servers tab**: block Valve server locations (Madrid, Paris...) with Windows Firewall rules, with live ping and status read from the firewall
- ⚡ **RNX Performance power plan** applied by Everything (`/todo`, `/nobackup`) and from the CS2 modules tab, without ever duplicating it
- 🎮 New default NVIDIA profile: **RNX Performance** (replaces the previous one)
- ⚙️ **Settings tab**: export your current Windows mouse settings or load your own `.reg`
- 🖱️ Fixed the mouse module description: the bundled `.reg` is Windows' default mouse configuration
- 🧯 **Restore point** before Full and Everything (on by default, can be turned off in Settings)
- ↩️ **Undo** in CS2 modules: puts back the mouse settings and power plan you had before RNX applied them
- 📊 **Current state** in CS2 modules: active power plan, whether the mouse settings are applied, shader cache size and when the NVIDIA profile was applied
- ⏰ **Scheduled cleanup** with Windows Task Scheduler (at sign-in, daily or weekly), minimized and with a notification when it's done
- 📝 The update notice now shows **what's new** in the new version
- 🐛 Fixed: the bundled mouse `.reg` was damaged by git's line endings and Windows imported nothing; the mouse settings are now applied right away, without signing out
- 🗑️ Removed the "RNX - TODO" Start menu shortcut and its button

### v5.1
- 🎨 **New interface**: framed-sheet layout, light and dark themes and six color palettes (Amber by default), following Windows' theme automatically
- 🌍 **Six languages**: English (default), Spanish, Russian, German, French and Chinese, for the whole app and the log
- 🚀 **First-launch setup**: pick language, color and appearance before you start
- 🏠 New home screen: mode selector, disk usage, backup status, what the chosen mode does and the space freed lately
- 🐛 Fixed: cleaning the user Temp folder deleted the app's own runtime files in `%TEMP%\.net`, so it crashed on exit
- 🎯 New amber icon

### v5.0
- 🆕 **Rewritten as a Windows app** (C# / .NET 10 / WPF): graphical interface, single self-contained `.exe`
- 🆕 **MSI installer** (per-user) with Start menu and desktop shortcuts
- 🆕 **Automatic updates** from GitHub Releases, keeping your settings
- 🆕 **Steam backup to Google Drive** (replaces the old GitHub/LFS sync), one subfolder per day, keeping the current and previous day
- 🆕 **History** tab with a chart of the space freed by every run
- 🆕 Shader cache found automatically in every Steam library
- 🆕 Restart recommendation at the end of a cleanup
- 🆕 The Microsoft Store window opened by `wsreset` closes by itself
- 🆕 Profile Inspector, the NVIDIA profile and `raton.reg` bundled inside the `.exe`
- 🗑️ Removed: the `.bat`, the PowerShell modules and the `backups\` folder in the repository

### v4.5
- 🆕 **Automatic GitHub sync after every backup**: `commit` + `fetch` + `push` of `backups\`, no manual git
- 🆕 New `RNX_Git_Sync.ps1` module: commits only the watched paths, disables credential prompts, times every git command out and refuses to rebase over a dirty working tree
- 🆕 New settings in the `.bat`: `GIT_SYNC`, `GIT_REMOTO`, `GIT_RAMA`, `GIT_TIMEOUT`
- 🆕 New `/nobackup` argument: everything `/todo` does, minus the backup and the GitHub push
- 🛡️ A failed push (no network, no credentials, LFS quota) no longer loses anything: the commit stays local and goes up on the next run

### v4.4
- 🆕 **Automatic Steam configuration backup**: with any argument (`/todo`, `/completa`, …) the whole `userdata` folder is saved to `backups\<STEAMID>.zip` before cleaning, with Steam's structure preserved inside the ZIP
- 🆕 New `/backup` argument (backup only, no cleaning)
- 🆕 New `RNX_Backup_Userdata.ps1` module: finds Steam through the registry, skips files larger than `BACKUP_MAX_MB` and **only rewrites the ZIP when the contents changed** (hash in `_indice.txt`)
- 🆕 Configurable exclusion list in `BACKUP_EXCLUIR`: screenshots (`760`), clips (`gamerecordings`) and rebuildable caches (`inventorymsgcache`, `ugc`, `ugcmsgcache`) by default
- 🆕 `.gitattributes` with Git LFS for `backups/*.zip`
- 🐛 **Fixed: the `.bat` had Unix line endings (LF)**. `cmd` mis-parsed it and ate the first character of some lines (`call` → `all`), aborting halfway through. The file is CRLF now and `.gitattributes` enforces it
- 🐛 Fixed: the `[>]` inside `echo` lines weren't escaped, so `cmd` treated them as redirection — section titles came out blank and a junk file named `]` was created

### v4.3
- 🐛 Fixed: the "Recent files" cleanup no longer deletes Jump Lists (`AutomaticDestinations`/`CustomDestinations`), which is where Windows keeps the pinned items of the Explorer panel. Only loose recent shortcuts are removed now, so your pinned folders stay put

### v4.2
- The NVIDIA CS2 profile no longer backs up the previous config (the profile only affects CS2)
- The full cleanup (option [2] and `/completa`) now applies the NVIDIA CS2 profile too

### v4.1
- 🆕 `/todo` mode: full cleanup + shadercache + mouse + NVIDIA profile, completely unattended
- 🆕 The log is overwritten on every run (only the last one is kept, with date and time)
- Shadercache and mouse steps are now logged in automatic mode as well

### v4.0
- 🆕 Full cyberpunk/neon UI redesign (ANSI colours, banner, highlighted items)
- 🆕 Arrow-key menu navigation (↑↓ + Enter) plus number shortcuts
- 🆕 Fast loading animations (~0.5s) replacing static progress screens
- 🆕 Automatic GPU brand detection
- 🆕 NVIDIA CS2 profile module (silent `.nip` import via Profile Inspector)
- 🆕 `/nvidia` silent-mode argument

### v3.1
- GPU driver cache, Discord cache, SSD TRIM and silent-mode arguments

### v3.0
- Interactive menu, three cleanup modes, 8 new categories, freed-space measurement and logging

### v2.0
- Initial public release

---

> 👤 **RNX Cache Cleaner Pro** by [Ren0X1](https://github.com/Ren0X1) · free to use.
