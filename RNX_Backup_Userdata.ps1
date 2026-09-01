<#
    ===================================================================
     RNX BACKUP USERDATA  (modulo del RNX Cache Cleaner Pro)
    -------------------------------------------------------------------
     Copia de seguridad de la configuracion de TODOS los juegos de Steam.
     Recorre <Steam>\userdata y genera un ZIP por cada cuenta, con la
     estructura original de Steam intacta dentro del archivo:

         backups\<STEAMID>.zip     ->  730/local/cfg/...   (CS2)
                                       730/remote/...
                                       config/localconfig.vdf
                                       <cualquier otro appid>/...
         backups\_indice.txt

     - Solo se reescribe el ZIP de una cuenta si su contenido ha cambiado
       (se compara el hash guardado en _indice.txt), asi el repositorio
       de git no crece en cada ejecucion.
     - Se excluyen por defecto las capturas (760) y los clips de video
       (gamerecordings) y cualquier archivo mayor de -MaxMB.

     Uso:
       powershell -NoProfile -ExecutionPolicy Bypass -File RNX_Backup_Userdata.ps1 -Destino "C:\...\backups"
    ===================================================================
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Destino,
    [string]$RutaSteam = '',
    [string]$LogFile   = '',
    [int]$MaxMB        = 500,
    [string[]]$Excluir = @('760', 'gamerecordings')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression.FileSystem

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

# ----- Localizar la instalacion de Steam -----
function Get-RutaSteam {
    param([string]$Sugerida)
    if ($Sugerida -and (Test-Path -LiteralPath $Sugerida)) { return $Sugerida }
    $claves = @(
        @{ K = 'HKCU:\Software\Valve\Steam';             V = 'SteamPath'   },
        @{ K = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; V = 'InstallPath' },
        @{ K = 'HKLM:\SOFTWARE\Valve\Steam';             V = 'InstallPath' }
    )
    foreach ($c in $claves) {
        try {
            $val = (Get-ItemProperty -LiteralPath $c.K -Name $c.V -ErrorAction Stop).($c.V)
            if ($val -and (Test-Path -LiteralPath $val)) { return (Resolve-Path -LiteralPath $val).Path }
        } catch { }
    }
    foreach ($d in @("${env:ProgramFiles(x86)}\Steam", "$env:ProgramFiles\Steam", 'C:\Steam', 'D:\Steam')) {
        if ($d -and (Test-Path -LiteralPath $d)) { return $d }
    }
    return $null
}

$steam = Get-RutaSteam -Sugerida $RutaSteam
if (-not $steam) {
    Write-Linea "BACKUP: no se encontro la instalacion de Steam, modulo omitido" $YE
    exit 2
}
$userdata = Join-Path $steam 'userdata'
if (-not (Test-Path -LiteralPath $userdata)) {
    Write-Linea "BACKUP: no existe $userdata, modulo omitido" $YE
    exit 2
}

$maxBytes = [int64]$MaxMB * 1MB
New-Item -ItemType Directory -Force -Path $Destino | Out-Null
Write-Linea ("BACKUP: origen  " + $userdata) $GY
Write-Linea ("BACKUP: destino " + $Destino) $GY

$sha = [System.Security.Cryptography.SHA256]::Create()

function Get-HashContenido {
    param($Archivos, [string]$Base)
    $sb = New-Object System.Text.StringBuilder
    foreach ($f in ($Archivos | Sort-Object -Property FullName)) {
        [void]$sb.Append($f.FullName.Substring($Base.Length))
        [void]$sb.Append('|'); [void]$sb.Append($f.Length)
        [void]$sb.Append('|'); [void]$sb.Append($f.LastWriteTimeUtc.Ticks)
        [void]$sb.Append([Environment]::NewLine)
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
    return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').Substring(0, 16)
}

$indice = Join-Path $Destino '_indice.txt'

# Hashes de la ejecucion anterior:  steamid|hash|archivos|bytes
$previo = @{}
if (Test-Path -LiteralPath $indice) {
    foreach ($l in (Get-Content -LiteralPath $indice -ErrorAction SilentlyContinue)) {
        $p = $l.Split('|')
        if ($p.Count -ge 2) { $previo[$p[0]] = $p[1] }
    }
}

$totCuentas = 0; $totBloq = 0; $totGrandes = 0; $totBytes = [int64]0
$lineas = New-Object System.Collections.ArrayList

foreach ($sid in (Get-ChildItem -LiteralPath $userdata -Directory -ErrorAction SilentlyContinue)) {
    if ($sid.Name -notmatch '^\d+$') { continue }
    $totCuentas++
    $base = $sid.FullName

    # Todo el userdata de la cuenta, menos las carpetas excluidas
    $archivos = @(Get-ChildItem -LiteralPath $base -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $rel = $_.FullName.Substring($base.Length + 1)
            $raiz = $rel.Split('\')[0]
            -not ($Excluir -contains $raiz)
        })

    $grandes  = @($archivos | Where-Object { $_.Length -gt $maxBytes })
    $archivos = @($archivos | Where-Object { $_.Length -le $maxBytes })
    foreach ($f in $grandes) {
        $totGrandes++
        Write-Linea ("BACKUP: omitido por tamano (" + [math]::Round($f.Length / 1MB, 1) + " MB > " + $MaxMB + " MB): " + $f.FullName) $YE
    }
    if ($archivos.Count -eq 0) {
        Write-Linea ("BACKUP: SteamID " + $sid.Name + " no tiene nada que guardar") $YE
        continue
    }

    $hash      = Get-HashContenido -Archivos $archivos -Base $base
    $zip       = Join-Path $Destino ($sid.Name + '.zip')
    $sumaBytes = ($archivos | Measure-Object -Property Length -Sum).Sum

    # Sin cambios respecto a la ejecucion anterior: no se toca el ZIP
    if ($previo.ContainsKey($sid.Name) -and $previo[$sid.Name] -eq $hash -and (Test-Path -LiteralPath $zip)) {
        $totBytes += (Get-Item -LiteralPath $zip).Length
        [void]$lineas.Add(($sid.Name + '|' + $hash + '|' + $archivos.Count + '|' + $sumaBytes))
        Write-Linea ("BACKUP: SteamID " + $sid.Name + " sin cambios, ZIP intacto") $GY
        continue
    }

    $estado = if (Test-Path -LiteralPath $zip) { 'actualizado' } else { 'creado' }
    $tmp = $zip + '.tmp'
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force }
    $bloqueados = 0
    try {
        $z = [System.IO.Compression.ZipFile]::Open($tmp, 'Create')
        try {
            foreach ($f in ($archivos | Sort-Object -Property FullName)) {
                $rel = $f.FullName.Substring($base.Length + 1).Replace('\', '/')
                try {
                    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal)
                } catch {
                    $bloqueados++; $totBloq++
                }
            }
        } finally { $z.Dispose() }
        Move-Item -LiteralPath $tmp -Destination $zip -Force
    } catch {
        Write-Linea ("BACKUP: ERROR comprimiendo " + $sid.Name + " -> " + $_.Exception.Message) $RD
        if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
        continue
    }

    if ($bloqueados -gt 0) {
        Write-Linea ("BACKUP: " + $sid.Name + " -> " + $bloqueados + " archivo(s) en uso omitidos (Steam abierto?)") $YE
    }
    $tam = (Get-Item -LiteralPath $zip).Length
    $totBytes += $tam
    if ($tam -gt (95MB)) {
        Write-Linea ("BACKUP: AVISO - " + $sid.Name + ".zip pesa " + [math]::Round($tam / 1MB, 1) + " MB; GitHub rechaza archivos de mas de 100 MB salvo que uses Git LFS (ver .gitattributes). Si no, baja -MaxMB para dejar fuera las partidas guardadas gigantes") $YE
    }
    [void]$lineas.Add(($sid.Name + '|' + $hash + '|' + $archivos.Count + '|' + $sumaBytes))
    Write-Linea ("BACKUP: " + $sid.Name + ".zip " + $estado + " - " + $archivos.Count + " archivos, " + [math]::Round($tam / 1MB, 1) + " MB") $GR
}

if ($lineas.Count -gt 0) { Set-Content -LiteralPath $indice -Encoding UTF8 -Value ($lineas | Sort-Object) }

if ($totCuentas -eq 0) {
    Write-Linea "BACKUP: no se encontro ninguna cuenta en userdata" $YE
    exit 2
}

Write-Linea ("BACKUP: OK - " + $totCuentas + " cuenta(s), " + [math]::Round($totBytes / 1MB, 1) + " MB en " + $Destino) $GR
if ($totGrandes -gt 0) { Write-Linea ("BACKUP: " + $totGrandes + " archivo(s) omitidos por superar " + $MaxMB + " MB") $YE -SoloLog }
if ($totBloq   -gt 0) { Write-Linea ("BACKUP: " + $totBloq + " archivo(s) omitidos por estar en uso") $YE -SoloLog }
exit 0
