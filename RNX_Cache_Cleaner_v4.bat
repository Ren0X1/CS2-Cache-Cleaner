@echo off
setlocal enabledelayedexpansion

:: ===================================================================
::  RNX CACHE CLEANER PRO v4.0
::  Limpieza y optimizacion del sistema Windows
::  Interfaz cyberpunk/neon con navegacion por flechas
:: ===================================================================

:: ----- Auto-elevacion a administrador -----
:: Pasamos %* (todos los argumentos) al proceso elevado para no perder
:: los modos silenciosos (/rapida, /completa, etc.) al relanzar con UAC.
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "%*", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B
)

:: ----- Forzar codificacion UTF-8 para los caracteres de las cajas -----
chcp 65001 >nul 2>&1

:: ----- Habilitar secuencias de escape ANSI (colores neon reales) -----
:: En Windows 10/11 esto activa el soporte de color VT100 en la consola.
reg add HKCU\Console /v VirtualTerminalLevel /t REG_DWORD /d 1 /f >nul 2>&1
:: Caracter ESC (0x1B) para las secuencias ANSI
for /f %%a in ('echo prompt $E^| cmd') do set "ESC=%%a"

:: ----- Paleta de colores neon (ANSI 24-bit / 256) -----
set "C_CYAN=%ESC%[38;5;51m"
set "C_MAGENTA=%ESC%[38;5;201m"
set "C_YELLOW=%ESC%[38;5;226m"
set "C_GREEN=%ESC%[38;5;46m"
set "C_RED=%ESC%[38;5;196m"
set "C_BLUE=%ESC%[38;5;75m"
set "C_GREY=%ESC%[38;5;240m"
set "C_WHITE=%ESC%[38;5;255m"
set "C_RESET=%ESC%[0m"
set "C_BOLD=%ESC%[1m"
:: Fondo para item resaltado en el menu (magenta)
set "HL=%ESC%[1;30;48;5;201m"
set "HL_CYAN=%ESC%[1;30;48;5;51m"

:: ----- Variables globales -----
set "VERSION_SCRIPT=4.3"
set "SCRIPT_DIR=%~dp0"
set "LOG_FILE=%SCRIPT_DIR%RNX_Cleaner.log"
set "EMPTY_DIR=%TEMP%\rnx_empty_dir"
for /f "tokens=4-5 delims=. " %%i in ('ver') do set WIN_VERSION=%%i.%%j

:: Detectar marca de GPU (PowerShell es mas fiable que wmic, que esta deprecado)
set "GPU_NVIDIA=0"
set "GPU_TXT=Generica"
for /f "usebackq delims=" %%G in (`powershell -NoProfile -Command "(Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name) -join ' '"`) do set "GPU_RAW=%%G"
echo !GPU_RAW! | find /i "NVIDIA" >nul && ( set "GPU_NVIDIA=1" & set "GPU_TXT=NVIDIA" )
echo !GPU_RAW! | find /i "Radeon" >nul && set "GPU_TXT=AMD Radeon"
echo !GPU_RAW! | find /i "AMD"    >nul && if "!GPU_NVIDIA!"=="0" set "GPU_TXT=AMD"
echo !GPU_RAW! | find /i "Intel"  >nul && if "!GPU_NVIDIA!"=="0" set "GPU_TXT=Intel"

:: Carpeta vacia auxiliar para robocopy /MIR
if not exist "%EMPTY_DIR%" mkdir "%EMPTY_DIR%" >nul 2>&1

set "ESPACIO_ANTES=0"
set "ESPACIO_DESPUES=0"
set "ESPACIO_LIBERADO=0"
set "MODO_TODO=0"

:: Reiniciar el log: solo se conserva la ejecucion actual (sobrescribe).
echo === RNX Cache Cleaner v%VERSION_SCRIPT% - Ejecucion %date% %time:~0,8% ===> "%LOG_FILE%"

Title RNX Cache Cleaner Pro v%VERSION_SCRIPT%

:: ===================================================================
::  MODO ARGUMENTOS (ejecucion silenciosa sin menu)
:: ===================================================================
set "SILENT_MODE=0"
if /i "%~1"=="/rapida"   ( set "SILENT_MODE=1" & goto LIMPIEZA_RAPIDA )
if /i "%~1"=="/completa" ( set "SILENT_MODE=1" & goto LIMPIEZA_COMPLETA )
if /i "%~1"=="/todo"     ( set "SILENT_MODE=1" & set "MODO_TODO=1" & goto LIMPIEZA_COMPLETA )
if /i "%~1"=="/shader"   ( set "SILENT_MODE=1" & goto SHADERCACHE_DIRECTO )
if /i "%~1"=="/raton"    ( set "SILENT_MODE=1" & goto RATON_DIRECTO )
if /i "%~1"=="/nvidia"   ( set "SILENT_MODE=1" & goto NVIDIA_DIRECTO )

:: ===================================================================
::  MENU PRINCIPAL (navegacion por flechas)
::  La seleccion se guarda en MENU_SEL. Las opciones estan en
::  MENU_1..MENU_N. Usamos choice para capturar flechas via scancode.
:: ===================================================================
:MENU_PRINCIPAL
set "MENU_SEL=1"
:: Numero de opciones del menu (8 si hay NVIDIA, 7 si no)
if "%GPU_NVIDIA%"=="1" ( set "MENU_MAX=8" ) else ( set "MENU_MAX=7" )

:MENU_LOOP
call :DIBUJAR_MENU
:: Leer una tecla. choice no captura flechas, asi que usamos un helper
:: en PowerShell que devuelve: UP / DOWN / ENTER / un digito.
for /f %%K in ('powershell -NoProfile -Command "$k=$host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown').VirtualKeyCode; switch($k){38{'UP'}40{'DOWN'}13{'ENTER'}48{'0'}49{'1'}50{'2'}51{'3'}52{'4'}53{'5'}54{'6'}55{'7'}56{'8'}default{'X'}}"') do set "KEY=%%K"

if "!KEY!"=="UP" (
    set /a MENU_SEL-=1
    if !MENU_SEL! LSS 1 set "MENU_SEL=!MENU_MAX!"
    goto MENU_LOOP
)
if "!KEY!"=="DOWN" (
    set /a MENU_SEL+=1
    if !MENU_SEL! GTR !MENU_MAX! set "MENU_SEL=1"
    goto MENU_LOOP
)
:: Atajos numericos directos (solo si KEY es un digito valido del menu)
if "!KEY!"=="0" ( set "MENU_SEL=0" & goto MENU_EJECUTAR )
for %%N in (1 2 3 4 5 6 7 8) do (
    if "!KEY!"=="%%N" (
        if %%N LEQ !MENU_MAX! ( set "MENU_SEL=%%N" & goto MENU_EJECUTAR )
    )
)
if "!KEY!"=="ENTER" goto MENU_EJECUTAR
goto MENU_LOOP

:MENU_EJECUTAR
if "!MENU_SEL!"=="1" goto LIMPIEZA_RAPIDA
if "!MENU_SEL!"=="2" goto LIMPIEZA_COMPLETA
if "!MENU_SEL!"=="3" goto LIMPIEZA_PERSONALIZADA
if "!MENU_SEL!"=="4" goto SHADERCACHE_DIRECTO
if "!MENU_SEL!"=="5" goto RATON_DIRECTO
if "!MENU_SEL!"=="6" goto VER_LOG
if "!MENU_SEL!"=="7" (
    if "%GPU_NVIDIA%"=="1" ( goto NVIDIA_DIRECTO ) else ( goto FIN_SCRIPT )
)
if "!MENU_SEL!"=="8" goto FIN_SCRIPT
if "!MENU_SEL!"=="0" goto FIN_SCRIPT
goto MENU_LOOP

:: ===================================================================
::  DIBUJAR MENU (resalta la opcion MENU_SEL)
:: ===================================================================
:DIBUJAR_MENU
cls
call :BANNER
echo.
echo  %C_GREY%   Usa %C_CYAN%FLECHAS%C_GREY% para moverte  -  %C_CYAN%ENTER%C_GREY% para elegir  -  o pulsa el %C_CYAN%numero%C_RESET%
echo.
call :ITEM 1 "  LIMPIEZA RAPIDA      " "Temp, GPU, DNS, Prefetch"
call :ITEM 2 "  LIMPIEZA COMPLETA    " "Todo + TRIM SSD"
call :ITEM 3 "  LIMPIEZA PERSONALIZADA" "Elige que limpiar"
call :ITEM 4 "  SHADERCACHE          " "Solo shaders de CS2/Steam"
call :ITEM 5 "  CONFIG RATON         " "Importar raton.reg"
call :ITEM 6 "  VER LOG              " "Historial de operaciones"
if "%GPU_NVIDIA%"=="1" (
    call :ITEM 7 "  PERFIL NVIDIA CS2    " "Importar .nip optimizado"
    call :ITEM 8 "  SALIR                " ""
) else (
    call :ITEM 7 "  SALIR                " ""
)
echo.
call :LINEA_INFERIOR
exit /b

:: Dibuja una linea de menu. %1=indice  %2=texto  %3=descripcion
:ITEM
set "IDX=%~1"
set "TXT=%~2"
set "DESC=%~3"
if "!MENU_SEL!"=="!IDX!" (
    echo   %HL%  [!IDX!]!TXT! %C_RESET%  %C_YELLOW%!DESC!%C_RESET%
) else (
    echo   %C_GREY%    %C_WHITE%[!IDX!]!TXT!%C_RESET%  %C_GREY%!DESC!%C_RESET%
)
exit /b

:: ===================================================================
::  BANNER cyberpunk (ASCII puro - funciona en cualquier codificacion)
:: ===================================================================
:BANNER
echo %C_MAGENTA%  ===============================================================%C_RESET%
echo %C_MAGENTA%   ^|%C_CYAN%  ____  _   _ __  __ %C_RESET%
echo %C_MAGENTA%   ^|%C_CYAN% ^|  _ \^| \ ^| ^|\ \/ / %C_RESET%   %C_YELLOW%CACHE CLEANER PRO%C_RESET%
echo %C_MAGENTA%   ^|%C_CYAN% ^| ^|_^) ^|  \^| ^| \  /  %C_RESET%   %C_WHITE%v%VERSION_SCRIPT%   ::   RNX SYSTEMS%C_RESET%
echo %C_MAGENTA%   ^|%C_CYAN% ^|  _ ^<^| ^|\  ^| /  \  %C_RESET%
echo %C_MAGENTA%   ^|%C_CYAN% ^|_^| \_\_^| \_^|/_/\_\ %C_RESET%
echo %C_MAGENTA%  ===============================================================%C_RESET%
echo    %C_GREY%SYS %C_WHITE%Win %WIN_VERSION%%C_GREY%   USER %C_WHITE%%username%%C_GREY%   GPU %C_GREEN%%GPU_TXT%%C_RESET%
echo %C_MAGENTA%  ===============================================================%C_RESET%
exit /b

:LINEA_INFERIOR
echo %C_GREY%  ===============================================================%C_RESET%
exit /b

:: ===================================================================
::  ANIMACION DE CARGA RAPIDA (~0.5s) - barra neon
:: ===================================================================
:LOADING
:: %1 = mensaje
set "LMSG=%~1"
<nul set /p "=%C_CYAN%  %LMSG% %C_RESET%"
for %%b in (1 2 3 4 5 6 7 8 9 0) do (
    <nul set /p "=%C_MAGENTA%#%C_RESET%"
    ping -n 1 -w 50 127.0.0.1 >nul
)
echo  %C_GREEN%OK%C_RESET%
exit /b

:: ===================================================================
::  LIMPIEZA RAPIDA
:: ===================================================================
:LIMPIEZA_RAPIDA
cls
call :BANNER
echo.
echo  %C_YELLOW%  [>] LIMPIEZA RAPIDA%C_RESET%
echo.
call :MEDIR_ESPACIO_ANTES
call :LOG "=== INICIO LIMPIEZA RAPIDA ==="
call :LOADING "Windows Temp.............."
call :LIMPIAR_WINDOWS_TEMP
call :LOADING "Temp de usuario..........."
call :LIMPIAR_USER_TEMP
call :LOADING "Prefetch.................."
call :LIMPIAR_PREFETCH
call :LOADING "Archivos recientes........"
call :LIMPIAR_RECENT
call :LOADING "Cache drivers graficos...."
call :LIMPIAR_GPU_CACHE
call :LOADING "Cache DNS................."
call :LIMPIAR_DNS
call :LOADING "Registros de eventos......"
call :LIMPIAR_EVENTOS
call :MEDIR_ESPACIO_DESPUES
call :MOSTRAR_RESUMEN "LIMPIEZA RAPIDA"
call :LOG "=== FIN LIMPIEZA RAPIDA ==="
if "!SILENT_MODE!"=="1" goto FIN_SCRIPT
goto PREGUNTAR_SHADER

:: ===================================================================
::  LIMPIEZA COMPLETA
:: ===================================================================
:LIMPIEZA_COMPLETA
cls
call :BANNER
echo.
echo  %C_YELLOW%  [>] LIMPIEZA COMPLETA%C_RESET%
echo.
call :MEDIR_ESPACIO_ANTES
call :LOG "=== INICIO LIMPIEZA COMPLETA ==="
call :LOADING "Windows Temp.............."
call :LIMPIAR_WINDOWS_TEMP
call :LOADING "Temp de usuario..........."
call :LIMPIAR_USER_TEMP
call :LOADING "Prefetch.................."
call :LIMPIAR_PREFETCH
call :LOADING "Archivos recientes........"
call :LIMPIAR_RECENT
call :LOADING "Cola de impresion........."
call :LIMPIAR_COLA_IMPRESION
call :LOADING "Cache DNS................."
call :LIMPIAR_DNS
call :LOADING "Cache Windows Update......"
call :LIMPIAR_WUPDATE
call :LOADING "Miniaturas................"
call :LIMPIAR_THUMBNAILS
call :LOADING "Icon cache................"
call :LIMPIAR_ICON_CACHE
call :LOADING "Microsoft Store..........."
call :LIMPIAR_STORE
call :LOADING "Delivery Optimization....."
call :LIMPIAR_DELIVERY
call :LOADING "Memory dumps y logs......."
call :LIMPIAR_DUMPS
call :LOADING "Cache drivers graficos...."
call :LIMPIAR_GPU_CACHE
call :LOADING "Cache de fuentes.........."
call :LIMPIAR_FONTS
call :LOADING "Papelera de reciclaje....."
call :LIMPIAR_PAPELERA
call :LOADING "Registros de eventos......"
call :LIMPIAR_EVENTOS
call :LOADING "Optimizando SSD (TRIM)...."
call :OPTIMIZAR_SSD
:: Aplicar perfil NVIDIA CS2 si hay GPU NVIDIA + exe + nip (desatendido, omite si falta)
if "%GPU_NVIDIA%"=="1" (
    call :LOADING "Perfil NVIDIA CS2........."
    call :NVIDIA_AUTO
)
call :MEDIR_ESPACIO_DESPUES
call :MOSTRAR_RESUMEN "LIMPIEZA COMPLETA"
call :LOG "=== FIN LIMPIEZA COMPLETA ==="
:: En modo /todo seguimos con shadercache + raton automaticos
if "!MODO_TODO!"=="1" goto TODO_MODULOS
if "!SILENT_MODE!"=="1" goto FIN_SCRIPT
goto PREGUNTAR_SHADER

:: ===================================================================
::  LIMPIEZA PERSONALIZADA
:: ===================================================================
:LIMPIEZA_PERSONALIZADA
cls
call :BANNER
echo.
echo  %C_YELLOW%  [>] LIMPIEZA PERSONALIZADA%C_RESET%  %C_GREY%^(responde Y/N a cada una^)%C_RESET%
echo.
for %%V in (WTEMP UTEMP PREF RECENT PRINT DNS WUP THUMB ICON STORE DELIV DUMPS FONT RECYCLE EVENTOS GPU DISCORD SSD) do set "OPT_%%V=N"

set /p OPT_WTEMP="   [01] Windows Temp? (Y/N): "
set /p OPT_UTEMP="   [02] Temp de usuario? (Y/N): "
set /p OPT_PREF="   [03] Prefetch? (Y/N): "
set /p OPT_RECENT="   [04] Archivos recientes? (Y/N): "
set /p OPT_PRINT="   [05] Cola de impresion? (Y/N): "
set /p OPT_DNS="   [06] Cache DNS? (Y/N): "
set /p OPT_WUP="   [07] Cache Windows Update? (Y/N): "
set /p OPT_THUMB="   [08] Miniaturas? (Y/N): "
set /p OPT_ICON="   [09] Icon cache? (Y/N): "
set /p OPT_STORE="   [10] Cache Microsoft Store? (Y/N): "
set /p OPT_DELIV="   [11] Delivery Optimization? (Y/N): "
set /p OPT_DUMPS="   [12] Memory dumps / CBS logs? (Y/N): "
set /p OPT_FONT="   [13] Cache de fuentes? (Y/N): "
set /p OPT_RECYCLE="   [14] Vaciar papelera? (Y/N): "
set /p OPT_EVENTOS="   [15] Registros de eventos (CUIDADO)? (Y/N): "
set /p OPT_GPU="   [16] Cache drivers graficos? (Y/N): "
set /p OPT_DISCORD="   [17] Cache Discord (cierra Discord)? (Y/N): "
set /p OPT_SSD="   [18] Optimizar SSD (TRIM)? (Y/N): "

call :MEDIR_ESPACIO_ANTES
call :LOG "=== INICIO LIMPIEZA PERSONALIZADA ==="
cls
call :BANNER
echo.
echo  %C_YELLOW%  [>] Ejecutando seleccion...%C_RESET%
echo.
if /i "!OPT_WTEMP!"=="Y" ( call :LOADING "Windows Temp.............." & call :LIMPIAR_WINDOWS_TEMP )
if /i "!OPT_UTEMP!"=="Y" ( call :LOADING "Temp de usuario..........." & call :LIMPIAR_USER_TEMP )
if /i "!OPT_PREF!"=="Y" ( call :LOADING "Prefetch.................." & call :LIMPIAR_PREFETCH )
if /i "!OPT_RECENT!"=="Y" ( call :LOADING "Archivos recientes........" & call :LIMPIAR_RECENT )
if /i "!OPT_PRINT!"=="Y" ( call :LOADING "Cola de impresion........." & call :LIMPIAR_COLA_IMPRESION )
if /i "!OPT_DNS!"=="Y" ( call :LOADING "Cache DNS................." & call :LIMPIAR_DNS )
if /i "!OPT_WUP!"=="Y" ( call :LOADING "Windows Update............" & call :LIMPIAR_WUPDATE )
if /i "!OPT_THUMB!"=="Y" ( call :LOADING "Miniaturas................" & call :LIMPIAR_THUMBNAILS )
if /i "!OPT_ICON!"=="Y" ( call :LOADING "Icon cache................" & call :LIMPIAR_ICON_CACHE )
if /i "!OPT_STORE!"=="Y" ( call :LOADING "Microsoft Store..........." & call :LIMPIAR_STORE )
if /i "!OPT_DELIV!"=="Y" ( call :LOADING "Delivery Optimization....." & call :LIMPIAR_DELIVERY )
if /i "!OPT_DUMPS!"=="Y" ( call :LOADING "Memory dumps.............." & call :LIMPIAR_DUMPS )
if /i "!OPT_FONT!"=="Y" ( call :LOADING "Cache de fuentes.........." & call :LIMPIAR_FONTS )
if /i "!OPT_RECYCLE!"=="Y" ( call :LOADING "Papelera.................." & call :LIMPIAR_PAPELERA )
if /i "!OPT_EVENTOS!"=="Y" ( call :LOADING "Registros de eventos......" & call :LIMPIAR_EVENTOS )
if /i "!OPT_GPU!"=="Y" ( call :LOADING "Cache drivers graficos...." & call :LIMPIAR_GPU_CACHE )
if /i "!OPT_DISCORD!"=="Y" ( call :LOADING "Cache Discord............." & call :LIMPIAR_DISCORD )
if /i "!OPT_SSD!"=="Y" ( call :LOADING "Optimizando SSD..........." & call :OPTIMIZAR_SSD )
call :MEDIR_ESPACIO_DESPUES
call :MOSTRAR_RESUMEN "LIMPIEZA PERSONALIZADA"
call :LOG "=== FIN LIMPIEZA PERSONALIZADA ==="
goto PREGUNTAR_SHADER

:: ===================================================================
::  SUBRUTINAS DE LIMPIEZA
:: ===================================================================
:LIMPIAR_WINDOWS_TEMP
robocopy "%EMPTY_DIR%" "C:\Windows\Temp" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Limpiada: C:\Windows\Temp"
exit /b

:LIMPIAR_USER_TEMP
robocopy "%EMPTY_DIR%" "%TEMP%" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
if not exist "%EMPTY_DIR%" mkdir "%EMPTY_DIR%" >nul 2>&1
call :LOG "Limpiada: %TEMP%"
exit /b

:LIMPIAR_PREFETCH
del /f /q C:\Windows\Prefetch\*.* >nul 2>&1
call :LOG "Limpiada: Prefetch"
exit /b

:LIMPIAR_RECENT
:: Solo borramos los accesos recientes sueltos (caché que Windows regenera).
:: NO tocamos AutomaticDestinations ni CustomDestinations: ahi viven las
:: Jump Lists y los elementos ANCLADOS del panel del Explorador (Desktop,
:: Downloads, etc.). Borrarlos hacia que desaparecieran los anclados.
del /f /q "%APPDATA%\Microsoft\Windows\Recent\*.lnk" >nul 2>&1
call :LOG "Limpiada: Recent (anclados del Explorador preservados)"
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
taskkill /f /im explorer.exe >nul 2>&1
del /f /s /q /a "%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db" >nul 2>&1
start explorer.exe
call :LOG "Limpiadas: miniaturas"
exit /b

:LIMPIAR_ICON_CACHE
del /f /q "%LOCALAPPDATA%\IconCache.db" >nul 2>&1
del /f /s /q /a "%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache_*.db" >nul 2>&1
call :LOG "Limpiado: icon cache"
exit /b

:LIMPIAR_STORE
start "" /b wsreset.exe >nul 2>&1
ping -n 2 -w 500 127.0.0.1 >nul
call :LOG "Cache Microsoft Store reiniciada"
exit /b

:LIMPIAR_DELIVERY
robocopy "%EMPTY_DIR%" "C:\Windows\SoftwareDistribution\DeliveryOptimization" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Limpiada: Delivery Optimization"
exit /b

:LIMPIAR_DUMPS
del /f /q C:\Windows\Minidump\*.* >nul 2>&1
del /f /q C:\Windows\MEMORY.DMP >nul 2>&1
del /f /q C:\Windows\Logs\CBS\*.log >nul 2>&1
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
powershell -NoProfile -Command "Clear-RecycleBin -Force -ErrorAction SilentlyContinue" >nul 2>&1
call :LOG "Papelera de reciclaje vaciada"
exit /b

:LIMPIAR_EVENTOS
for /F "tokens=*" %%G in ('wevtutil.exe el 2^>nul') DO wevtutil.exe cl "%%G" >nul 2>&1
del /f /q /s "C:\Windows\System32\winevt\Logs\*.evtx" >nul 2>&1
del /f /q /s "C:\Windows\Logs\*.log" >nul 2>&1
del /f /q /s "C:\Windows\Logs\*.etl" >nul 2>&1
del /f /q /s "C:\Windows\Panther\*.log" >nul 2>&1
del /f /q "C:\Windows\Logs\DISM\dism.log" >nul 2>&1
del /f /q "C:\Windows\WindowsUpdate.log" >nul 2>&1
call :LOG "Registros de eventos y logs de Windows borrados"
exit /b

:LIMPIAR_GPU_CACHE
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\NVIDIA\DXCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\NVIDIA\GLCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%APPDATA%\NVIDIA\ComputeCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\NVIDIA Corporation\NV_Cache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\AMD\DxCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\AMD\GLCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%LOCALAPPDATA%\Intel\ShaderCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Cache drivers graficos limpiada"
exit /b

:LIMPIAR_DISCORD
tasklist /FI "IMAGENAME eq Discord.exe" 2>nul | find /I "Discord.exe" >nul
if !errorlevel! EQU 0 (
    taskkill /f /im Discord.exe >nul 2>&1
    ping -n 2 -w 500 127.0.0.1 >nul
)
robocopy "%EMPTY_DIR%" "%APPDATA%\discord\Cache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%APPDATA%\discord\Code Cache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
robocopy "%EMPTY_DIR%" "%APPDATA%\discord\GPUCache" /MIR /NFL /NDL /NJH /NJS /NC /NS /NP >nul 2>&1
call :LOG "Cache de Discord limpiada"
exit /b

:OPTIMIZAR_SSD
defrag C: /L >nul 2>&1
call :LOG "TRIM/optimizacion ejecutado en C:"
exit /b

:: ===================================================================
::  MEDICION DE ESPACIO
:: ===================================================================
:MEDIR_ESPACIO_ANTES
for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "(Get-PSDrive C).Free"`) do set "ESPACIO_ANTES=%%A"
exit /b

:MEDIR_ESPACIO_DESPUES
for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "(Get-PSDrive C).Free"`) do set "ESPACIO_DESPUES=%%A"
for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "[math]::Round((!ESPACIO_DESPUES! - !ESPACIO_ANTES!)/1MB, 2)"`) do set "ESPACIO_LIBERADO=%%A"
exit /b

:: ===================================================================
::  RESUMEN
:: ===================================================================
:MOSTRAR_RESUMEN
echo.
call :LINEA_INFERIOR
echo  %C_GREEN%  [OK] %~1 COMPLETADA%C_RESET%
echo.
echo  %C_CYAN%    Espacio liberado en C:  %C_YELLOW%!ESPACIO_LIBERADO! MB%C_RESET%
echo  %C_GREY%    Log: %LOG_FILE%%C_RESET%
call :LINEA_INFERIOR
ping -n 3 -w 500 127.0.0.1 >nul
exit /b

:LOG
echo [%date% %time%] %~1 >> "%LOG_FILE%"
exit /b

:: ===================================================================
::  VER LOG
:: ===================================================================
:VER_LOG
cls
call :BANNER
echo.
if exist "%LOG_FILE%" (
    echo  %C_YELLOW%  [>] ULTIMAS 30 LINEAS DEL LOG%C_RESET%
    echo.
    powershell -NoProfile -Command "Get-Content '%LOG_FILE%' -Tail 30 | ForEach-Object { Write-Host $_ }"
) else (
    echo  %C_GREY%  No existe ningun log todavia.%C_RESET%
)
echo.
echo  %C_CYAN%  Pulsa una tecla para volver al menu...%C_RESET%
pause >nul
goto MENU_PRINCIPAL

:: ===================================================================
::  SHADERCACHE (CS2 / Steam)
:: ===================================================================
:PREGUNTAR_SHADER
cls
call :BANNER
echo.
echo  %C_MAGENTA%  [>] MODULO SHADERCACHE%C_RESET%
echo.
set /p BORRAR_SHADER="   Borrar la carpeta shadercache? (Y/N): "
if /i "!BORRAR_SHADER!" NEQ "Y" goto PREGUNTAR_RATON
goto SHADERCACHE_CORE

:SHADERCACHE_DIRECTO
cls
call :BANNER
echo.
echo  %C_MAGENTA%  [>] MODULO SHADERCACHE%C_RESET%
echo.

:SHADERCACHE_CORE
tasklist /FI "IMAGENAME eq steam.exe" 2>nul | find /I "steam.exe" >nul
if !errorlevel! EQU 0 (
    echo  %C_YELLOW%  [AVISO] Steam esta corriendo, conviene cerrarlo.%C_RESET%
    set /p CONTINUAR="   Continuar igualmente? (Y/N): "
    if /i "!CONTINUAR!" NEQ "Y" goto PREGUNTAR_RATON
)
set "CONFIG_FILE=%SCRIPT_DIR%shadercache.txt"
if exist "!CONFIG_FILE!" (
    set /p SHADER_PATH=<"!CONFIG_FILE!"
    echo  %C_GREY%   Ruta guardada: %C_WHITE%!SHADER_PATH!%C_RESET%
    set /p USAR_GUARDADA="   Usar esta ruta? (Y/N): "
    if /i "!USAR_GUARDADA!"=="Y" goto BORRAR_SHADER
)
echo  %C_GREY%   Defecto: C:\Program Files ^(x86^)\Steam\steamapps\shadercache\730%C_RESET%
set /p SHADER_PATH="   Ruta (vacio = defecto): "
if "!SHADER_PATH!"=="" set "SHADER_PATH=C:\Program Files (x86)\Steam\steamapps\shadercache\730"
set "SHADER_PATH=!SHADER_PATH:"=!"
echo !SHADER_PATH!>"!CONFIG_FILE!"

:BORRAR_SHADER
if not exist "!SHADER_PATH!" (
    echo  %C_RED%  [ERROR] La ruta no existe: !SHADER_PATH!%C_RESET%
    call :LOG "ERROR shadercache: ruta no existe"
    ping -n 3 -w 500 127.0.0.1 >nul
    goto PREGUNTAR_RATON
)
call :LOADING "Eliminando shadercache...."
rd /s /q "!SHADER_PATH!" >nul 2>&1
if not exist "!SHADER_PATH!" (
    echo  %C_GREEN%  [OK] Shadercache eliminada.%C_RESET%
    call :LOG "Shadercache eliminada: !SHADER_PATH!"
) else (
    echo  %C_RED%  [ERROR] No se pudo eliminar ^(permisos o Steam abierto^).%C_RESET%
    call :LOG "ERROR borrando shadercache"
)
ping -n 2 -w 500 127.0.0.1 >nul
if "!SILENT_MODE!"=="1" goto FIN_SCRIPT
goto PREGUNTAR_RATON

:: ===================================================================
::  CONFIG RATON
:: ===================================================================
:PREGUNTAR_RATON
cls
call :BANNER
echo.
echo  %C_MAGENTA%  [>] MODULO CONFIG RATON%C_RESET%
echo.
set /p APLICAR_RATON="   Aplicar configuracion del raton? (Y/N): "
if /i "!APLICAR_RATON!" NEQ "Y" goto FIN_MODULOS
goto RATON_CORE

:RATON_DIRECTO
cls
call :BANNER
echo.
echo  %C_MAGENTA%  [>] MODULO CONFIG RATON%C_RESET%
echo.

:RATON_CORE
set "RATON_CONFIG=%SCRIPT_DIR%raton.reg"
if not exist "!RATON_CONFIG!" (
    echo  %C_GREY%   No se encontro raton.reg en el directorio del script.%C_RESET%
    set /p RATON_CONFIG="   Ruta completa de raton.reg: "
)
if not exist "!RATON_CONFIG!" (
    echo  %C_RED%  [ERROR] Archivo no encontrado.%C_RESET%
    call :LOG "ERROR raton.reg no encontrado"
    ping -n 3 -w 500 127.0.0.1 >nul
    goto FIN_MODULOS
)
call :LOADING "Importando raton.reg......"
reg import "!RATON_CONFIG!" >nul 2>&1
if !errorlevel! EQU 0 (
    echo  %C_GREEN%  [OK] Configuracion del raton aplicada.%C_RESET%
    call :LOG "Config raton aplicada"
) else (
    echo  %C_RED%  [ERROR] No se pudo importar.%C_RESET%
    call :LOG "ERROR importando raton.reg"
)
ping -n 2 -w 500 127.0.0.1 >nul

:FIN_MODULOS
if "!SILENT_MODE!"=="1" goto FIN_SCRIPT
goto FIN_SCRIPT

:: ===================================================================
::  MODO /todo : ejecuta shadercache + raton + nvidia DESATENDIDOS
::  (sin preguntar; usa rutas guardadas/por defecto; salta lo que falte)
:: ===================================================================
:TODO_MODULOS
echo.
echo  %C_MAGENTA%  [>] MODULOS AUTOMATICOS ^(/todo^)%C_RESET%
echo.

:: --- Shadercache ---
set "CONFIG_FILE=%SCRIPT_DIR%shadercache.txt"
if exist "!CONFIG_FILE!" (
    set /p SHADER_PATH=<"!CONFIG_FILE!"
) else (
    set "SHADER_PATH=C:\Program Files (x86)\Steam\steamapps\shadercache\730"
)
set "SHADER_PATH=!SHADER_PATH:"=!"
if exist "!SHADER_PATH!" (
    call :LOADING "Shadercache..............."
    rd /s /q "!SHADER_PATH!" >nul 2>&1
    if not exist "!SHADER_PATH!" (
        call :LOG "Shadercache eliminada: !SHADER_PATH!"
    ) else (
        call :LOG "ERROR: no se pudo borrar shadercache (Steam abierto?)"
    )
) else (
    call :LOG "Shadercache: ruta no existe, omitida (!SHADER_PATH!)"
)

:: --- Config raton ---
set "RATON_CONFIG=%SCRIPT_DIR%raton.reg"
if exist "!RATON_CONFIG!" (
    call :LOADING "Config raton.............."
    reg import "!RATON_CONFIG!" >nul 2>&1
    if !errorlevel! EQU 0 (
        call :LOG "Config raton aplicada: !RATON_CONFIG!"
    ) else (
        call :LOG "ERROR aplicando config raton"
    )
) else (
    call :LOG "Config raton: raton.reg no encontrado, omitido"
)

:: Nota: el perfil NVIDIA ya se aplico durante la LIMPIEZA COMPLETA,
:: por eso no se repite aqui (evitamos importarlo dos veces).

call :LOG "=== FIN MODO /todo ==="
goto FIN_SCRIPT

:: Version desatendida del modulo NVIDIA (no pregunta nada, omite si falta algo)
:NVIDIA_AUTO
set "NVI_EXE="
if exist "%SCRIPT_DIR%nvidiaProfileInspector.exe" set "NVI_EXE=%SCRIPT_DIR%nvidiaProfileInspector.exe"
if exist "%SCRIPT_DIR%tools\nvidiaProfileInspector.exe" set "NVI_EXE=%SCRIPT_DIR%tools\nvidiaProfileInspector.exe"
if not defined NVI_EXE (
    call :LOG "NVIDIA: nvidiaProfileInspector.exe no encontrado, modulo omitido"
    exit /b
)
set "NIP_FILE="
if exist "%SCRIPT_DIR%CS2_Profile.nip" set "NIP_FILE=%SCRIPT_DIR%CS2_Profile.nip"
if exist "%SCRIPT_DIR%CS2.nip" set "NIP_FILE=%SCRIPT_DIR%CS2.nip"
if not defined NIP_FILE (
    call :LOG "NVIDIA: .nip no encontrado, modulo omitido"
    exit /b
)
"!NVI_EXE!" -silentImport "!NIP_FILE!" >nul 2>&1
if !errorlevel! EQU 0 (
    call :LOG "NVIDIA: perfil CS2 importado: !NIP_FILE!"
) else (
    call :LOG "NVIDIA: ERROR importando perfil CS2"
)
exit /b

:: ===================================================================
::  MODULO NVIDIA - Importar perfil .nip de CS2
::  Requiere nvidiaProfileInspector.exe en el directorio del script
::  (o en una subcarpeta \tools\). Importa el .nip en modo silencioso.
:: ===================================================================
:NVIDIA_DIRECTO
cls
call :BANNER
echo.
echo  %C_GREEN%  [>] MODULO PERFIL NVIDIA - CS2%C_RESET%
echo.
if "%GPU_NVIDIA%" NEQ "1" (
    echo  %C_RED%  [ERROR] No se detecto ninguna GPU NVIDIA en este equipo.%C_RESET%
    echo  %C_GREY%   Este modulo solo funciona con tarjetas NVIDIA.%C_RESET%
    call :LOG "NVIDIA: GPU no detectada, modulo cancelado"
    ping -n 4 -w 500 127.0.0.1 >nul
    goto FIN_SCRIPT
)

:: Localizar nvidiaProfileInspector.exe
set "NVI_EXE="
if exist "%SCRIPT_DIR%nvidiaProfileInspector.exe" set "NVI_EXE=%SCRIPT_DIR%nvidiaProfileInspector.exe"
if exist "%SCRIPT_DIR%tools\nvidiaProfileInspector.exe" set "NVI_EXE=%SCRIPT_DIR%tools\nvidiaProfileInspector.exe"
if not defined NVI_EXE (
    echo  %C_RED%  [ERROR] No se encontro nvidiaProfileInspector.exe%C_RESET%
    echo  %C_GREY%   Colocalo junto al script o en una subcarpeta \tools\%C_RESET%
    echo  %C_GREY%   Descarga: github.com/Orbmu2k/nvidiaProfileInspector%C_RESET%
    call :LOG "NVIDIA: nvidiaProfileInspector.exe no encontrado"
    ping -n 5 -w 500 127.0.0.1 >nul
    goto FIN_SCRIPT
)

:: Localizar el perfil .nip de CS2
set "NIP_FILE="
if exist "%SCRIPT_DIR%CS2_Profile.nip" set "NIP_FILE=%SCRIPT_DIR%CS2_Profile.nip"
if exist "%SCRIPT_DIR%CS2.nip" set "NIP_FILE=%SCRIPT_DIR%CS2.nip"
if not defined NIP_FILE (
    echo  %C_GREY%   No se encontro CS2_Profile.nip en el directorio.%C_RESET%
    set /p NIP_FILE="   Ruta completa del archivo .nip: "
)
set "NIP_FILE=!NIP_FILE:"=!"
if not exist "!NIP_FILE!" (
    echo  %C_RED%  [ERROR] Archivo .nip no encontrado.%C_RESET%
    call :LOG "NVIDIA: .nip no encontrado"
    ping -n 4 -w 500 127.0.0.1 >nul
    goto FIN_SCRIPT
)

:: Importar el perfil de CS2 en modo silencioso
call :LOADING "Importando perfil CS2....."
"!NVI_EXE!" -silentImport "!NIP_FILE!" >nul 2>&1
if !errorlevel! EQU 0 (
    echo  %C_GREEN%  [OK] Perfil NVIDIA de CS2 aplicado correctamente.%C_RESET%
    call :LOG "NVIDIA: perfil CS2 importado: !NIP_FILE!"
) else (
    echo  %C_RED%  [ERROR] Fallo la importacion del perfil.%C_RESET%
    call :LOG "NVIDIA: ERROR importando perfil CS2"
)
ping -n 4 -w 500 127.0.0.1 >nul
goto FIN_SCRIPT

:: ===================================================================
::  FIN
:: ===================================================================
:FIN_SCRIPT
if exist "%EMPTY_DIR%" rd /s /q "%EMPTY_DIR%" >nul 2>&1
cls
call :BANNER
echo.
echo  %C_CYAN%  [>] Sesion finalizada.%C_RESET%  %C_GREY%Log: %LOG_FILE%%C_RESET%
echo.
if "!SILENT_MODE!"=="1" exit /B
echo  %C_GREY%  Pulsa una tecla para salir...%C_RESET%
pause >nul
exit /B
