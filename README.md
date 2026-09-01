# CS2-Cache-Cleaner / Limpiador de Caché CS2

[English](#english) | [Español](#español)

---

## Español

### Descripción

**RNX Cache Cleaner Pro v4.0** es una herramienta de limpieza y optimización del sistema Windows con una **interfaz cyberpunk/neón** y **navegación por flechas**. Limpia archivos temporales, cachés y logs, y añade módulos especializados para gamers: shader cache de CS2/Steam, caché de drivers gráficos y un **nuevo módulo de perfil NVIDIA** que importa un `.nip` optimizado para CS2.

> **Novedad v4.0:** rediseño visual completo (colores neón ANSI, banner, menú navegable con ↑↓ y Enter), animaciones de carga rápidas (~0.5s), detección automática de GPU, y módulo de importación de perfil NVIDIA Profile Inspector para CS2.

### Interfaz nueva

- **Colores neón reales** mediante secuencias ANSI (cian, magenta, amarillo sobre negro)
- **Menú navegable**: muévete con las **flechas ↑↓**, confirma con **Enter**, o pulsa el **número** directamente
- **Opción resaltada** con fondo neón para ver siempre dónde estás
- **Animaciones de carga** rápidas en cada tarea
- **Detección de GPU**: el módulo NVIDIA solo aparece si tienes una tarjeta NVIDIA

> Nota: los colores ANSI requieren Windows 10/11. El script activa el soporte automáticamente vía registro (`VirtualTerminalLevel`).

### Menú principal

| Opción | Descripción |
|--------|-------------|
| **[1] Limpieza RÁPIDA** | Temp, GPU cache, DNS, Prefetch, recientes, eventos |
| **[2] Limpieza COMPLETA** | Todo automático + TRIM SSD al final |
| **[3] Limpieza PERSONALIZADA** | Pregunta Y/N a cada categoría (18 opciones) |
| **[4] SHADERCACHE** | Borra directamente la carpeta shadercache (CS2/Steam) |
| **[5] CONFIG RATÓN** | Importa `raton.reg` |
| **[6] VER LOG** | Muestra las últimas 30 líneas del registro |
| **[7] PERFIL NVIDIA CS2** 🆕 | Importa un `.nip` optimizado (solo si hay GPU NVIDIA) |
| **[0 / Salir]** | Cierra el script |

### Modo silencioso (argumentos)

```bat
RNX_Cache_Cleaner.bat /rapida     :: Limpieza rápida directa
RNX_Cache_Cleaner.bat /completa   :: Limpieza completa directa
RNX_Cache_Cleaner.bat /todo       :: TODO: completa + shadercache + ratón + perfil NVIDIA
RNX_Cache_Cleaner.bat /shader     :: Solo shadercache
RNX_Cache_Cleaner.bat /raton      :: Solo configuración ratón
RNX_Cache_Cleaner.bat /nvidia     :: Solo importar perfil NVIDIA CS2
RNX_Cache_Cleaner.bat /backup     :: Solo backup de la config de Steam
```

Ideal para accesos directos en el escritorio o tareas programadas (el menú y las intros se saltan por completo).

> **Backup automático**: con **cualquier** argumento (`/todo`, `/completa`, `/rapida`, …) se hace primero una copia de seguridad de la configuración de Steam en `backups\`. Ver [Backup de configuración de Steam](#-backup-de-configuración-de-steam-userdata).

> **`/todo` vs `/completa`**: `/completa` solo hace la limpieza del sistema y termina. `/todo` hace todo eso y **además** borra el shadercache (usando la ruta guardada o la de defecto), aplica `raton.reg` e importa el perfil NVIDIA — todo sin preguntar nada. Si falta algún archivo (raton.reg, .nip, etc.), simplemente lo omite y lo anota en el log.

> **El log se sobrescribe** en cada ejecución: `RNX_Cleaner.log` siempre contiene únicamente la última pasada, con su fecha y hora en la primera línea.

### 🆕 Backup de configuración de Steam (userdata)

Antes de limpiar nada, si el script se lanza **con argumentos** se guarda una copia de la carpeta `userdata` de Steam — la configuración de **todos** los juegos, no solo CS2. Un ZIP por cuenta, con la estructura de Steam intacta dentro:

```
backups\
├── 76561198xxxxxxxx.zip      <- un ZIP por SteamID
│   ├── 730/local/cfg/...     <- CS2 (binds, video, convars)
│   ├── 730/remote/cfg/...
│   ├── config/localconfig.vdf
│   └── <cualquier otro appid>/...
└── _indice.txt               <- hash de cada cuenta (control de cambios)
```

Detalles:

- **Steam se localiza solo** por registro (`HKCU\Software\Valve\Steam`) con respaldo a las rutas típicas.
- **Se excluyen** `760` (capturas), `gamerecordings` (clips de vídeo) y las cachés que Steam regenera solo (`inventorymsgcache`, `ugc`, `ugcmsgcache`). La lista está en `BACKUP_EXCLUIR` dentro del `.bat` — añade ahí `config` si tampoco quieres tus launch options y ajustes de Steam.
- **Archivos > 500 MB se omiten** (ajustable en `BACKUP_MAX_MB` dentro del `.bat`). Si con ese límite algún ZIP acaba pesando más de 100 MB, el script **avisa**: GitHub rechaza archivos de ese tamaño, así que tendrías que bajar el límite para dejar fuera las partidas guardadas más gordas.
- **Solo se reescribe el ZIP si algo cambió**: se compara el hash de la cuenta contra `_indice.txt`, así el repositorio no engorda en cada ejecución.
- **Archivos en uso** (Steam abierto) se saltan y se anotan en el log; el resto se guarda igual.
- La carpeta `backups\` está pensada para **sincronizarse con GitHub** (`git add backups && git commit && git push`).

**Git LFS** — el repositorio ya trae `.gitattributes` con `backups/*.zip filter=lfs`, así que los ZIP viajan por [Git Large File Storage](https://git-lfs.com/) en vez de ir dentro del historial normal. Ventaja: el límite por archivo sube de 100 MB a 2 GB. Para activarlo en tu clon:

```bat
git lfs install
git add .gitattributes backups
git commit -m "Backup userdata"
git push
```

> ⚠️ **Cuota**: una cuenta gratuita de GitHub tiene **1 GB de almacenamiento LFS y 1 GB de ancho de banda al mes**, y LFS guarda **cada versión** del ZIP para siempre. Con un ZIP de ~250 MB agotas la cuota en 4 subidas. Si tus partidas guardadas engordan el backup, baja `BACKUP_MAX_MB` o excluye más carpetas en vez de comprar packs de datos.

> ⚠️ **Privacidad**: `localconfig.vdf` contiene tu SteamID, tu biblioteca, launch options y nicks de amigos. Si vas a subir `backups\` a un repositorio **público**, revísalo antes — en git queda en el historial aunque lo borres después.

### 🆕 Módulo Perfil NVIDIA para CS2

Importa un perfil `.nip` optimizado para CS2 directamente al driver NVIDIA, usando **NVIDIA Profile Inspector** en modo silencioso (sin abrir su interfaz).

**Cómo funciona:**
1. Detecta automáticamente si tu GPU es NVIDIA (si no lo es, el módulo no aparece)
2. Importa el perfil de CS2 en silencio con `-silentImport`
3. El perfil afecta solo a CS2, no a tu configuración global

**Requisitos del módulo (debes aportarlos tú):**

| Archivo | Dónde colocarlo | De dónde sacarlo |
|---------|-----------------|------------------|
| `nvidiaProfileInspector.exe` | Junto al script o en subcarpeta `tools\` | [github.com/Orbmu2k/nvidiaProfileInspector](https://github.com/Orbmu2k/nvidiaProfileInspector) |
| `CS2_Profile.nip` | Junto al script (o se te pedirá la ruta) | Lo creas/exportas tú desde Profile Inspector, o usas uno de la comunidad |

> ⚠️ **Importante sobre los `.nip`**: revisa siempre el contenido de un `.nip` de terceros antes de aplicarlo, ya que NVIDIA Profile Inspector expone ajustes no documentados y específicos de versión de driver.

### Áreas de limpieza disponibles

| # | Categoría | Qué limpia |
|---|-----------|------------|
| 1 | **Windows Temp** | `C:\Windows\Temp` (vía robocopy `/MIR`) |
| 2 | **Temp de usuario** | `%TEMP%` |
| 3 | **Prefetch** | `C:\Windows\Prefetch` |
| 4 | **Recientes** | Accesos directos recientes de `Recent` (preserva los anclados del Explorador) |
| 5 | **Cola de impresión** | `spool\PRINTERS` (con reinicio del spooler) |
| 6 | **Cache DNS** | `ipconfig /flushdns` |
| 7 | **Windows Update** | `SoftwareDistribution\Download` (reinicia servicios) |
| 8 | **Miniaturas** | `thumbcache_*.db` (reinicia explorer) |
| 9 | **Icon Cache** | `IconCache.db`, `iconcache_*.db` |
| 10 | **Microsoft Store** | `wsreset.exe` |
| 11 | **Delivery Optimization** | Caché compartida de Windows Update |
| 12 | **Memory dumps** | `Minidump`, `MEMORY.DMP`, CBS logs, WER |
| 13 | **Cache de fuentes** | `FontCache*.dat`, `FNTCACHE.DAT` |
| 14 | **Papelera de reciclaje** | `Clear-RecycleBin` |
| 15 | **Registros de eventos** | `wevtutil cl` + `.evtx` + Panther + CBS + DISM |
| 16 | **Caché GPU drivers** | NVIDIA / AMD / Intel |
| 17 | **Caché Discord** | Cierra Discord y limpia su caché |
| 18 | **Optimización SSD** | `defrag C: /L` (TRIM) |

### Módulos opcionales (al final de la limpieza interactiva)

#### 🎮 Shadercache (CS2 / Steam)
- Ruta por defecto: `C:\Program Files (x86)\Steam\steamapps\shadercache\730`
- Detecta si Steam está abierto y avisa antes de borrar
- Guarda la ruta en `shadercache.txt`

#### 🖱️ Configuración de ratón
- Importa `raton.reg` al registro de Windows

### Cómo usar

**Modo interactivo**
1. Descarga `RNX_Cache_Cleaner.bat`
2. Clic derecho → **Ejecutar como administrador** (también se autoeleva)
3. Navega con **flechas** y **Enter**, o pulsa el número de la opción
4. Al final verás cuántos MB has liberado

**Modo silencioso**
1. Crea un acceso directo al `.bat`
2. Propiedades → en "Destino" añade ` /completa` (o el modo que quieras)
3. Marca "Ejecutar como administrador" en opciones avanzadas

**Programar limpieza semanal**
1. Programador de tareas → Crear tarea
2. Acción: ejecutar `RNX_Cache_Cleaner.bat` con argumento `/rapida`
3. Marcar "Ejecutar con los privilegios más altos"
4. Activador: semanal

### Requisitos

- **Windows 10 u 11** (necesario para los colores neón ANSI; en 7/8 funciona pero sin color)
- PowerShell 5.0+ (incluido por defecto)
- Permisos de administrador (se solicitan solos)
- Steam (solo para el módulo shadercache)
- GPU + NVIDIA Profile Inspector + un `.nip` (solo para el módulo NVIDIA)

### Archivos auxiliares

| Archivo | Propósito | Se crea solo |
|---------|-----------|--------------|
| `shadercache.txt` | Ruta guardada de shadercache | Sí |
| `raton.reg` | Config de ratón a importar | No (lo aportas tú) |
| `RNX_Cleaner.log` | Log con timestamps | Sí |
| `CS2_Profile.nip` | Perfil NVIDIA para CS2 | No (lo aportas tú) |
| `nvidiaProfileInspector.exe` | Herramienta de importación | No (descárgalo) |
| `RNX_Backup_Userdata.ps1` | Motor del backup de Steam | No (viene con el repo) |
| `backups\` | ZIP de config por SteamID | Sí |
| `.gitattributes` | CRLF en los scripts + LFS para los ZIP | No (viene con el repo) |

### Notas de seguridad

- ⚠️ Los archivos eliminados no se pueden recuperar.
- ⚠️ Borrar registros de eventos dificulta el diagnóstico posterior.
- ⚠️ El módulo Discord cierra Discord automáticamente.
- ⚠️ El módulo de ratón y el de NVIDIA modifican el registro / perfiles del driver. El perfil NVIDIA solo afecta a CS2; el de ratón es global (haz copia de tu `.reg` actual si te preocupa).
- ⚠️ Cierra CS2 y Steam antes de usar el módulo shadercache.

---

## English

### Description

**RNX Cache Cleaner Pro v4.0** is a Windows cleaning and optimization tool with a **cyberpunk/neon interface** and **arrow-key navigation**. It cleans temporary files, caches and logs, and adds gamer-focused modules: CS2/Steam shader cache, GPU driver cache, and a **new NVIDIA profile module** that imports a CS2-optimized `.nip`.

> **What's new in v4.0:** full visual redesign (ANSI neon colors, banner, arrow-navigable menu with ↑↓ and Enter), fast loading animations (~0.5s), automatic GPU detection, and an NVIDIA Profile Inspector import module for CS2.

### New interface

- **Real neon colors** via ANSI escape sequences (cyan, magenta, yellow on black)
- **Navigable menu**: move with **arrows ↑↓**, confirm with **Enter**, or press the **number** directly
- **Highlighted option** with neon background so you always see where you are
- **Loading animations** on each task
- **GPU detection**: the NVIDIA module only appears if you have an NVIDIA card

> Note: ANSI colors require Windows 10/11. The script enables support automatically via registry (`VirtualTerminalLevel`).

### Main menu

| Option | Description |
|--------|-------------|
| **[1] QUICK Cleanup** | Temp, GPU cache, DNS, Prefetch, recent, events |
| **[2] FULL Cleanup** | Everything automatic + SSD TRIM at the end |
| **[3] CUSTOM Cleanup** | Y/N prompt for each category (18 options) |
| **[4] SHADERCACHE** | Directly deletes the shadercache folder (CS2/Steam) |
| **[5] MOUSE CONFIG** | Imports `raton.reg` |
| **[6] VIEW LOG** | Shows the last 30 lines of the log |
| **[7] NVIDIA CS2 PROFILE** 🆕 | Imports an optimized `.nip` (only if NVIDIA GPU) |
| **[0 / Exit]** | Closes the script |

### Silent mode (arguments)

```bat
RNX_Cache_Cleaner.bat /rapida     :: Quick cleanup
RNX_Cache_Cleaner.bat /completa   :: Full cleanup
RNX_Cache_Cleaner.bat /todo       :: EVERYTHING: full + shadercache + mouse + NVIDIA profile
RNX_Cache_Cleaner.bat /shader     :: Shadercache only
RNX_Cache_Cleaner.bat /raton      :: Mouse config only
RNX_Cache_Cleaner.bat /nvidia     :: NVIDIA CS2 profile only
RNX_Cache_Cleaner.bat /backup     :: Steam config backup only
```

Ideal for desktop shortcuts or scheduled tasks (menu and intros are fully skipped).

> **Automatic backup**: with **any** argument (`/todo`, `/completa`, `/rapida`, …) a backup of your Steam configuration is written to `backups\` first. See [Steam configuration backup](#-steam-configuration-backup-userdata).

> **`/todo` vs `/completa`**: `/completa` only runs the system cleanup and exits. `/todo` does all of that and **also** deletes the shadercache (using the saved or default path), applies `raton.reg`, and imports the NVIDIA profile — all without asking. If any file is missing (raton.reg, .nip, etc.), it's simply skipped and noted in the log.

> **The log is overwritten** on each run: `RNX_Cleaner.log` always contains only the last pass, with its date and time on the first line.

### 🆕 Steam configuration backup (userdata)

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

Details:

- **Steam is located automatically** through the registry (`HKCU\Software\Valve\Steam`), falling back to the usual install paths.
- **Excluded**: `760` (screenshots), `gamerecordings` (video clips) and the caches Steam rebuilds by itself (`inventorymsgcache`, `ugc`, `ugcmsgcache`). The list lives in `BACKUP_EXCLUIR` inside the `.bat` — add `config` there too if you don't want your launch options and Steam settings either.
- **Files over 500 MB are skipped** (tune `BACKUP_MAX_MB` in the `.bat`). If a ZIP still ends up over 100 MB the script warns you: GitHub rejects files that big, so you'd need to lower the limit to leave the largest savegames out.
- **The ZIP is only rewritten when something changed**: the account hash is compared against `_indice.txt`, so the repository doesn't grow on every run.
- **Locked files** (Steam running) are skipped and noted in the log; everything else is still saved.
- The `backups\` folder is meant to be **synced to GitHub** (`git add backups && git commit && git push`).

**Git LFS** — the repository ships a `.gitattributes` with `backups/*.zip filter=lfs`, so the ZIPs travel through [Git Large File Storage](https://git-lfs.com/) instead of the regular history. That raises the per-file limit from 100 MB to 2 GB. To enable it on your clone:

```bat
git lfs install
git add .gitattributes backups
git commit -m "Backup userdata"
git push
```

> ⚠️ **Quota**: a free GitHub account gets **1 GB of LFS storage and 1 GB of bandwidth per month**, and LFS keeps **every version** of the ZIP forever. With a ~250 MB ZIP you burn through that in 4 pushes. If your savegames bloat the backup, lower `BACKUP_MAX_MB` or exclude more folders instead of buying data packs.

> ⚠️ **Privacy**: `localconfig.vdf` holds your SteamID, library, launch options and friend nicknames. Review it before pushing `backups\` to a **public** repository — it stays in git history even if you delete it later.

### 🆕 NVIDIA CS2 Profile module

Imports a CS2-optimized `.nip` profile directly into the NVIDIA driver, using **NVIDIA Profile Inspector** in silent mode (without opening its UI).

**How it works:**
1. Automatically detects whether your GPU is NVIDIA (if not, the module doesn't appear)
2. Imports the CS2 profile silently with `-silentImport`
3. The profile affects only CS2, not your global configuration

**Module requirements (you must provide these):**

| File | Where to place it | Where to get it |
|------|-------------------|-----------------|
| `nvidiaProfileInspector.exe` | Next to the script or in `tools\` | [github.com/Orbmu2k/nvidiaProfileInspector](https://github.com/Orbmu2k/nvidiaProfileInspector) |
| `CS2_Profile.nip` | Next to the script (or you'll be asked) | Export it yourself from Profile Inspector, or use a community one |

> ⚠️ **Important about `.nip` files**: always review the contents of a third-party `.nip` before applying it, since NVIDIA Profile Inspector exposes undocumented, driver-version-specific settings.

### Available cleanup areas

| # | Category | What it cleans |
|---|----------|----------------|
| 1 | **Windows Temp** | `C:\Windows\Temp` (via robocopy `/MIR`) |
| 2 | **User Temp** | `%TEMP%` |
| 3 | **Prefetch** | `C:\Windows\Prefetch` |
| 4 | **Recent files** | Recent shortcuts in `Recent` (preserves Explorer pinned items) |
| 5 | **Print queue** | `spool\PRINTERS` (with spooler restart) |
| 6 | **DNS cache** | `ipconfig /flushdns` |
| 7 | **Windows Update** | `SoftwareDistribution\Download` (restarts services) |
| 8 | **Thumbnails** | `thumbcache_*.db` (restarts explorer) |
| 9 | **Icon Cache** | `IconCache.db`, `iconcache_*.db` |
| 10 | **Microsoft Store** | `wsreset.exe` |
| 11 | **Delivery Optimization** | Windows Update shared cache |
| 12 | **Memory dumps** | `Minidump`, `MEMORY.DMP`, CBS logs, WER |
| 13 | **Font cache** | `FontCache*.dat`, `FNTCACHE.DAT` |
| 14 | **Recycle Bin** | `Clear-RecycleBin` |
| 15 | **Event logs** | `wevtutil cl` + `.evtx` + Panther + CBS + DISM |
| 16 | **GPU driver cache** | NVIDIA / AMD / Intel |
| 17 | **Discord cache** | Closes Discord and clears its cache |
| 18 | **SSD optimization** | `defrag C: /L` (TRIM) |

### Optional modules (after interactive cleanup)

#### 🎮 Shadercache (CS2 / Steam)
- Default path: `C:\Program Files (x86)\Steam\steamapps\shadercache\730`
- Detects if Steam is running and warns before deletion
- Saves path in `shadercache.txt`

#### 🖱️ Mouse configuration
- Imports `raton.reg` into the Windows registry

### How to use

**Interactive mode**
1. Download `RNX_Cache_Cleaner.bat`
2. Right-click → **Run as administrator** (also self-elevates)
3. Navigate with **arrows** and **Enter**, or press the option number
4. At the end you'll see how many MB you freed

**Silent mode**
1. Create a shortcut to the `.bat`
2. Properties → in "Target" append ` /completa` (or your chosen mode)
3. Check "Run as administrator" in advanced options

**Schedule weekly cleanup**
1. Task Scheduler → Create task
2. Action: run `RNX_Cache_Cleaner.bat` with argument `/rapida`
3. Check "Run with highest privileges"
4. Trigger: weekly

### Requirements

- **Windows 10 or 11** (needed for neon ANSI colors; works on 7/8 but without color)
- PowerShell 5.0+ (included by default)
- Administrator permissions (auto-requested)
- Steam (only for shadercache module)
- NVIDIA GPU + NVIDIA Profile Inspector + a `.nip` (only for NVIDIA module)

### Auxiliary files

| File | Purpose | Auto-created |
|------|---------|--------------|
| `shadercache.txt` | Saved shadercache path | Yes |
| `raton.reg` | Mouse config to import | No (you provide it) |
| `RNX_Cleaner.log` | Timestamped log | Yes |
| `CS2_Profile.nip` | NVIDIA profile for CS2 | No (you provide it) |
| `nvidiaProfileInspector.exe` | Import tool | No (download it) |
| `RNX_Backup_Userdata.ps1` | Steam backup engine | No (ships with the repo) |
| `backups\` | Per-SteamID config ZIPs | Yes |
| `.gitattributes` | CRLF for scripts + LFS for the ZIPs | No (ships with the repo) |

### Safety notes

- ⚠️ Deleted files cannot be recovered.
- ⚠️ Deleting event logs makes later diagnosis harder.
- ⚠️ The Discord module closes Discord automatically.
- ⚠️ The mouse and NVIDIA modules modify the registry / driver profiles. The NVIDIA profile only affects CS2; the mouse one is global (back up your current `.reg` if concerned).
- ⚠️ Close CS2 and Steam before using the shadercache module.

---

### Changelog

#### v4.4 (current)
- 🆕 **Backup automático de la configuración de Steam**: con cualquier argumento (`/todo`, `/completa`, …) se guarda `userdata` completo en `backups\<STEAMID>.zip` antes de limpiar, con la estructura de Steam intacta dentro del ZIP
- 🆕 Nuevo argumento `/backup` (solo la copia de seguridad, sin limpiar)
- 🆕 Nuevo módulo `RNX_Backup_Userdata.ps1`: detecta Steam por registro, omite archivos de más de `BACKUP_MAX_MB` y **solo reescribe el ZIP si el contenido cambió** (hash en `_indice.txt`)
- 🆕 Lista de exclusiones configurable en `BACKUP_EXCLUIR`: por defecto capturas (`760`), clips (`gamerecordings`) y cachés regenerables (`inventorymsgcache`, `ugc`, `ugcmsgcache`)
- 🆕 `.gitattributes` con Git LFS para `backups/*.zip`
- 🐛 **Corregido: el `.bat` tenía finales de línea Unix (LF)**. `cmd` se descuadraba al parsear y se comía el primer carácter de algunas líneas (`call` → `all`), abortando la ejecución a medias. Ahora el archivo es CRLF y `.gitattributes` lo fuerza
- 🐛 Corregido: los `[>]` de los `echo` no estaban escapados, así que `cmd` los interpretaba como redirección — los títulos de sección salían en blanco y se creaba un archivo basura llamado `]`

#### v4.3
- 🐛 Corregido: la limpieza de "Recientes" ya no borra las Jump Lists (`AutomaticDestinations`/`CustomDestinations`), que es donde Windows guarda los elementos anclados del panel del Explorador. Ahora solo borra los accesos recientes sueltos, así tus anclados (Desktop, Downloads, etc.) se mantienen.

#### v4.2
- El perfil NVIDIA CS2 ya no hace backup de la config previa (el perfil solo afecta a CS2)
- La limpieza completa (opción [2] y `/completa`) ahora aplica también el perfil NVIDIA CS2

#### v4.1
- 🆕 Modo `/todo`: limpieza completa + shadercache + ratón + perfil NVIDIA, todo desatendido
- 🆕 El log ahora se sobrescribe en cada ejecución (solo queda la última, con fecha/hora)
- Shadercache y ratón ahora se registran en el log también en modo automático

#### v4.0
- 🆕 Full cyberpunk/neon UI redesign (ANSI colors, banner, highlighted items)
- 🆕 Arrow-key menu navigation (↑↓ + Enter) plus number shortcuts
- 🆕 Fast loading animations (~0.5s) replacing static progress screens
- 🆕 Automatic GPU brand detection
- 🆕 NVIDIA CS2 profile module (silent `.nip` import via Profile Inspector)
- 🆕 `/nvidia` silent-mode argument
- File renamed to `RNX_Cache_Cleaner.bat` (version no longer in filename)

#### v3.1
- GPU driver cache, Discord cache, SSD TRIM, silent-mode arguments

#### v3.0
- Interactive menu, three cleanup modes, 8 new categories, freed-space measurement, logging

#### v2.0
- Initial public release

---

### Autor / Author
RNX Cache Cleaner Pro v4.0

### Licencia / License
Libre para usar / Free to use
