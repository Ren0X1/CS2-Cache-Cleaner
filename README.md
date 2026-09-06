# 🧹 CS2 Cache Cleaner

![Batch](https://img.shields.io/badge/Batch-CMD-4D4D4D?style=flat-square&logo=windowsterminal&logoColor=white) ![PowerShell](https://img.shields.io/badge/PowerShell-5.0%2B-5391FE?style=flat-square&logo=powershell&logoColor=white) ![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat-square&logo=windows&logoColor=white) ![Counter-Strike 2](https://img.shields.io/badge/Counter--Strike%202-shader%20cache-F5A623?style=flat-square&logo=counterstrike&logoColor=white) ![Steam](https://img.shields.io/badge/Steam-userdata%20backup-171A21?style=flat-square&logo=steam&logoColor=white) ![NVIDIA](https://img.shields.io/badge/NVIDIA-Profile%20Inspector-76B900?style=flat-square&logo=nvidia&logoColor=white)

**RNX Cache Cleaner Pro** frees disk space and gets rid of the stutter that builds up on a gaming PC. It wipes Windows temp files, caches and logs, clears the **CS2/Steam shader cache** and the GPU driver cache, trims the SSD, and can apply a CS2-optimized **NVIDIA profile** — one run, no digging through Windows settings.

Everything is a single `.bat` you can run interactively or fire from a shortcut or a scheduled task. 🚀

---

## ⚡ Quick start

1. ⬇️ Download `RNX_Cache_Cleaner_v4.bat`.
2. 🖱️ Right-click → **Run as administrator** (it self-elevates anyway).
3. ⌨️ Move with the **arrow keys ↑↓** and confirm with **Enter**, or press the option number.
4. 📊 When it finishes you get how many MB you freed.

> 💡 Prefer it unattended? Create a shortcut, add ` /completa` to the Target, tick *Run as administrator*, and you have a one-click cleanup.

---

## 📋 Main menu

| Option | What it does |
|--------|--------------|
| **[1] QUICK cleanup** | Temp, GPU cache, DNS, Prefetch, recent files, event logs |
| **[2] FULL cleanup** | Everything automatic + SSD TRIM at the end |
| **[3] CUSTOM cleanup** | Y/N prompt for each category (18 of them) |
| **[4] SHADERCACHE** | Deletes the shadercache folder straight away (CS2/Steam) |
| **[5] MOUSE CONFIG** | Imports `raton.reg` |
| **[6] VIEW LOG** | Shows the last 30 lines of the log |
| **[7] NVIDIA CS2 PROFILE** | Imports an optimized `.nip` (only with an NVIDIA GPU) |
| **[0 / Exit]** | Closes the script |

The menu is a neon ANSI interface with arrow navigation and the current option highlighted, so you always know where you are. ANSI colours need **Windows 10/11**; the script turns support on by itself through the registry (`VirtualTerminalLevel`). On 7/8 it still works, just without colour.

---

## 🤖 Silent mode (arguments)

```bat
RNX_Cache_Cleaner_v4.bat /rapida     :: quick cleanup
RNX_Cache_Cleaner_v4.bat /completa   :: full cleanup
RNX_Cache_Cleaner_v4.bat /todo       :: EVERYTHING: full + shadercache + mouse + NVIDIA profile
RNX_Cache_Cleaner_v4.bat /shader     :: shadercache only
RNX_Cache_Cleaner_v4.bat /raton      :: mouse config only
RNX_Cache_Cleaner_v4.bat /nvidia     :: NVIDIA CS2 profile only
RNX_Cache_Cleaner_v4.bat /backup     :: Steam config backup only
```

Ideal for desktop shortcuts or scheduled tasks — the menu and the intros are skipped entirely.

> 💾 **Automatic backup**: with **any** argument (`/todo`, `/completa`, `/rapida`…) your Steam configuration is saved to `backups\` *before* anything is deleted. See [Steam configuration backup](#-steam-configuration-backup-userdata).

> ⚖️ **`/todo` vs `/completa`**: `/completa` runs the system cleanup and exits. `/todo` does all of that and **also** deletes the shadercache (saved or default path), applies `raton.reg` and imports the NVIDIA profile — without asking anything. Missing files are skipped and noted in the log.

> 📝 **The log is overwritten** on every run: `RNX_Cleaner.log` always holds only the last pass, with its date and time on the first line.

**Weekly scheduled cleanup**: Task Scheduler → Create task → action *run `RNX_Cache_Cleaner_v4.bat` with argument `/rapida`* → tick *Run with highest privileges* → weekly trigger. 🗓️

---

## 💾 Steam configuration backup (userdata)

Before cleaning anything, when the script runs **with arguments** it backs up Steam's `userdata` folder — the settings of **every** game, not just CS2. One ZIP per account, with Steam's original structure preserved inside:

```
backups\
├── 76561198xxxxxxxx.zip      <- one ZIP per SteamID
│   ├── 730/local/cfg/...     <- CS2 (binds, video, convars)
│   ├── 730/remote/cfg/...
│   ├── config/localconfig.vdf
│   └── <any other appid>/...
└── _indice.txt               <- per-account hash (change tracking)
```

- 🔍 **Steam is located automatically** through the registry (`HKCU\Software\Valve\Steam`), falling back to the usual install paths.
- 🚫 **Excluded**: `760` (screenshots), `gamerecordings` (video clips) and the caches Steam rebuilds by itself (`inventorymsgcache`, `ugc`, `ugcmsgcache`). The list lives in `BACKUP_EXCLUIR` inside the `.bat` — add `config` there too if you don't want your launch options and Steam settings either.
- 📦 **Files over 500 MB are skipped** (tune `BACKUP_MAX_MB` in the `.bat`). If a ZIP still ends up over 100 MB the script warns you: GitHub rejects files that big, so you'd need to lower the limit to leave the largest savegames out.
- ♻️ **The ZIP is only rewritten when something changed**: the account hash is compared against `_indice.txt`, so the repository doesn't grow on every run.
- 🔒 **Locked files** (Steam running) are skipped and noted in the log; everything else is still saved.
- ☁️ **The `backups\` folder is pushed to GitHub on its own**: every backup ends with a `commit` + `push` of that folder. See [Automatic GitHub sync](#️-automatic-github-sync).

**Git LFS** — the repository ships a `.gitattributes` with `backups/*.zip filter=lfs`, so the ZIPs travel through [Git Large File Storage](https://git-lfs.com/) instead of the regular history. That raises the per-file limit from 100 MB to 2 GB. To enable it on your clone:

```bat
git lfs install
git add .gitattributes backups
git commit -m "Backup userdata"
git push
```

> ⚠️ **Quota**: a free GitHub account gets **1 GB of LFS storage and 1 GB of bandwidth per month**, and LFS keeps **every version** of the ZIP forever. With a ~250 MB ZIP you burn through that in 4 pushes. If your savegames bloat the backup, lower `BACKUP_MAX_MB` or exclude more folders instead of buying data packs.

---

## ☁️ Automatic GitHub sync

Every time a backup runs — `/todo`, `/completa`, `/rapida`, `/backup`… — the script tries to push it to GitHub by itself. No manual `git` needed:

```
backup  ->  git add backups  ->  git commit  ->  git fetch  ->  git push
```

The work is done by `RNX_Git_Sync.ps1`, and it plays safe:

- 📦 **Only `backups\` is committed.** Anything else you have half-finished in the repo is left untouched — the commit is scoped to that path.
- 🔇 **It never blocks the cleanup.** Credential pop-ups are disabled (`GIT_TERMINAL_PROMPT=0`) and every git command has a time limit (`GIT_TIMEOUT`, 900 s by default).
- 🧹 **It never leaves conflict markers in your files.** If GitHub has new commits *and* you have unsaved changes it stops and tells you, instead of rebasing on top of a dirty tree.
- 🔁 **Nothing is lost if the push fails** (no network, no credentials, LFS quota…): the commit stays local and goes up on the next run, and the reason is written to `RNX_Cleaner.log`.
- 😴 **No changes, no commit**: if the ZIPs are identical to the last run there is nothing to push.

Settings live at the top of the `.bat`:

```bat
set "GIT_SYNC=1"        :: 0 = keep the backups local only
set "GIT_REMOTO=origin" :: remote to push to
set "GIT_RAMA="         :: empty = whatever branch you are on
set "GIT_TIMEOUT=900"   :: seconds per git command
```

> 🔑 **Credentials**: the first push has to be done by hand once (`git push`) so Git Credential Manager stores them. From then on it is unattended. If they are missing the script says so and keeps the commit local.

> ⚠️ **This burns LFS quota**: every changed ZIP is a new LFS version on each run. With a ~250 MB ZIP the free 1 GB/month of bandwidth is gone in about 4 pushes — see the quota note above, and set `GIT_SYNC=0` if you would rather push by hand.

> ⚠️ **Privacy**: `localconfig.vdf` holds your SteamID, library, launch options and friend nicknames. Review it before pushing `backups\` to a **public** repository — it stays in git history even if you delete it later.

---

## 🎯 NVIDIA CS2 profile module

Imports a CS2-optimized `.nip` profile straight into the NVIDIA driver using **NVIDIA Profile Inspector** in silent mode, without ever opening its UI.

1. 🔎 Detects whether your GPU is NVIDIA (if it isn't, the module doesn't even appear).
2. ⚙️ Imports the CS2 profile silently with `-silentImport`.
3. 🎮 The profile affects **only CS2**, not your global configuration.

**You have to provide these two files:**

| File | Where to place it | Where to get it |
|------|-------------------|-----------------|
| `nvidiaProfileInspector.exe` | Next to the script or in `tools\` | [github.com/Orbmu2k/nvidiaProfileInspector](https://github.com/Orbmu2k/nvidiaProfileInspector) |
| `CS2_Profile.nip` | Next to the script (or you'll be asked for the path) | Export it yourself from Profile Inspector, or use a community one |

> ⚠️ Always review the contents of a third-party `.nip` before applying it: Profile Inspector exposes undocumented, driver-version-specific settings.

---

## 🧽 What gets cleaned

| # | Category | What it clears |
|---|----------|----------------|
| 1 | **Windows Temp** | `C:\Windows\Temp` (via robocopy `/MIR`) |
| 2 | **User Temp** | `%TEMP%` |
| 3 | **Prefetch** | `C:\Windows\Prefetch` |
| 4 | **Recent files** | Recent shortcuts in `Recent` (Explorer pinned items are preserved) |
| 5 | **Print queue** | `spool\PRINTERS` (with spooler restart) |
| 6 | **DNS cache** | `ipconfig /flushdns` |
| 7 | **Windows Update** | `SoftwareDistribution\Download` (restarts services) |
| 8 | **Thumbnails** | `thumbcache_*.db` (restarts explorer) |
| 9 | **Icon cache** | `IconCache.db`, `iconcache_*.db` |
| 10 | **Microsoft Store** | `wsreset.exe` |
| 11 | **Delivery Optimization** | Windows Update shared cache |
| 12 | **Memory dumps** | `Minidump`, `MEMORY.DMP`, CBS logs, WER |
| 13 | **Font cache** | `FontCache*.dat`, `FNTCACHE.DAT` |
| 14 | **Recycle Bin** | `Clear-RecycleBin` |
| 15 | **Event logs** | `wevtutil cl` + `.evtx` + Panther + CBS + DISM |
| 16 | **GPU driver cache** | NVIDIA / AMD / Intel |
| 17 | **Discord cache** | Closes Discord and clears its cache |
| 18 | **SSD optimization** | `defrag C: /L` (TRIM) |

### 🎮 Optional modules (offered after the interactive cleanup)

- **Shadercache (CS2 / Steam)** — default path `C:\Program Files (x86)\Steam\steamapps\shadercache\730`. It checks whether Steam is running and warns you before deleting, and remembers the path in `shadercache.txt`.
- **Mouse configuration** — imports `raton.reg` into the Windows registry.

---

## 📦 Requirements

- 🪟 **Windows 10 or 11** (needed for the neon ANSI colours; on 7/8 it runs without colour)
- 💠 PowerShell 5.0+ (ships with Windows)
- 🔑 Administrator rights (requested automatically)
- 🎮 Steam (only for the shadercache and backup modules)
- 🟩 NVIDIA GPU + Profile Inspector + a `.nip` (only for the NVIDIA module)

## 🗂️ Files

| File | Purpose | Auto-created |
|------|---------|--------------|
| `shadercache.txt` | Saved shadercache path | ✅ |
| `raton.reg` | Mouse config to import | ❌ you provide it |
| `RNX_Cleaner.log` | Timestamped log of the last run | ✅ |
| `CS2_Profile.nip` | NVIDIA profile for CS2 | ❌ you provide it |
| `nvidiaProfileInspector.exe` | Import tool | ❌ download it |
| `RNX_Backup_Userdata.ps1` | Steam backup engine | 📦 ships with the repo |
| `RNX_Git_Sync.ps1` | Auto commit + push of `backups\` | 📦 ships with the repo |
| `backups\` | Per-SteamID config ZIPs | ✅ |
| `.gitattributes` | CRLF for the scripts + LFS for the ZIPs | 📦 ships with the repo |

---

## ⚠️ Safety notes

- Deleted files **cannot** be recovered.
- Clearing event logs makes later troubleshooting harder.
- The Discord module closes Discord automatically.
- The mouse and NVIDIA modules write to the registry / driver profiles. The NVIDIA profile only affects CS2; the mouse one is global, so back up your current `.reg` if that worries you.
- Close CS2 and Steam before using the shadercache module.

---

## 📜 Changelog

### v4.5 (current)
- 🆕 **Automatic GitHub sync after every backup**: `commit` + `fetch` + `push` of `backups\`, no manual git
- 🆕 New `RNX_Git_Sync.ps1` module: commits only the watched paths, disables credential prompts, times every git command out and refuses to rebase over a dirty working tree
- 🆕 New settings in the `.bat`: `GIT_SYNC`, `GIT_REMOTO`, `GIT_RAMA`, `GIT_TIMEOUT`
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
