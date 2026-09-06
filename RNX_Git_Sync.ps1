<#
    ===================================================================
     RNX GIT SYNC  (modulo del RNX Cache Cleaner Pro)
    -------------------------------------------------------------------
     Sincroniza con GitHub lo que acaba de generar el backup:

         git add backups -> git commit -> git pull --rebase -> git push

     Reglas de la casa:
       - Solo se tocan las rutas indicadas en -Rutas (por defecto
         "backups"), asi que nunca se sube trabajo a medias del resto
         del repositorio.
       - Nunca bloquea al usuario: los prompts de credenciales estan
         desactivados (GIT_TERMINAL_PROMPT=0) y cada comando tiene un
         limite de tiempo (-TimeoutSeg).
       - Si algo falla (sin red, sin credenciales, conflicto...) se
         avisa en pantalla y en el log, pero el commit local queda
         hecho y se subira en la siguiente ejecucion.

     Codigos de salida:
       0 = subido correctamente, o no habia nada que subir
       2 = modulo omitido (sin git / no es un repo / sin remoto)
       3 = habia cambios pero no se pudieron subir

     Uso:
       powershell -NoProfile -ExecutionPolicy Bypass -File RNX_Git_Sync.ps1 -Repo "C:\...\CS2-Cache-Cleaner"
    ===================================================================
#>
[CmdletBinding()]
param(
    [string]$Repo    = '',
    [string[]]$Rutas = @('backups'),
    [string]$Remoto  = 'origin',
    [string]$Rama    = '',
    [string]$LogFile = '',
    [string]$Mensaje = '',
    [int]$TimeoutSeg = 900
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

# Con "powershell -File", -Rutas backups,otra llega como UNA sola cadena.
$Rutas = @($Rutas | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Rutas.Count -eq 0) { $Rutas = @('backups') }

# ----- Paleta neon (la misma que usa el .bat) -----
$E  = [char]27
$CY = "$E[38;5;51m"; $GR = "$E[38;5;46m"; $YE = "$E[38;5;226m"
$RD = "$E[38;5;196m"; $GY = "$E[38;5;240m"; $RS = "$E[0m"

function Write-Linea {
    param([string]$Texto, [string]$Color = $CY, [switch]$SoloLog)
    if (-not $SoloLog) { Write-Host ("  " + $Color + $Texto + $RS) }
    if ($LogFile) {
        try {
            Add-Content -LiteralPath $LogFile -Encoding UTF8 -Value ("[" + (Get-Date -Format 'dd/MM/yyyy HH:mm:ss') + "] " + $Texto)
        } catch { }
    }
}

# Nada de ventanas de credenciales ni paginadores: esto corre desatendido.
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE     = 'never'
$env:GIT_PAGER           = 'cat'

if (-not $Repo) { $Repo = $PSScriptRoot }
if (-not $Repo -or -not (Test-Path -LiteralPath $Repo)) {
    Write-Linea "GIT: no se encontro la carpeta del repositorio, sincronizacion omitida" $YE
    exit 2
}
$Repo = (Resolve-Path -LiteralPath $Repo).Path

# ----- Lanzar git con limite de tiempo y capturando toda la salida -----
function Format-Arg {
    param([string]$A)
    if ($A -match '[\s"]') { return '"' + ($A -replace '"', '\"') + '"' }
    return $A
}

function Invoke-Git {
    param([string[]]$Argumentos, [int]$Timeout = 0)
    if ($Timeout -le 0) { $Timeout = $TimeoutSeg }
    $linea = (($Argumentos | ForEach-Object { Format-Arg $_ }) -join ' ')
    $fo = [System.IO.Path]::GetTempFileName()
    $fe = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath 'git' -ArgumentList $linea -WorkingDirectory $Repo `
                           -NoNewWindow -PassThru -RedirectStandardOutput $fo -RedirectStandardError $fe
        # Sin tocar .Handle, Windows PowerShell deja $p.ExitCode vacio
        $null = $p.Handle
    } catch {
        return [pscustomobject]@{ Code = 127; Salida = $_.Exception.Message }
    }
    if (-not $p.WaitForExit($Timeout * 1000)) {
        try { $p.Kill() } catch { }
        return [pscustomobject]@{ Code = 124; Salida = ("tiempo agotado tras " + $Timeout + " s") }
    }
    $p.WaitForExit()
    $txt = ''
    foreach ($f in @($fo, $fe)) {
        $c = Get-Content -LiteralPath $f -Raw -ErrorAction SilentlyContinue
        if ($c) { $txt += $c }
    }
    Remove-Item -LiteralPath $fo, $fe -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{ Code = $p.ExitCode; Salida = $txt.Trim() }
}

# Vuelca la salida de git al log (y a pantalla solo si hubo error)
function Write-SalidaGit {
    param($R, [switch]$EnPantalla)
    if (-not $R.Salida) { return }
    foreach ($l in ($R.Salida -split "`r?`n")) {
        if ($l.Trim()) { Write-Linea ("GIT: | " + $l.Trim()) $GY -SoloLog:(-not $EnPantalla) }
    }
}

# ----- Comprobaciones previas -----
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Linea "GIT: git no esta instalado o no esta en el PATH, sincronizacion omitida" $YE
    exit 2
}

$r = Invoke-Git @('rev-parse', '--show-toplevel') 30
if ($r.Code -ne 0) {
    Write-Linea ("GIT: " + $Repo + " no es un repositorio git, sincronizacion omitida") $YE
    exit 2
}
$Repo = ($r.Salida -split "`r?`n")[0].Trim().Replace('/', '\')

$r = Invoke-Git @('remote', 'get-url', $Remoto) 30
if ($r.Code -ne 0 -or -not $r.Salida) {
    Write-Linea ("GIT: el repositorio no tiene remoto '" + $Remoto + "', sincronizacion omitida") $YE
    exit 2
}
$urlRemoto = ($r.Salida -split "`r?`n")[0].Trim()

if (-not $Rama) {
    $r = Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD') 30
    if ($r.Code -ne 0) {
        Write-Linea "GIT: no se pudo determinar la rama actual, sincronizacion omitida" $YE
        exit 2
    }
    $Rama = $r.Salida.Trim()
}
if ($Rama -eq 'HEAD') {
    Write-Linea "GIT: HEAD desacoplado (detached), no hay rama a la que subir; sincronizacion omitida" $YE
    exit 2
}

Write-Linea ("GIT: repositorio " + $Repo) $GY
Write-Linea ("GIT: remoto " + $Remoto + " (" + $urlRemoto + ") rama " + $Rama) $GY

# ----- Identidad para el commit (si el usuario no la tiene configurada) -----
$ident = @()
$r = Invoke-Git @('config', '--get', 'user.email') 15
if ($r.Code -ne 0 -or -not $r.Salida.Trim()) {
    $ident = @('-c', 'user.name=RNX Cache Cleaner', '-c', 'user.email=rnx-cleaner@localhost')
    Write-Linea "GIT: no hay user.email configurado, el commit se firma como 'RNX Cache Cleaner'" $YE
}

# ----- Preparar los cambios de las rutas vigiladas -----
$vigiladas = @()
foreach ($ruta in $Rutas) {
    if (-not (Test-Path -LiteralPath (Join-Path $Repo $ruta))) {
        Write-Linea ("GIT: la ruta '" + $ruta + "' no existe todavia, se ignora") $GY -SoloLog
        continue
    }
    $vigiladas += $ruta
    $r = Invoke-Git (@('add', '-A', '--') + @($ruta)) 600
    if ($r.Code -ne 0) {
        Write-Linea ("GIT: ERROR al preparar '" + $ruta + "' -> " + $r.Salida) $RD
        exit 3
    }
}
if ($vigiladas.Count -eq 0) {
    Write-Linea "GIT: no hay ninguna ruta que sincronizar" $YE
    exit 2
}

# ----- Commit (solo de las rutas vigiladas) -----
$hayCommit = $false
$r = Invoke-Git (@('diff', '--cached', '--quiet', '--') + $vigiladas) 300
if ($r.Code -eq 1) {
    if (-not $Mensaje) { $Mensaje = 'backup: config Steam userdata ' + (Get-Date -Format 'dd/MM/yyyy HH:mm') }
    $r = Invoke-Git ($ident + @('commit', '-m', $Mensaje, '--') + $vigiladas) 600
    if ($r.Code -ne 0) {
        Write-Linea ("GIT: ERROR al crear el commit -> " + $r.Salida) $RD
        exit 3
    }
    $hayCommit = $true
    Write-Linea ("GIT: commit creado - " + $Mensaje) $GR
    Write-SalidaGit $r
} elseif ($r.Code -ne 0) {
    Write-Linea ("GIT: ERROR comparando los cambios -> " + $r.Salida) $RD
    exit 3
} else {
    Write-Linea "GIT: los backups no han cambiado desde la ultima subida" $GY
}

# ----- Queda algo por subir? (incluye intentos fallidos anteriores) -----
$r = Invoke-Git @('rev-parse', '--abbrev-ref', '--symbolic-full-name', '@{u}') 30
$hayUpstream = ($r.Code -eq 0 -and $r.Salida.Trim())
$pendientes  = -1
if ($hayUpstream) {
    $r = Invoke-Git @('rev-list', '--count', '@{u}..HEAD') 120
    if ($r.Code -eq 0) { $pendientes = [int]($r.Salida.Trim()) }
    if ($pendientes -eq 0 -and -not $hayCommit) {
        Write-Linea "GIT: todo sincronizado, no hay nada que subir" $GR
        exit 0
    }
    if ($pendientes -gt 1) { Write-Linea ("GIT: " + $pendientes + " commit(s) pendientes de subir") $GY }
}

# ----- Traer antes lo que haya en GitHub -----
if ($hayUpstream) {
    Write-Linea "GIT: comprobando novedades en GitHub (fetch)..." $CY
    $r = Invoke-Git @('fetch', $Remoto, $Rama)
    if ($r.Code -ne 0) {
        Write-SalidaGit $r -EnPantalla
        Write-Linea "GIT: no se pudo contactar con GitHub. El commit local esta hecho y se subira en la proxima ejecucion" $YE
        exit 3
    }
    Write-SalidaGit $r
    $entrantes = 0
    $r = Invoke-Git @('rev-list', '--count', 'HEAD..FETCH_HEAD') 120
    if ($r.Code -eq 0 -and $r.Salida.Trim()) { $entrantes = [int]($r.Salida.Trim()) }
    if ($entrantes -gt 0) {
        # Solo se reescribe la historia si el arbol de trabajo esta limpio:
        # esto corre desatendido y jamas debe dejar marcas de conflicto
        # dentro de los archivos del usuario.
        $sucio = (Invoke-Git @('diff', '--quiet', 'HEAD') 300).Code -ne 0
        if ($sucio) {
            Write-Linea ("GIT: GitHub tiene " + $entrantes + " commit(s) nuevos y hay cambios sin guardar en el repositorio, asi que no se toca nada. El backup ya esta commiteado en local: guarda o descarta esos cambios, haz 'git pull --rebase' y se subira solo en la proxima ejecucion") $RD
            exit 3
        }
        Write-Linea ("GIT: integrando " + $entrantes + " commit(s) de GitHub (rebase)...") $CY
        $r = Invoke-Git @('rebase', 'FETCH_HEAD')
        if ($r.Code -ne 0) {
            Write-SalidaGit $r -EnPantalla
            Invoke-Git @('rebase', '--abort') 60 | Out-Null
            Write-Linea "GIT: conflicto al integrar lo que hay en GitHub. Se ha dejado el repositorio como estaba; resuelvelo a mano y vuelve a ejecutar" $RD
            exit 3
        }
        Write-SalidaGit $r
    } else {
        Write-Linea "GIT: GitHub no tiene novedades" $GY
    }
}

# ----- Subir -----
Write-Linea "GIT: subiendo a GitHub (push)..." $CY
if ($hayUpstream) {
    $r = Invoke-Git @('push', $Remoto, ('HEAD:refs/heads/' + $Rama))
} else {
    $r = Invoke-Git @('push', '-u', $Remoto, $Rama)
}
if ($r.Code -eq 0) {
    Write-SalidaGit $r
    Write-Linea "GIT: OK - backups sincronizados con GitHub" $GR
    exit 0
}

# ----- Diagnostico del fallo: un mensaje util, no un volcado de git -----
Write-SalidaGit $r -EnPantalla
$s = $r.Salida
if ($r.Code -eq 124) {
    Write-Linea "GIT: el push tardo demasiado y se cancelo (conexion lenta o ZIP enorme). Sube GIT_TIMEOUT o baja BACKUP_MAX_MB" $RD
} elseif ($s -match '(?i)quota|exceeded|bandwidth') {
    Write-Linea "GIT: GitHub ha rechazado el push por la cuota de Git LFS (1 GB gratis de almacenamiento y otro de trafico al mes). Amplia el plan o reduce el tamano de los ZIP" $RD
} elseif ($s -match '(?i)authentication|could not read Username|403|permission denied|terminal prompts disabled') {
    Write-Linea "GIT: sin credenciales validas para GitHub. Haz un 'git push' a mano una vez para guardarlas (Git Credential Manager) o configura un token" $RD
} elseif ($s -match '(?i)exceeds .{0,20}file size limit|GH001') {
    Write-Linea "GIT: hay un archivo de mas de 100 MB que no pasa por Git LFS. Revisa .gitattributes (backups/*.zip) o baja BACKUP_MAX_MB" $RD
} elseif ($s -match '(?i)non-fast-forward|fetch first|\[rejected\]') {
    Write-Linea "GIT: GitHub tiene commits que no estan en local. Haz 'git pull --rebase' a mano y vuelve a ejecutar" $RD
} elseif ($s -match '(?i)could not resolve host|unable to access|timed out|network') {
    Write-Linea "GIT: sin conexion con GitHub. El commit local esta hecho y se subira en la proxima ejecucion" $YE
} else {
    Write-Linea ("GIT: el push ha fallado (codigo " + $r.Code + "). El commit local esta hecho y se subira en la proxima ejecucion") $RD
}
exit 3
