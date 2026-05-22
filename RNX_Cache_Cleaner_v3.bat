@echo off
setlocal enabledelayedexpansion

:: ===================================================================
::  RNX CACHE CLEANER PRO v3.0
::  Script de limpieza y optimizacion del sistema
:: ===================================================================

:: ----- Auto-elevacion a administrador -----
:: IMPORTANTE: pasamos %* (todos los argumentos) al proceso elevado.
:: Sin esto, los modos silenciosos (/rapida, /completa, etc.) se pierden
:: al relanzarse el script con UAC.
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "%*", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B
)

:: ----- Variables globales -----
set "VERSION_SCRIPT=3.2"
set "SCRIPT_DIR=%~dp0"
set "LOG_FILE=%SCRIPT_DIR%RNX_Cleaner.log"
set "EMPTY_DIR=%TEMP%\rnx_empty_dir"
for /f "tokens=4-5 delims=. " %%i in ('ver') do set WIN_VERSION=%%i.%%j

:: Crear carpeta vacia para usar con robocopy /MIR (forma rapida de vaciar)
if not exist "%EMPTY_DIR%" mkdir "%EMPTY_DIR%" >nul 2>&1

:: Variables para medir espacio liberado
set "ESPACIO_ANTES=0"
set "ESPACIO_DESPUES=0"
set "ESPACIO_LIBERADO=0"

Title RNX Cache Cleaner Pro v%VERSION_SCRIPT%

:: ===================================================================
::  MODO ARGUMENTOS (ejecucion silenciosa sin menu)
::  Uso: script.bat /rapida   ->  Limpieza rapida directa
::       script.bat /completa ->  Limpieza completa directa
::       script.bat /shader   ->  Solo shadercache
::       script.bat /raton    ->  Solo configuracion raton
:: ===================================================================
set "SILENT_MODE=0"
if /i "%~1"=="/rapida"   ( set "SILENT_MODE=1" & goto LIMPIEZA_RAPIDA )
if /i "%~1"=="/completa" ( set "SILENT_MODE=1" & goto LIMPIEZA_COMPLETA )
if /i "%~1"=="/shader"   ( set "SILENT_MODE=1" & goto SHADERCACHE_DIRECTO )
if /i "%~1"=="/raton"    ( set "SILENT_MODE=1" & goto RATON_DIRECTO )

:: ===================================================================
::  MENU PRINCIPAL
:: ===================================================================
:MENU_PRINCIPAL
cls
color 0B
call :MOSTRAR_HEADER
echo.
echo  -------------------------------------------------------------------
echo                          MENU PRINCIPAL
echo  -------------------------------------------------------------------
echo.
echo    [1] Limpieza RAPIDA       (Temp, Prefetch, Recent, DNS)
echo    [2] Limpieza COMPLETA     (Todo lo anterior + extras)
echo    [3] Limpieza PERSONALIZADA (Elige que limpiar)
echo    [4] Solo SHADERCACHE      (Steam / juegos)
echo    [5] Solo CONFIG RATON     (Importar raton.reg)
echo    [6] Ver LOG de operaciones
echo    [0] Salir
echo.
echo  -------------------------------------------------------------------
set /p OPCION="   Elige una opcion: "

if "!OPCION!"=="1" goto LIMPIEZA_RAPIDA
if "!OPCION!"=="2" goto LIMPIEZA_COMPLETA
if "!OPCION!"=="3" goto LIMPIEZA_PERSONALIZADA
if "!OPCION!"=="4" goto SHADERCACHE_DIRECTO
if "!OPCION!"=="5" goto RATON_DIRECTO
if "!OPCION!"=="6" goto VER_LOG
if "!OPCION!"=="0" goto FIN_SCRIPT
goto MENU_PRINCIPAL

:: ===================================================================
::  LIMPIEZA RAPIDA
:: ===================================================================
:LIMPIEZA_RAPIDA
call :MEDIR_ESPACIO_ANTES
call :LOG "=== INICIO LIMPIEZA RAPIDA ==="

call :PROGRESO 15 "Limpiando Windows Temp..."
call :LIMPIAR_WINDOWS_TEMP

call :PROGRESO 35 "Limpiando Temp de usuario..."
call :LIMPIAR_USER_TEMP

call :PROGRESO 55 "Limpiando Prefetch..."
call :LIMPIAR_PREFETCH

call :PROGRESO 75 "Limpiando archivos recientes..."
call :LIMPIAR_RECENT

call :PROGRESO 82 "Limpiando cache de drivers graficos..."
call :LIMPIAR_GPU_CACHE

call :PROGRESO 88 "Vaciando cache DNS..."
call :LIMPIAR_DNS

call :PROGRESO 95 "Limpiando registros de eventos..."
call :LIMPIAR_EVENTOS

call :PROGRESO 100 "Finalizando..."
call :MEDIR_ESPACIO_DESPUES
call :MOSTRAR_RESUMEN "LIMPIEZA RAPIDA"
call :LOG "=== FIN LIMPIEZA RAPIDA ==="
if "!SILENT_MODE!"=="1" goto FIN_SCRIPT
goto PREGUNTAR_SHADER

:: ===================================================================
::  LIMPIEZA COMPLETA
:: ===================================================================
:LIMPIEZA_COMPLETA
call :MEDIR_ESPACIO_ANTES
call :LOG "=== INICIO LIMPIEZA COMPLETA ==="

call :PROGRESO 8 "Limpiando Windows Temp..."
call :LIMPIAR_WINDOWS_TEMP

call :PROGRESO 14 "Limpiando Temp de usuario..."
call :LIMPIAR_USER_TEMP

call :PROGRESO 20 "Limpiando Prefetch..."
call :LIMPIAR_PREFETCH

call :PROGRESO 26 "Limpiando archivos recientes..."
call :LIMPIAR_RECENT

call :PROGRESO 32 "Limpiando cola de impresion..."
call :LIMPIAR_COLA_IMPRESION

call :PROGRESO 38 "Vaciando cache DNS..."
call :LIMPIAR_DNS

call :PROGRESO 44 "Limpiando cache Windows Update..."
call :LIMPIAR_WUPDATE

call :PROGRESO 50 "Limpiando miniaturas..."
call :LIMPIAR_THUMBNAILS

call :PROGRESO 56 "Limpiando icon cache..."
call :LIMPIAR_ICON_CACHE

call :PROGRESO 62 "Limpiando cache de Microsoft Store..."
call :LIMPIAR_STORE

call :PROGRESO 68 "Limpiando Delivery Optimization..."
call :LIMPIAR_DELIVERY

call :PROGRESO 74 "Limpiando memory dumps y logs antiguos..."
call :LIMPIAR_DUMPS

call :PROGRESO 80 "Limpiando cache de drivers graficos..."
call :LIMPIAR_GPU_CACHE

call :PROGRESO 86 "Vaciando papelera de reciclaje..."
call :LIMPIAR_PAPELERA

call :PROGRESO 92 "Limpiando registros de eventos..."
call :LIMPIAR_EVENTOS

call :PROGRESO 97 "Optimizando SSD (TRIM)..."
call :OPTIMIZAR_SSD

call :PROGRESO 100 "Finalizando..."
call :MEDIR_ESPACIO_DESPUES
call :MOSTRAR_RESUMEN "LIMPIEZA COMPLETA"
call :LOG "=== FIN LIMPIEZA COMPLETA ==="

if "!SILENT_MODE!"=="1" goto FIN_SCRIPT
goto PREGUNTAR_SHADER

:: ===================================================================
::  LIMPIEZA PERSONALIZADA
:: ===================================================================
:LIMPIEZA_PERSONALIZADA
cls
call :MOSTRAR_HEADER
echo.
echo  -------------------------------------------------------------------
echo                    LIMPIEZA PERSONALIZADA
echo  -------------------------------------------------------------------
echo   Responde Y/N a cada opcion:
echo.

set "OPT_WTEMP=N"
set "OPT_UTEMP=N"
set "OPT_PREF=N"
set "OPT_RECENT=N"
set "OPT_PRINT=N"
set "OPT_DNS=N"
set "OPT_WUP=N"
set "OPT_THUMB=N"
set "OPT_ICON=N"
set "OPT_STORE=N"
set "OPT_DELIV=N"
set "OPT_DUMPS=N"
set "OPT_FONT=N"
set "OPT_RECYCLE=N"
set "OPT_EVENTOS=N"
set "OPT_GPU=N"
set "OPT_DISCORD=N"
set "OPT_SSD=N"

set /p OPT_WTEMP="   [1] Windows Temp? (Y/N): "
set /p OPT_UTEMP="   [2] Temp de usuario? (Y/N): "
set /p OPT_PREF="   [3] Prefetch? (Y/N): "
set /p OPT_RECENT="   [4] Archivos recientes? (Y/N): "
set /p OPT_PRINT="   [5] Cola de impresion? (Y/N): "
set /p OPT_DNS="   [6] Cache DNS? (Y/N): "
set /p OPT_WUP="   [7] Cache Windows Update? (Y/N): "
set /p OPT_THUMB="   [8] Miniaturas (thumbnails)? (Y/N): "
set /p OPT_ICON="   [9] Icon cache? (Y/N): "
set /p OPT_STORE="   [10] Cache Microsoft Store? (Y/N): "
set /p OPT_DELIV="   [11] Delivery Optimization? (Y/N): "
set /p OPT_DUMPS="   [12] Memory dumps / CBS logs antiguos? (Y/N): "
set /p OPT_FONT="   [13] Cache de fuentes? (Y/N): "
set /p OPT_RECYCLE="   [14] Vaciar papelera de reciclaje? (Y/N): "
set /p OPT_EVENTOS="   [15] Registros de eventos (CUIDADO)? (Y/N): "
set /p OPT_GPU="   [16] Cache drivers graficos (NVIDIA/AMD/Intel)? (Y/N): "
set /p OPT_DISCORD="   [17] Cache de Discord (cerrara Discord)? (Y/N): "
set /p OPT_SSD="   [18] Optimizar SSD (TRIM al final)? (Y/N): "

call :MEDIR_ESPACIO_ANTES
call :LOG "=== INICIO LIMPIEZA PERSONALIZADA ==="
cls
call :MOSTRAR_HEADER
echo.
echo   Ejecutando tareas seleccionadas...
echo.

if /i "!OPT_WTEMP!"=="Y" ( echo   -^> Windows Temp & call :LIMPIAR_WINDOWS_TEMP )
if /i "!OPT_UTEMP!"=="Y" ( echo   -^> Temp de usuario & call :LIMPIAR_USER_TEMP )
if /i "!OPT_PREF!"=="Y" ( echo   -^> Prefetch & call :LIMPIAR_PREFETCH )
if /i "!OPT_RECENT!"=="Y" ( echo   -^> Recientes & call :LIMPIAR_RECENT )
if /i "!OPT_PRINT!"=="Y" ( echo   -^> Cola de impresion & call :LIMPIAR_COLA_IMPRESION )
if /i "!OPT_DNS!"=="Y" ( echo   -^> Cache DNS & call :LIMPIAR_DNS )
if /i "!OPT_WUP!"=="Y" ( echo   -^> Windows Update & call :LIMPIAR_WUPDATE )
if /i "!OPT_THUMB!"=="Y" ( echo   -^> Miniaturas & call :LIMPIAR_THUMBNAILS )
if /i "!OPT_ICON!"=="Y" ( echo   -^> Icon cache & call :LIMPIAR_ICON_CACHE )
if /i "!OPT_STORE!"=="Y" ( echo   -^> Microsoft Store & call :LIMPIAR_STORE )
if /i "!OPT_DELIV!"=="Y" ( echo   -^> Delivery Optimization & call :LIMPIAR_DELIVERY )
if /i "!OPT_DUMPS!"=="Y" ( echo   -^> Memory dumps & call :LIMPIAR_DUMPS )
if /i "!OPT_FONT!"=="Y" ( echo   -^> Cache de fuentes & call :LIMPIAR_FONTS )
if /i "!OPT_RECYCLE!"=="Y" ( echo   -^> Papelera & call :LIMPIAR_PAPELERA )
if /i "!OPT_EVENTOS!"=="Y" ( echo   -^> Eventos & call :LIMPIAR_EVENTOS )
if /i "!OPT_GPU!"=="Y" ( echo   -^> Cache drivers graficos & call :LIMPIAR_GPU_CACHE )
if /i "!OPT_DISCORD!"=="Y" ( echo   -^> Cache Discord & call :LIMPIAR_DISCORD )
if /i "!OPT_SSD!"=="Y" ( echo   -^> Optimizando SSD & call :OPTIMIZAR_SSD )

call :MEDIR_ESPACIO_DESPUES
call :MOSTRAR_RESUMEN "LIMPIEZA PERSONALIZADA"
call :LOG "=== FIN LIMPIEZA PERSONALIZADA ==="
goto PREGUNTAR_SHADER

:: ===================================================================
::  SUBRUTINAS DE LIMPIEZA
:: ===================================================================

:LIMPIAR_WINDOWS_TEMP
:: Usar robocopy /MIR es muchisimo mas rapido que del /s en carpetas grandes
robocopy "%EMPTY_DIR%" "C:\Windows\Temp" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Limpiada: C:\Windows\Temp"
exit /b

:LIMPIAR_USER_TEMP
robocopy "%EMPTY_DIR%" "%TEMP%" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
:: Volver a crear EMPTY_DIR por si se borro al estar dentro de TEMP
if not exist "%EMPTY_DIR%" mkdir "%EMPTY_DIR%" >nul 2>&1
call :LOG "Limpiada: %TEMP%"
exit /b

:LIMPIAR_PREFETCH
del /f /q C:\Windows\Prefetch\*.* >nul 2>&1
call :LOG "Limpiada: C:\Windows\Prefetch"
exit /b

:LIMPIAR_RECENT
del /f /q "%APPDATA%\Microsoft\Windows\Recent\*.*" >nul 2>&1
del /f /q "%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations\*.*" >nul 2>&1
del /f /q "%APPDATA%\Microsoft\Windows\Recent\CustomDestinations\*.*" >nul 2>&1
call :LOG "Limpiada: Recent"
exit /b

:LIMPIAR_COLA_IMPRESION
net stop spooler >nul 2>&1
del /f /q C:\Windows\System32\spool\PRINTERS\*.* >nul 2>&1
net start spooler >nul 2>&1
call :LOG "Limpiada: cola de impresion"
exit /b

:LIMPIAR_DNS
ipconfig /flushdns >nul 2>&1
call :LOG "Cache DNS vaciada"
exit /b

:LIMPIAR_WUPDATE
:: Detener servicios, limpiar y reiniciar
net stop wuauserv >nul 2>&1
net stop bits >nul 2>&1
net stop cryptsvc >nul 2>&1
robocopy "%EMPTY_DIR%" "C:\Windows\SoftwareDistribution\Download" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
net start wuauserv >nul 2>&1
net start bits >nul 2>&1
net start cryptsvc >nul 2>&1
call :LOG "Limpiada: cache Windows Update"
exit /b

:LIMPIAR_THUMBNAILS
:: Hay que cerrar explorer para liberar el archivo thumbcache
taskkill /f /im explorer.exe >nul 2>&1
del /f /s /q /a "%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db" >nul 2>&1
start explorer.exe
call :LOG "Limpiadas: miniaturas (thumbcache)"
exit /b

:LIMPIAR_ICON_CACHE
del /f /q "%LOCALAPPDATA%\IconCache.db" >nul 2>&1
del /f /s /q /a "%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache_*.db" >nul 2>&1
call :LOG "Limpiado: icon cache"
exit /b

:LIMPIAR_STORE
:: wsreset abre una ventana del Store, lo lanzamos en background
start "" /b wsreset.exe >nul 2>&1
timeout /t 2 /nobreak >nul
call :LOG "Cache Microsoft Store reiniciada"
exit /b

:LIMPIAR_DELIVERY
robocopy "%EMPTY_DIR%" "C:\Windows\SoftwareDistribution\DeliveryOptimization" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Limpiada: Delivery Optimization"
exit /b

:LIMPIAR_DUMPS
:: Memory dumps de Windows
del /f /q C:\Windows\Minidump\*.* >nul 2>&1
del /f /q C:\Windows\MEMORY.DMP >nul 2>&1
:: CBS logs grandes (se regeneran)
del /f /q C:\Windows\Logs\CBS\*.log >nul 2>&1
:: WER (Windows Error Reporting) reports
robocopy "%EMPTY_DIR%" "%ProgramData%\Microsoft\Windows\WER\ReportArchive" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%ProgramData%\Microsoft\Windows\WER\ReportQueue" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Limpiados: memory dumps y CBS logs"
exit /b

:LIMPIAR_FONTS
del /f /q "%LOCALAPPDATA%\FontCache*.dat" >nul 2>&1
del /f /q "%WINDIR%\System32\FNTCACHE.DAT" >nul 2>&1
call :LOG "Limpiada: cache de fuentes"
exit /b

:LIMPIAR_PAPELERA
:: Usar PowerShell para vaciar la papelera de forma fiable en todas las versiones
powershell -NoProfile -Command "Clear-RecycleBin -Force -ErrorAction SilentlyContinue" >nul 2>&1
call :LOG "Papelera de reciclaje vaciada"
exit /b

:LIMPIAR_EVENTOS
echo   Borrando registros de eventos (puede tardar)...
:: 1) Vaciar todos los canales de eventos via wevtutil
for /F "tokens=*" %%G in ('wevtutil.exe el 2^>nul') DO (
    wevtutil.exe cl "%%G" >nul 2>&1
)
:: 2) Borrar archivos .evtx fisicos por si quedan restos
del /f /q /s "C:\Windows\System32\winevt\Logs\*.evtx" >nul 2>&1
:: 3) Limpiar carpeta C:\Windows\Logs (CBS, DISM, etc)
del /f /q /s "C:\Windows\Logs\*.log" >nul 2>&1
del /f /q /s "C:\Windows\Logs\*.etl" >nul 2>&1
:: 4) Panther (logs de instalacion/upgrade) y DISM
del /f /q /s "C:\Windows\Panther\*.log" >nul 2>&1
del /f /q "C:\Windows\Logs\DISM\dism.log" >nul 2>&1
:: 5) WindowsUpdate.log antiguo
del /f /q "C:\Windows\WindowsUpdate.log" >nul 2>&1
call :LOG "Registros de eventos y logs de Windows borrados"
exit /b

:LIMPIAR_GPU_CACHE
:: Limpia cache de drivers graficos (NVIDIA, AMD, Intel)
:: Complementa al shadercache de Steam: el driver tambien cachea shaders compilados.
:: --- NVIDIA ---
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\NVIDIA\DXCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\NVIDIA\GLCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%APPDATA%\NVIDIA\ComputeCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\NVIDIA Corporation\NV_Cache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
:: --- AMD ---
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\AMD\DxCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\AMD\GLCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
:: --- Intel ---
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\Intel\ShaderCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Cache de drivers graficos (NVIDIA/AMD/Intel) limpiada"
exit /b

:LIMPIAR_DISCORD
:: Discord acumula MUCHO en cache. Hay que cerrarlo primero o falla.
tasklist /FI "IMAGENAME eq Discord.exe" 2>nul | find /I "Discord.exe" >nul
if !errorlevel! EQU 0 (
    echo   [INFO] Cerrando Discord para limpiar su cache...
    taskkill /f /im Discord.exe >nul 2>&1
    timeout /t 2 /nobreak >nul
)
robocopy "%EMPTY_DIR%" "%APPDATA%\discord\Cache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%APPDATA%\discord\Code Cache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%APPDATA%\discord\GPUCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
:: Discord PTB y Canary si existen
robocopy "%EMPTY_DIR%" "%APPDATA%\discordptb\Cache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%APPDATA%\discordcanary\Cache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Cache de Discord limpiada"
exit /b

:OPTIMIZAR_SSD
:: Despues de borrar muchos archivos, ejecutar TRIM le dice al SSD que bloques
:: estan libres realmente, mejorando rendimiento y vida util.
:: /L hace TRIM en SSDs, /O optimiza (desfragmenta HDDs, consolida SSDs)
echo   Optimizando unidad C: (TRIM/desfragmentacion)...
defrag C: /L >nul 2>&1
call :LOG "TRIM/optimizacion ejecutado en C:"
exit /b

:: ===================================================================
::  SUBRUTINA: PROGRESO
:: ===================================================================
:PROGRESO
:: %1 = porcentaje (0-100), %2 = mensaje
set "PORC=%~1"
set "MSG=%~2"
cls
call :MOSTRAR_HEADER
echo.
echo  -------------------------------------------------------------------
echo.
set "BARRA="
set /a BLOQUES=PORC/10
for /L %%i in (1,1,!BLOQUES!) do set "BARRA=!BARRA!#"
for /L %%i in (!BLOQUES!,1,9) do set "BARRA=!BARRA!."
echo    [!BARRA!] !PORC!%% - !MSG!
echo.
echo  -------------------------------------------------------------------
exit /b

:: ===================================================================
::  SUBRUTINA: MOSTRAR HEADER
:: ===================================================================
:MOSTRAR_HEADER
echo.
echo  ===================================================================
echo.
echo    RRRRR   N   N  X   X      CACHE CLEANER PRO v%VERSION_SCRIPT%
echo    R    R  NN  N   X X
echo    RRRRR   N N N    X        Sistema:  Windows %WIN_VERSION%
echo    R   R   N  NN   X X       Usuario:  %username%
echo    R    R  N   N  X   X      Fecha:    %date% - %time:~0,5%
echo.
echo  ===================================================================
exit /b

:: ===================================================================
::  SUBRUTINA: MEDIR ESPACIO LIBRE EN C:
:: ===================================================================
:MEDIR_ESPACIO_ANTES
for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "(Get-PSDrive C).Free"`) do set "ESPACIO_ANTES=%%A"
exit /b

:MEDIR_ESPACIO_DESPUES
for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "(Get-PSDrive C).Free"`) do set "ESPACIO_DESPUES=%%A"
:: Calcular diferencia en MB usando PowerShell (batch no maneja bien numeros grandes)
for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "[math]::Round((!ESPACIO_DESPUES! - !ESPACIO_ANTES!)/1MB, 2)"`) do set "ESPACIO_LIBERADO=%%A"
exit /b

:: ===================================================================
::  SUBRUTINA: MOSTRAR RESUMEN
:: ===================================================================
:MOSTRAR_RESUMEN
cls
color 0A
call :MOSTRAR_HEADER
echo.
echo  -------------------------------------------------------------------
echo                  %~1 COMPLETADA EXITOSAMENTE
echo  -------------------------------------------------------------------
echo.
echo    [##########] 100%%
echo.
echo    Espacio liberado en C: !ESPACIO_LIBERADO! MB
echo.
echo    Log guardado en: %LOG_FILE%
echo.
echo  -------------------------------------------------------------------
echo.
timeout /t 5 /nobreak >nul
exit /b

:: ===================================================================
::  SUBRUTINA: LOG
:: ===================================================================
:LOG
echo [%date% %time%] %~1 >> "%LOG_FILE%"
exit /b

:: ===================================================================
::  VER LOG
:: ===================================================================
:VER_LOG
cls
call :MOSTRAR_HEADER
echo.
if exist "%LOG_FILE%" (
    echo  -------------------------------------------------------------------
    echo                    ULTIMAS LINEAS DEL LOG
    echo  -------------------------------------------------------------------
    powershell -NoProfile -Command "Get-Content '%LOG_FILE%' -Tail 30"
) else (
    echo   No existe ningun log todavia.
)
echo.
pause
goto MENU_PRINCIPAL

:: ===================================================================
::  SHADERCACHE (Steam)
:: ===================================================================
:PREGUNTAR_SHADER
cls
color 0E
call :MOSTRAR_HEADER
echo.
echo  -------------------------------------------------------------------
echo                       MODULO SHADERCACHE
echo  -------------------------------------------------------------------
echo.
set /p BORRAR_SHADER="   Deseas borrar la carpeta shadercache? (Y/N): "
if /i "!BORRAR_SHADER!" NEQ "Y" goto PREGUNTAR_RATON
goto SHADERCACHE_CORE

:SHADERCACHE_DIRECTO
cls
color 0E
call :MOSTRAR_HEADER

:SHADERCACHE_CORE
:: Verificar si Steam esta corriendo (puede bloquear el borrado)
tasklist /FI "IMAGENAME eq steam.exe" 2>nul | find /I "steam.exe" >nul
if !errorlevel! EQU 0 (
    echo.
    echo   [AVISO] Steam esta corriendo. Es recomendable cerrarlo.
    set /p CONTINUAR="   Continuar de todos modos? (Y/N): "
    if /i "!CONTINUAR!" NEQ "Y" goto PREGUNTAR_RATON
)

set "CONFIG_FILE=%SCRIPT_DIR%shadercache.txt"

if exist "!CONFIG_FILE!" (
    set /p SHADER_PATH=<"!CONFIG_FILE!"
    echo.
    echo    Ruta guardada: !SHADER_PATH!
    set /p USAR_GUARDADA="   Usar esta ruta? (Y/N): "
    if /i "!USAR_GUARDADA!"=="Y" goto BORRAR_SHADER
)

echo.
echo    Ruta por defecto: C:\Program Files (x86)\Steam\steamapps\shadercache\730
echo    (Deja en blanco para usar la ruta por defecto)
echo.
set /p SHADER_PATH="   Ruta: "

if "!SHADER_PATH!"=="" set "SHADER_PATH=C:\Program Files (x86)\Steam\steamapps\shadercache\730"
set "SHADER_PATH=!SHADER_PATH:"=!"
echo !SHADER_PATH!>"!CONFIG_FILE!"
echo   [OK] Ruta guardada para la proxima vez.

:BORRAR_SHADER
echo.
if not exist "!SHADER_PATH!" (
    color 0C
    echo   [ERROR] La ruta no existe: !SHADER_PATH!
    call :LOG "ERROR shadercache: ruta no existe !SHADER_PATH!"
    timeout /t 3 /nobreak >nul
    goto PREGUNTAR_RATON
)

echo    Eliminando shadercache...
rd /s /q "!SHADER_PATH!" >nul 2>&1

if not exist "!SHADER_PATH!" (
    color 0A
    echo   [OK] Carpeta shadercache eliminada correctamente.
    call :LOG "Shadercache eliminada: !SHADER_PATH!"
) else (
    color 0C
    echo   [ERROR] No se pudo eliminar. Verifica permisos o que Steam este cerrado.
    call :LOG "ERROR borrando shadercache: !SHADER_PATH!"
)
timeout /t 2 /nobreak >nul
if "!SILENT_MODE!"=="1" goto FIN_SCRIPT
goto PREGUNTAR_RATON

:: ===================================================================
::  CONFIG RATON
:: ===================================================================
:PREGUNTAR_RATON
cls
color 0E
call :MOSTRAR_HEADER
echo.
echo  -------------------------------------------------------------------
echo                       MODULO CONFIG RATON
echo  -------------------------------------------------------------------
echo.
set /p APLICAR_RATON="   Deseas aplicar la configuracion del raton? (Y/N): "
if /i "!APLICAR_RATON!" NEQ "Y" goto FIN_SCRIPT
goto RATON_CORE

:RATON_DIRECTO
cls
color 0E
call :MOSTRAR_HEADER

:RATON_CORE
set "RATON_CONFIG=%SCRIPT_DIR%raton.reg"

if not exist "!RATON_CONFIG!" (
    echo.
    echo   No se encontro raton.reg en: !RATON_CONFIG!
    set /p RATON_CONFIG="   Ingresa la ruta completa del archivo raton.reg: "
)

if not exist "!RATON_CONFIG!" (
    color 0C
    echo.
    echo   [ERROR] Archivo no encontrado.
    call :LOG "ERROR raton.reg no encontrado"
    timeout /t 3 /nobreak >nul
    goto FIN_SCRIPT
)

echo.
echo   Aplicando configuracion del raton...
reg import "!RATON_CONFIG!" >nul 2>&1

if !errorlevel! EQU 0 (
    color 0A
    echo   [OK] Configuracion del raton aplicada correctamente.
    call :LOG "Config raton aplicada: !RATON_CONFIG!"
) else (
    color 0C
    echo   [ERROR] No se pudo importar la configuracion.
    call :LOG "ERROR importando raton.reg"
)
timeout /t 2 /nobreak >nul

:: ===================================================================
::  FIN
:: ===================================================================
:FIN_SCRIPT
:: Limpiar carpeta auxiliar
if exist "%EMPTY_DIR%" rd /s /q "%EMPTY_DIR%" >nul 2>&1

cls
color 0A
call :MOSTRAR_HEADER
echo.
echo  -------------------------------------------------------------------
echo                         FIN DEL SCRIPT
echo  -------------------------------------------------------------------
echo.
echo   Gracias por usar RNX Cache Cleaner Pro v%VERSION_SCRIPT%
echo.
echo   Log de operaciones: %LOG_FILE%
echo.
echo   Presiona cualquier tecla para salir...
pause >nul
exit /B
