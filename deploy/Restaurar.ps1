<#
.SYNOPSIS
  Restaura la base de datos de producción desde un respaldo (zip, o .zip.enc de la copia 3).
.DESCRIPTION
  DESTRUCTIVO: reemplaza TODA la información actual. Antes hace un respaldo de seguridad (motivo 'previo-restauracion'),
  pide escribir RESTAURAR, detiene el servicio, recrea la base, restaura y vuelve a arrancar.
  Para probar un respaldo SIN tocar producción use Probar-Restauracion.ps1.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Archivo,
    [switch]$RestaurarClaves,        # también devuelve claves\ (sesiones y formularios sobreviven); úselo al reinstalar en otro equipo
    [securestring]$Frase,            # solo si el archivo es .enc; si falta se pide
    [string]$ConfigRuta
)

. (Join-Path $PSScriptRoot 'Comun.ps1')
Exigir-Administrador
$cfg = Cargar-Configuracion $ConfigRuta
$r = Rutas-Parroquia $cfg
Iniciar-Log $r 'restauracion.log'
Configurar-Pg $cfg $r

if (-not (Test-Path -LiteralPath $Archivo)) { throw "No existe '$Archivo'." }
$base = $cfg.PostgreSql.Base

Write-Host ''
Write-Host "Se va a REEMPLAZAR la base '$base' con el contenido de:" -ForegroundColor Yellow
Write-Host "  $Archivo" -ForegroundColor Yellow
Write-Host 'Todo lo digitado después de ese respaldo se perderá (antes se hará un respaldo de seguridad).' -ForegroundColor Yellow
if ((Read-Host 'Escriba RESTAURAR para continuar') -cne 'RESTAURAR') { Write-Host 'Cancelado.'; exit 2 }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('parroquia-restaurar-' + [guid]::NewGuid().ToString('N'))
$servicio = $cfg.Servicio.Nombre
$servicioExiste = [bool](Get-Service -Name $servicio -ErrorAction SilentlyContinue)
try {
    New-Item -ItemType Directory -Force -Path $temp | Out-Null

    # 1. Preparar el contenido (descifrar si hace falta) ANTES de tocar nada.
    $zip = $Archivo
    if ($Archivo -like '*.enc') {
        if (-not $Frase) { $Frase = Read-Host 'Frase de cifrado de la copia 3' -AsSecureString }
        $zip = Join-Path $temp 'respaldo.zip'
        Desproteger-Archivo $Archivo $zip $Frase
    }
    $contenido = Join-Path $temp 'contenido'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $contenido)
    $dump = Join-Path $contenido 'base.dump'
    if (-not (Test-Path -LiteralPath $dump)) { throw 'El respaldo no contiene base.dump.' }
    Ejecutar-Pg $cfg 'pg_restore' @('--list', $dump) | Out-Null
    Escribir-Log 'Respaldo verificado; se hace el respaldo de seguridad previo.'

    # 2. Respaldo de seguridad de lo que hay hoy.
    & (Join-Path $PSScriptRoot 'Respaldar.ps1') -Motivo 'previo-restauracion' -ConfigRuta $ConfigRuta
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo hacer el respaldo de seguridad previo; no se restaura nada.' }

    # 3. Restaurar.
    if ($servicioExiste) { Stop-Service -Name $servicio -Force; Escribir-Log "Servicio $servicio detenido." }
    Borrar-Base $cfg $base
    Nueva-Base $cfg $base
    Restaurar-Dump $cfg $base $dump
    Escribir-Log 'Base restaurada.'

    if ($RestaurarClaves -and (Test-Path -LiteralPath (Join-Path $contenido 'extra\claves'))) {
        New-Item -ItemType Directory -Force -Path $r.Claves | Out-Null
        Copy-Item -Path (Join-Path $contenido 'extra\claves\*') -Destination $r.Claves -Force
        Escribir-Log 'Claves de Data Protection restauradas.'
    }
} catch {
    Escribir-Log "Restauración FALLÓ: $($_.Exception.Message)" 'ERROR'
    Write-Host 'Si la base quedó a medias, vuelva a ejecutar este script con el respaldo de seguridad (carpeta respaldos\archivos, motivo previo-restauracion).' -ForegroundColor Red
    throw
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    if ($servicioExiste) {
        Start-Service -Name $servicio
        if (Esperar-Salud -Puerto $cfg.Servidor.PuertoHttps) { Escribir-Log 'La aplicación respondió correctamente (/salud).' }
        else { Escribir-Log 'La aplicación no respondió en /salud: revise los logs.' 'ERROR' }
    }
}
