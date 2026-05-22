# CS2-Cache-Cleaner / Limpiador de Caché CS2

[English](#english) | [Español](#español)

---

## Español

### Descripción

**RNX Cache Cleaner Pro v3.1** es una herramienta de limpieza y optimización del sistema Windows diseñada para liberar espacio, mejorar el rendimiento y eliminar archivos temporales, cachés y logs. Incluye módulos especializados para gamers (limpieza de shader cache de CS2 / Steam y caché de drivers gráficos) y configuración de periférico (importación de configuración de ratón).

> **Novedad v3.1:** caché de drivers gráficos (NVIDIA/AMD/Intel), caché de Discord, optimización SSD con TRIM al final, y **modo silencioso por argumentos** para automatización.

### ¿Qué hace el script?

Al ejecutarse se eleva automáticamente a administrador y muestra un **menú principal** con las siguientes opciones:

| Opción | Descripción |
|--------|-------------|
| **[1] Limpieza RÁPIDA** | Limpieza esencial + caché GPU sin preguntar (recomendada para uso diario) |
| **[2] Limpieza COMPLETA** | Todo automático + caché GPU + TRIM SSD al final |
| **[3] Limpieza PERSONALIZADA** | Pregunta Y/N a cada categoría (18 opciones) |
| **[4] Solo SHADERCACHE** | Borra directamente la carpeta shadercache (CS2/Steam) |
| **[5] Solo CONFIG RATÓN** | Importa directamente el archivo `raton.reg` |
| **[6] Ver LOG** | Muestra las últimas 30 líneas del registro de operaciones |
| **[0] Salir** | Cierra el script |

### 🆕 Modo silencioso (argumentos de línea de comandos)

Puedes saltarte el menú lanzando el script con un argumento. Útil para accesos directos en el escritorio o tareas programadas:

```bat
RNX_Cache_Cleaner_v3.bat /rapida     :: Limpieza rápida sin preguntar nada
RNX_Cache_Cleaner_v3.bat /completa   :: Limpieza completa sin preguntar nada
RNX_Cache_Cleaner_v3.bat /shader     :: Solo borrar shadercache
RNX_Cache_Cleaner_v3.bat /raton      :: Solo importar raton.reg
```

**Ejemplo: programar limpieza semanal automática**

1. Abre el **Programador de tareas** de Windows
2. Crear tarea → ejecutar `RNX_Cache_Cleaner_v3.bat` con argumento `/rapida`
3. Marcar "Ejecutar con los privilegios más altos"
4. Activador: semanal, domingo a las 3:00

### Áreas de limpieza disponibles

| # | Categoría | Qué limpia |
|---|-----------|------------|
| 1 | **Windows Temp** | `C:\Windows\Temp` (vía robocopy `/MIR`, ultra rápido) |
| 2 | **Temp de usuario** | `%TEMP%` |
| 3 | **Prefetch** | `C:\Windows\Prefetch` |
| 4 | **Recientes** | `Recent`, `AutomaticDestinations`, `CustomDestinations` |
| 5 | **Cola de impresión** | `C:\Windows\System32\spool\PRINTERS` (con reinicio del servicio spooler) |
| 6 | **Cache DNS** | `ipconfig /flushdns` |
| 7 | **Windows Update** | `SoftwareDistribution\Download` (con reinicio de wuauserv, bits, cryptsvc) |
| 8 | **Miniaturas** | `thumbcache_*.db` (reinicia explorer.exe automáticamente) |
| 9 | **Icon Cache** | `IconCache.db` y `iconcache_*.db` |
| 10 | **Microsoft Store** | `wsreset.exe` para resetear la caché del Store |
| 11 | **Delivery Optimization** | Caché de descarga compartida de Windows Update |
| 12 | **Memory dumps** | `Minidump`, `MEMORY.DMP`, CBS logs, WER reports |
| 13 | **Cache de fuentes** | `FontCache*.dat`, `FNTCACHE.DAT` |
| 14 | **Papelera de reciclaje** | Vacía la papelera vía PowerShell `Clear-RecycleBin` |
| 15 | **Registros de eventos** | `wevtutil cl` + archivos `.evtx` físicos + logs de `Panther`, `CBS`, `DISM` |
| 16 🆕 | **Caché GPU drivers** | NVIDIA (`DXCache`, `GLCache`, `ComputeCache`, `NV_Cache`), AMD (`DxCache`, `GLCache`), Intel (`ShaderCache`) |
| 17 🆕 | **Caché Discord** | `Cache`, `Code Cache`, `GPUCache` de Discord (Stable, PTB, Canary). Cierra Discord automáticamente |
| 18 🆕 | **Optimización SSD** | `defrag C: /L` ejecuta TRIM en SSDs (consolida bloques libres tras la limpieza) |

### Módulos opcionales (al final de la limpieza interactiva)

#### 🎮 Módulo Shadercache (CS2 / Steam)

- **Ruta por defecto**: `C:\Program Files (x86)\Steam\steamapps\shadercache\730` (CS2 = 730)
- **Verificación de Steam**: Detecta si `steam.exe` está corriendo y avisa antes de borrar
- **Memoria de rutas**: Guarda la ruta usada en `shadercache.txt`
- **Beneficio**: Fuerza recompilación de shaders y puede mejorar rendimiento / arreglar glitches en CS2
- **Complemento ideal**: Combinarlo con la opción 16 (caché GPU drivers) para una limpieza gráfica total

#### 🖱️ Módulo Configuración de Ratón

- Importa el archivo `raton.reg` (mismo directorio del script) al registro de Windows
- Si no encuentra el archivo, permite indicar la ruta manualmente
- Útil para aplicar configuración de sensibilidad / aceleración tras formatear

### Características principales v3.1

✅ **Elevación automática a administrador**
✅ **Menú interactivo** con 7 opciones
✅ **Tres modos de limpieza**: Rápida, Completa, Personalizada (18 categorías)
✅ **🆕 Modo silencioso** con argumentos `/rapida`, `/completa`, `/shader`, `/raton`
✅ **🆕 Caché de drivers gráficos** (NVIDIA / AMD / Intel) — ideal para gamers
✅ **🆕 Limpieza de Discord** con cierre automático del proceso
✅ **🆕 TRIM/optimización SSD** al final de la limpieza completa
✅ **Medición de espacio liberado** en MB (vía PowerShell `Get-PSDrive`)
✅ **Sistema de log** con timestamp en `RNX_Cleaner.log`
✅ **Barra de progreso visual** con bloques `[##########]`
✅ **Header unificado** (subrutina reutilizada — sin código duplicado)
✅ **Uso de `robocopy /MIR`** para vaciar carpetas grandes en segundos
✅ **Detección de procesos** (Steam, Discord) antes de borrar
✅ **Limpieza profunda de logs**: eventos + `.evtx` + Panther + CBS + DISM
✅ **Manejo de errores** con códigos de color (verde OK, rojo ERROR, amarillo AVISO)
✅ **Memoria de configuración** (shadercache.txt, raton.reg)

### Cómo usar

**Modo interactivo (normal)**
1. **Descarga** el archivo `RNX_Cache_Cleaner_v3.bat`
2. **Haz clic derecho** → **"Ejecutar como administrador"** (también se autoeleva con doble clic)
3. **Elige una opción** del menú principal
4. Al final, el script muestra cuántos MB has liberado

**Modo silencioso (avanzado)**
1. Crea un acceso directo al `.bat`
2. Click derecho → Propiedades → en "Destino" añade ` /rapida` al final
3. Marca "Ejecutar como administrador" en propiedades avanzadas
4. Doble clic ejecuta limpieza directa sin menú

### Requisitos

- Windows 10 o Windows 11 recomendado (compatible desde Windows 7)
- PowerShell 5.0+ (incluido en Windows 10/11 por defecto)
- Permisos de administrador (el script los solicita automáticamente)
- Steam instalado (solo si vas a usar el módulo shadercache)

### Archivos auxiliares (opcionales)

| Archivo | Propósito | Se crea automáticamente |
|---------|-----------|------------------------|
| `shadercache.txt` | Guarda la ruta de shadercache para no volver a pedirla | Sí, al primer uso |
| `raton.reg` | Configuración del ratón que se importa al registro | No (lo tienes que aportar tú) |
| `RNX_Cleaner.log` | Log con timestamp de todas las operaciones realizadas | Sí, al primer uso |

### Notas de seguridad

- ⚠️ Los archivos eliminados **NO se pueden recuperar** desde la papelera (se borran directamente).
- ⚠️ Borrar **registros de eventos** dificulta el diagnóstico de problemas pasados.
- ⚠️ La opción de **caché de Discord** cierra Discord automáticamente. Guarda lo que tengas abierto antes.
- ⚠️ El módulo de ratón modifica el **registro de Windows**. Asegúrate de confiar en `raton.reg`.
- ⚠️ Cierra **CS2 y Steam** antes de usar el módulo shadercache.
- ⚠️ La primera ejecución tras una actualización mayor de Windows puede liberar varios GB.

---

## English

### Description

**RNX Cache Cleaner Pro v3.1** is a Windows system cleaning and optimization tool designed to free disk space, improve performance, and remove temporary files, caches, and logs. It includes specialized modules for gamers (CS2 / Steam shader cache and GPU driver cache cleaning) and peripheral configuration (mouse settings import).

> **What's new in v3.1:** GPU driver cache (NVIDIA/AMD/Intel), Discord cache, SSD TRIM optimization at the end, and **silent mode via command-line arguments** for automation.

### What does the script do?

When launched, it auto-elevates to administrator and shows a **main menu** with the following options:

| Option | Description |
|--------|-------------|
| **[1] QUICK Cleanup** | Essential cleanup + GPU cache, no questions (recommended for daily use) |
| **[2] FULL Cleanup** | Everything automatic + GPU cache + SSD TRIM at the end |
| **[3] CUSTOM Cleanup** | Y/N prompt for each category (18 options) |
| **[4] Shadercache only** | Directly deletes the shadercache folder (CS2 / Steam) |
| **[5] Mouse config only** | Directly imports the `raton.reg` file |
| **[6] View LOG** | Shows the last 30 lines of the operations log |
| **[0] Exit** | Closes the script |

### 🆕 Silent mode (command-line arguments)

You can skip the menu by launching the script with an argument. Useful for desktop shortcuts or scheduled tasks:

```bat
RNX_Cache_Cleaner_v3.bat /rapida     :: Quick cleanup, no prompts
RNX_Cache_Cleaner_v3.bat /completa   :: Full cleanup, no prompts
RNX_Cache_Cleaner_v3.bat /shader     :: Shadercache only
RNX_Cache_Cleaner_v3.bat /raton      :: Mouse config only
```

**Example: schedule weekly automatic cleanup**

1. Open **Task Scheduler**
2. Create task → run `RNX_Cache_Cleaner_v3.bat` with argument `/rapida`
3. Check "Run with highest privileges"
4. Trigger: weekly, Sunday at 3:00 AM

### Available cleanup areas

| # | Category | What it cleans |
|---|----------|----------------|
| 1 | **Windows Temp** | `C:\Windows\Temp` (via robocopy `/MIR`, ultra fast) |
| 2 | **User Temp** | `%TEMP%` |
| 3 | **Prefetch** | `C:\Windows\Prefetch` |
| 4 | **Recent files** | `Recent`, `AutomaticDestinations`, `CustomDestinations` |
| 5 | **Print queue** | `C:\Windows\System32\spool\PRINTERS` (with spooler restart) |
| 6 | **DNS cache** | `ipconfig /flushdns` |
| 7 | **Windows Update** | `SoftwareDistribution\Download` (with wuauserv, bits, cryptsvc restart) |
| 8 | **Thumbnails** | `thumbcache_*.db` (automatically restarts explorer.exe) |
| 9 | **Icon Cache** | `IconCache.db` and `iconcache_*.db` |
| 10 | **Microsoft Store** | `wsreset.exe` to reset Store cache |
| 11 | **Delivery Optimization** | Windows Update shared download cache |
| 12 | **Memory dumps** | `Minidump`, `MEMORY.DMP`, CBS logs, WER reports |
| 13 | **Font cache** | `FontCache*.dat`, `FNTCACHE.DAT` |
| 14 | **Recycle Bin** | Empties via PowerShell `Clear-RecycleBin` |
| 15 | **Event logs** | `wevtutil cl` + physical `.evtx` files + `Panther`, `CBS`, `DISM` logs |
| 16 🆕 | **GPU driver cache** | NVIDIA (`DXCache`, `GLCache`, `ComputeCache`, `NV_Cache`), AMD (`DxCache`, `GLCache`), Intel (`ShaderCache`) |
| 17 🆕 | **Discord cache** | Discord's `Cache`, `Code Cache`, `GPUCache` (Stable, PTB, Canary). Auto-closes Discord |
| 18 🆕 | **SSD optimization** | `defrag C: /L` runs TRIM on SSDs (consolidates free blocks after cleanup) |

### Optional modules (after interactive cleanup)

#### 🎮 Shadercache module (CS2 / Steam)

- **Default path**: `C:\Program Files (x86)\Steam\steamapps\shadercache\730` (CS2 = 730)
- **Steam detection**: Checks if `steam.exe` is running and warns before deletion
- **Path memory**: Saves the used path in `shadercache.txt`
- **Benefit**: Forces shader recompilation and can improve performance / fix glitches in CS2
- **Perfect combo**: Pair it with option 16 (GPU driver cache) for total graphics cleanup

#### 🖱️ Mouse configuration module

- Imports the `raton.reg` file (same directory as the script) into the Windows registry
- If not found, lets you specify the path manually

### Key features v3.1

✅ **Automatic admin elevation**
✅ **Interactive menu** with 7 options
✅ **Three cleanup modes**: Quick, Full, Custom (18 categories)
✅ **🆕 Silent mode** via `/rapida`, `/completa`, `/shader`, `/raton` arguments
✅ **🆕 GPU driver cache** (NVIDIA / AMD / Intel) — ideal for gamers
✅ **🆕 Discord cleanup** with automatic process closure
✅ **🆕 SSD TRIM/optimization** at the end of full cleanup
✅ **Freed space measurement** in MB
✅ **Logging system** with timestamps
✅ **Visual progress bar**
✅ **`robocopy /MIR` usage** for fast large folder cleaning
✅ **Process detection** (Steam, Discord) before deletion
✅ **Deep log cleanup**: events + `.evtx` + Panther + CBS + DISM

### How to use

**Interactive mode (normal)**
1. **Download** `RNX_Cache_Cleaner_v3.bat`
2. **Right-click** → **"Run as administrator"** (also self-elevates on double-click)
3. **Choose an option** from the main menu
4. At the end, the script shows how many MB you freed

**Silent mode (advanced)**
1. Create a shortcut to the `.bat`
2. Right-click → Properties → in "Target" append ` /rapida` at the end
3. Check "Run as administrator" in advanced properties
4. Double-click runs cleanup directly without menu

### Requirements

- Windows 10 or Windows 11 recommended (compatible from Windows 7)
- PowerShell 5.0+ (included in Windows 10/11 by default)
- Administrator permissions (the script requests them automatically)
- Steam installed (only if using the shadercache module)

### Auxiliary files (optional)

| File | Purpose | Auto-created |
|------|---------|--------------|
| `shadercache.txt` | Stores the shadercache path so it's not asked again | Yes, on first use |
| `raton.reg` | Mouse configuration imported into registry | No (you must provide it) |
| `RNX_Cleaner.log` | Timestamped log of all operations performed | Yes, on first use |

### Safety notes

- ⚠️ Deleted files **CANNOT be recovered** from the recycle bin.
- ⚠️ Deleting **event logs** makes diagnosing past problems harder.
- ⚠️ The **Discord cache** option auto-closes Discord. Save anything important first.
- ⚠️ The mouse module modifies the **Windows registry**. Make sure you trust `raton.reg`.
- ⚠️ Close **CS2 and Steam** before using the shadercache module.
- ⚠️ First run after a major Windows update may free several GB.

---

### Changelog

#### v3.1 (current)
- 🆕 GPU driver cache cleanup (NVIDIA / AMD / Intel)
- 🆕 Discord cache cleanup with automatic process closure
- 🆕 SSD TRIM/optimization (`defrag /L`) at end of full cleanup
- 🆕 Silent mode via command-line arguments (`/rapida`, `/completa`, `/shader`, `/raton`)
- GPU cache added to Quick Cleanup (gaming-oriented)
- 3 new categories in Custom Cleanup (16, 17, 18)
- README expanded with Task Scheduler example

#### v3.0
- Interactive main menu with 7 options
- Three cleanup modes: Quick / Full / Custom
- 8 new cleanup categories (DNS, Windows Update, Thumbnails, Icon Cache, Store, Delivery Optimization, Memory Dumps, Fonts, Recycle Bin)
- Freed space measurement
- Logging system with timestamps
- Reinforced log cleanup (.evtx + Panther + CBS + DISM)
- Steam process detection before shadercache deletion
- `robocopy /MIR` for fast large folder cleaning
- Unified header subroutine

#### v2.0
- Initial public release
- Automatic admin elevation
- Sequential cleanup with progress bar
- Shadercache module with path memory
- Mouse config module

---

### Autor / Author
RNX Cache Cleaner Pro v3.1

### Licencia / License
Libre para usar / Free to use
