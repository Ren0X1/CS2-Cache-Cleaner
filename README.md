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

Changes apply to your next matchmaking search.

---

## 🌍 Languages, colors and appearance

On first launch you choose your **language** (English by default, Spanish, Russian, German, French or Chinese), your **color** (Amber by default, Cobalt, Signal orange, Raspberry, Ocean or Graphite) and the **appearance**: Light, Dark or **System**, which follows Windows' light/dark mode live. All three can be changed later from the buttons at the bottom of the sidebar. Everything is translated, including the log.

---

## 🤖 Command-line modes / shortcuts

The same modes as the old `.bat` still work as arguments. With an argument the app runs straight away, shows the progress and closes by itself when done:

```bat
RNX_Cache_Cleaner.exe              :: graphical interface
RNX_Cache_Cleaner.exe /rapida      :: quick cleanup
RNX_Cache_Cleaner.exe /completa    :: full cleanup + TRIM + NVIDIA profile
RNX_Cache_Cleaner.exe /todo        :: full + shader cache + mouse + NVIDIA + power plan, no questions
RNX_Cache_Cleaner.exe /nobackup    :: same as /todo but without the Drive backup
RNX_Cache_Cleaner.exe /shader      :: shader cache only
RNX_Cache_Cleaner.exe /raton       :: mouse config only
RNX_Cache_Cleaner.exe /nvidia      :: NVIDIA CS2 profile only
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
| 18 | **SSD optimization** | `defrag C: /L` (TRIM) | | ✅ |

### 🎮 CS2 modules

- **Shader cache** — found automatically in **every Steam library** (`libraryfolders.vdf`), on any drive. You can also set the folder by hand.
- **Mouse config** — imports the bundled `raton.reg`, which is **Windows' default mouse configuration** (stock speed, Enhance pointer precision and default curves), or your own `.reg`.
- **NVIDIA CS2 profile** — silently imports the bundled **RNX Performance** profile, optimized for CS2, with NVIDIA Profile Inspector (only with an NVIDIA GPU). The profile is fixed and can't be swapped.
- **RNX Performance power plan** — imports and activates a power plan tuned for maximum performance and minimum latency in games. It always uses the same ID, so it never duplicates: if it already exists it's just activated. If Windows rejects the full `.pow` import (it's all-or-nothing and fails when a single setting isn't supported on that PC), the plan is built setting by setting and unsupported settings are skipped and logged.

**Everything** (`/todo` and `/nobackup`) runs all of them: shader cache, mouse config, NVIDIA profile and power plan.

### ⚙️ Settings: your own files

In **Settings** each user can use their own mouse **`.reg`** instead of Windows' default one:

- **Export my mouse** saves your current Windows mouse settings (`HKCU\Control Panel\Mouse`) to a `.reg` and uses it from then on — one click to make your own setup the one the app applies.
- **Choose file…** loads any `.reg` you already have.
- **Use default** goes back to Windows' default mouse configuration.

The CS2 mouse module, Full and Everything all use the chosen file. If it disappears, the bundled one is used and the log says so.

### 🧯 Safety: restore point and undo

- **Restore point** — before **Full** and **Everything** the app creates a Windows restore point (Settings → Safety, on by default). Windows only allows one every 24 hours; if there's a recent one, that one is kept. If System Protection is off on `C:`, the log says so.
- **Undo** — the first time RNX applies the mouse settings or the power plan, what you had before is saved. **CS2 modules → Undo** puts it back. The NVIDIA profile can't be undone this way; use the restore point.
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
| `%LOCALAPPDATA%\RNX Cache Cleaner\tools\` | Profile Inspector, `.nip` and `.reg`, extracted when used |

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

## 📜 Changelog

### v5.1.1 (current)
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
