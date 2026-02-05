# CS2-Cache-Cleaner / Limpiador de Caché CS2

[English](#english) | [Español](#español)

---

## Español

### Descripción

**RNX Cache Cleaner Pro v2.0** es una herramienta de limpieza del sistema Windows diseñada para optimizar el rendimiento eliminando archivos temporales, caché y archivos innecesarios. Incluye un módulo especializado para limpiar la caché de shaders de Counter-Strike 2 (CS2) en Steam.

### ¿Qué hace el archivo .bat?

El script realiza dos funciones principales:

#### 1️⃣ **Limpieza General del Sistema (Automática)**

El script limpia automáticamente los siguientes directorios:

| Ubicación | Descripción | Progreso |
|-----------|-------------|----------|
| `C:\Windows\Temp\*` | Archivos temporales del sistema | 10% |
| `C:\Windows\Prefetch\*` | Caché de prefetch (optimización de inicio) | 25% |
| `%TEMP%\*` | Carpeta temporal del usuario | 40% |
| `C:\Windows\*.tmp` y `*.log` | Archivos temporales y registros | 55% |
| `%USERPROFILE%\Recent\*` | Archivos recientes | 70% |
| `C:\Windows\Spool\Printers\*` | Cola de impresión | 85% |
| Registros de eventos de Windows | Event Viewer logs | 95% |

#### 2️⃣ **Módulo de Limpieza de Shader Cache (Interactivo)**

Después de completar la limpieza general, el script pregunta si deseas limpiar la caché de shaders:

- **Ruta por defecto**: `C:\Program Files (x86)\Steam\steamapps\shadercache\730`
- **Función**: Guarda la ruta en un archivo `shadercache.txt` para futuras ejecuciones
- **Beneficio**: Mejora significativamente el rendimiento en CS2 (Counter-Strike 2)

### Características

✅ **Elevación automática a administrador** - Se ejecuta con permisos de administrador automáticamente
✅ **Interfaz visual** - Barra de progreso y ASCII art
✅ **Información del sistema** - Muestra versión de Windows, usuario y fecha/hora
✅ **Configuración inteligente** - Recuerda rutas ingresadas previamente
✅ **Manejo de errores** - Verifica si las carpetas existen antes de eliminarlas
✅ **Seguro** - Solo elimina archivos que Windows puede regenerar sin problema

### Cómo usar

1. **Descarga** el archivo `RNX Cache Cleaner.bat`
2. **Haz clic derecho** en el archivo
3. **Selecciona "Ejecutar como administrador"**
4. El script limpiará automáticamente el sistema
5. Se abrirá el módulo de shader cache (puedes presionar `N` para saltarlo)
6. Presiona cualquier tecla para salir

### Requisitos

- Windows 7 o superior
- Permisos de administrador
- Steam instalado (opcional, solo para el módulo de shader cache)

### Notas de seguridad

- ⚠️ Los archivos eliminados no se pueden recuperar
- ⚠️ Se recomienda cerrar todas las aplicaciones antes de ejecutar
- ⚠️ Los cambios toman efecto inmediatamente después de la ejecución

---

## English

### Description

**RNX Cache Cleaner Pro v2.0** is a Windows system cleaning tool designed to optimize performance by removing temporary files, cache, and unnecessary files. It includes a specialized module for cleaning the Counter-Strike 2 (CS2) shader cache from Steam.

### What does the .bat file do?

The script performs two main functions:

#### 1️⃣ **General System Cleaning (Automatic)**

The script automatically cleans the following directories:

| Location | Description | Progress |
|----------|-------------|----------|
| `C:\Windows\Temp\*` | System temporary files | 10% |
| `C:\Windows\Prefetch\*` | Prefetch cache (startup optimization) | 25% |
| `%TEMP%\*` | User's temporary folder | 40% |
| `C:\Windows\*.tmp` and `*.log` | Temporary files and logs | 55% |
| `%USERPROFILE%\Recent\*` | Recent files | 70% |
| `C:\Windows\Spool\Printers\*` | Print queue | 85% |
| Windows Event logs | Event Viewer logs | 95% |

#### 2️⃣ **Shader Cache Cleaning Module (Interactive)**

After completing the general cleanup, the script asks if you want to clean the shader cache:

- **Default path**: `C:\Program Files (x86)\Steam\steamapps\shadercache\730`
- **Function**: Saves the path in a `shadercache.txt` file for future runs
- **Benefit**: Significantly improves CS2 (Counter-Strike 2) performance

### Features

✅ **Automatic admin elevation** - Runs with administrator permissions automatically
✅ **Visual interface** - Progress bar and ASCII art
✅ **System information** - Displays Windows version, user, and date/time
✅ **Smart configuration** - Remembers previously entered paths
✅ **Error handling** - Checks if folders exist before deleting them
✅ **Safe** - Only deletes files that Windows can regenerate without issues

### How to use

1. **Download** the `RNX Cache Cleaner.bat` file
2. **Right-click** on the file
3. **Select "Run as administrator"**
4. The script will automatically clean the system
5. The shader cache module will open (you can press `N` to skip it)
6. Press any key to exit

### Requirements

- Windows 7 or later
- Administrator permissions
- Steam installed (optional, only for shader cache module)

### Safety notes

- ⚠️ Deleted files cannot be recovered
- ⚠️ It is recommended to close all applications before running
- ⚠️ Changes take effect immediately after execution

---

### Autor / Author
RNX Cache Cleaner Pro v2.0

### Licencia / License
Libre para usar / Free to use
