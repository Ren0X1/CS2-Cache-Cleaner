@echo off
setlocal enabledelayedexpansion

:: Auto-elevacion a administrador
>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B
)

Title RNX Cache Cleaner v2.0
color 0B

:: Obtener informacion del sistema
for /f "tokens=4-5 delims=. " %%i in ('ver') do set VERSION=%%i.%%j

cls
echo.
echo  ===================================================================
echo.
echo    RRRRR   N   N  X   X
echo    R    R  NN  N   X X
echo    RRRRR   N N N    X
echo    R   R   N  NN   X X
echo    R    R  N   N  X   X
echo.
echo              CACHE CLEANER PRO v2.0
echo.
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  -------------------------------------------------------------------
echo   Iniciando proceso de limpieza del sistema...
echo  -------------------------------------------------------------------
echo.
timeout /t 2 /nobreak >nul

:: Limpiar carpeta Temp de Windows
cls
echo.
echo  ===================================================================
echo              RNX CACHE CLEANER PRO v2.0
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  [##........] 10%% - Limpiando Windows Temp...
del /s /f /q c:\windows\temp\*.* >nul 2>&1
for /d %%p in (c:\windows\temp\*) do rmdir "%%p" /s /q >nul 2>&1
timeout /t 1 /nobreak >nul

:: Limpiar Prefetch
cls
echo.
echo  ===================================================================
echo              RNX CACHE CLEANER PRO v2.0
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  [####......] 25%% - Limpiando Prefetch...
del /s /f /q C:\WINDOWS\Prefetch\*.* >nul 2>&1
timeout /t 1 /nobreak >nul

:: Limpiar carpeta Temp del usuario
cls
echo.
echo  ===================================================================
echo              RNX CACHE CLEANER PRO v2.0
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  [######....] 40%% - Limpiando Temp de usuario...
del /s /f /q %temp%\*.* >nul 2>&1
for /d %%p in (%temp%\*) do rmdir "%%p" /s /q >nul 2>&1
timeout /t 1 /nobreak >nul

:: Limpiar archivos temporales adicionales
cls
echo.
echo  ===================================================================
echo              RNX CACHE CLEANER PRO v2.0
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  [#######...] 55%% - Limpiando archivos temporales...
del /f /q c:\windows\*.tmp >nul 2>&1
del /f /q c:\windows\*.log >nul 2>&1
timeout /t 1 /nobreak >nul

:: Limpiar Recent
cls
echo.
echo  ===================================================================
echo              RNX CACHE CLEANER PRO v2.0
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  [#########.] 70%% - Limpiando archivos recientes...
del /f /q %userprofile%\Recent\*.* >nul 2>&1
timeout /t 1 /nobreak >nul

:: Limpiar cola de impresión
cls
echo.
echo  ===================================================================
echo              RNX CACHE CLEANER PRO v2.0
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  [##########] 85%% - Limpiando cola de impresion...
del /f /q c:\windows\spool\printers\*.* >nul 2>&1
timeout /t 1 /nobreak >nul

:: Limpiar registros de eventos
cls
echo.
echo  ===================================================================
echo              RNX CACHE CLEANER PRO v2.0
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  [##########] 95%% - Limpiando registros de eventos...
for /F "tokens=*" %%G in ('wevtutil.exe el') DO (
    wevtutil.exe cl "%%G" >nul 2>&1
)
timeout /t 1 /nobreak >nul

:: Pantalla final
cls
color 0A
echo.
echo  ===================================================================
echo.
echo    RRRRR   N   N  X   X
echo    R    R  NN  N   X X
echo    RRRRR   N N N    X
echo    R   R   N  NN   X X
echo    R    R  N   N  X   X
echo.
echo              CACHE CLEANER PRO v2.0
echo.
echo  ===================================================================
echo.
echo   Sistema: Windows %VERSION%
echo   Usuario: %username%
echo   Fecha: %date% - %time:~0,5%
echo.
echo  -------------------------------------------------------------------
echo              LIMPIEZA COMPLETADA EXITOSAMENTE
echo  -------------------------------------------------------------------
echo.
echo   [##########] 100%%
echo.
echo   - Archivos temporales eliminados
echo   - Cache del sistema limpiada
echo   - Registros de eventos borrados
echo   - Sistema optimizado
echo.
echo  -------------------------------------------------------------------
echo.
timeout /t 8 /nobreak >nul
cls
:: Pregunta para borrar shadercache
color 0E
echo.
echo  ===================================================================
echo.
echo    SSSS  H   H   A   DDDD   EEEEE  RRRR
echo   S      H   H  A A  D   D  E      R   R
echo    SSS   HHHHH A   A D   D  EEEE   RRRR
echo       S  H   H AAAAA D   D  E      R  R
echo   SSSS   H   H A   A DDDD   EEEEE  R   R
echo.
echo              CACHE CLEANER - SHADER MODULE
echo.
echo  ===================================================================
echo.
set /p BORRAR_SHADER="   Deseas borrar la carpeta shadercache? (Y/N): "
if /i "!BORRAR_SHADER!" NEQ "Y" goto FIN_SCRIPT

:: Ruta del archivo de configuracion (mismo directorio del script)
set CONFIG_FILE=%~dp0shadercache.txt

:: Verificar si existe el archivo de configuracion
if exist "!CONFIG_FILE!" (
    echo.
    echo   -------------------------------------------------------------------
    echo    CONFIGURACION GUARDADA ENCONTRADA
    echo   -------------------------------------------------------------------
    
    :: Leer la ruta del archivo txt
    set /p SHADER_PATH=<"!CONFIG_FILE!"
    
    echo.
    echo    Ruta guardada: !SHADER_PATH!
    echo.
    set /p USAR_GUARDADA="   Usar esta ruta? (Y/N): "
    
    if /i "!USAR_GUARDADA!" EQU "Y" (
        goto BORRAR_CARPETA
    )
)

:: Pedir nueva ruta
echo.
echo   -------------------------------------------------------------------
echo    INGRESA LA RUTA DE SHADERCACHE
echo   -------------------------------------------------------------------
echo.
echo    Ruta por defecto: C:\Program Files (x86)\Steam\steamapps\shadercache\730
echo    (Deja en blanco para usar la ruta por defecto)
echo.
set /p SHADER_PATH="   Ruta: "

:: Si esta vacia, usar ruta por defecto
if "!SHADER_PATH!"=="" (
    set SHADER_PATH="C:\Program Files (x86)\Steam\steamapps\shadercache\730"
    echo.
    echo   [INFO] Usando ruta por defecto.
)

:: Limpiar comillas si las hay
set SHADER_PATH=!SHADER_PATH:"=!

:: Guardar la ruta en el archivo de texto
echo !SHADER_PATH!>"!CONFIG_FILE!"
echo.
echo   [OK] Ruta guardada para la proxima vez.

:BORRAR_CARPETA
echo.
echo   -------------------------------------------------------------------
echo    PROCESANDO BORRADO
echo   -------------------------------------------------------------------
echo.
:: Verificar si la carpeta existe
if not exist "!SHADER_PATH!" (
    color 0C
    echo   [ERROR] La ruta no existe: !SHADER_PATH!
    echo.
    timeout /t 3 /nobreak >nul
    goto FIN_SCRIPT
)

:: Borrar la carpeta y todo su contenido
echo    Eliminando carpeta shadercache y su contenido...
echo.
rd /s /q "!SHADER_PATH!" >nul 2>&1

:: Verificar si la carpeta fue eliminada correctamente
if not exist "!SHADER_PATH!" (
    color 0A
    echo   [##########] COMPLETADO
    echo.
    echo   [OK] Carpeta shadercache eliminada correctamente.
) else (
    color 0C
    echo   [ERROR] No se pudo eliminar la carpeta. Verifica permisos.
)
echo.
timeout /t 2 /nobreak >nul

:FIN_SCRIPT
echo.
echo   Presiona cualquier tecla para salir...
pause >nul