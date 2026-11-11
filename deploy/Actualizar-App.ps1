<#
.SYNOPSIS
  Actualiza la aplicación a una versión nueva (paquete de Publicar.ps1) con respaldo previo y vuelta atrás.
.DESCRIPTION
  verifica el paquete → respaldo 'previo-actualizacion' → detiene el servicio → guarda los binarios actuales en app.anterior →
  descomprime la versión nueva conservando appsettings.Production.json → arranca (las migraciones corren al iniciar) →
  espera /salud. Si no responde, devuelve los binarios anteriores.
  OJO: si la versión nueva alcanzó a migrar la base, volver a los binarios viejos puede no bastar: restaure el respaldo
  'previo-actualizacion' con Restaurar.ps1 (el script lo indica).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Paquete,
    [string]$ConfigRuta
)

. (Join-Path $PSScriptRoot 'Comun.ps1')
Exigir-Administrador
$cfg = Cargar-Configuracion $ConfigRuta
$r = Rutas-Parroquia $cfg
Iniciar-Log $r 'instalacion.log'
$servicio = $cfg.Servicio.Nombre
if (-not (Get-Service -Name $servicio -ErrorAction SilentlyContinue)) { throw "No existe el servicio $servicio. Use Instalar-App.ps1." }

$hashArchivo = "$Paquete.sha256"
if (Test-Path -LiteralPath $hashArchivo) {
    $esperado = ((Get-Content -LiteralPath $hashArchivo -Raw) -split '\s+')[0].Trim().ToLowerInvariant()
    if ($esperado -ne (Get-FileHash -LiteralPath $Paquete -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'El paquete no coincide con su .sha256.' }
} else { Escribir-Log "No hay ${hashArchivo}: no se pudo verificar la integridad." 'AVISO' }

Escribir-Log 'Respaldo previo a la actualización...'
& (Join-Path $PSScriptRoot 'Respaldar.ps1') -Motivo 'previo-actualizacion' -ConfigRuta $ConfigRuta
if ($LASTEXITCODE -ne 0) { throw 'El respaldo previo falló: no se actualiza.' }

$app = $r.App
$anterior = "$app.anterior"
$temporal = "$app.nueva"
Remove-Item -LiteralPath $temporal -Recurse -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($Paquete, $temporal)   # primero se descomprime: si falla, no se tocó nada
if (-not (Test-Path -LiteralPath (Join-Path $temporal 'Parroquia.Web.exe'))) { throw 'El paquete no contiene Parroquia.Web.exe.' }

Stop-Service -Name $servicio -Force
Escribir-Log "Servicio $servicio detenido."
try {
    Remove-Item -LiteralPath $anterior -Recurse -Force -ErrorAction SilentlyContinue
    Rename-Item -LiteralPath $app -NewName (Split-Path $anterior -Leaf)
    Rename-Item -LiteralPath $temporal -NewName (Split-Path $app -Leaf)
    Copy-Item -LiteralPath (Join-Path $anterior 'appsettings.Production.json') -Destination $app -Force
    Remove-Item -LiteralPath (Join-Path $app 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue
    # La carpeta nueva no hereda los permisos de la anterior.
    Proteger-Carpeta $app -Lectura @("NT SERVICE\$servicio")

    Start-Service -Name $servicio
    if (-not (Esperar-Salud -Puerto $cfg.Servidor.PuertoHttps -Segundos 180)) { throw 'La aplicación nueva no respondió en /salud.' }
    Escribir-Log 'Actualización correcta.'
    Write-Host "Listo. La versión anterior queda en $anterior (puede borrarla cuando confirme que todo funciona)."
} catch {
    Escribir-Log "Actualización FALLÓ: $($_.Exception.Message). Se devuelven los binarios anteriores." 'ERROR'
    Stop-Service -Name $servicio -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $anterior) {
        Remove-Item -LiteralPath $app -Recurse -Force -ErrorAction SilentlyContinue
        Rename-Item -LiteralPath $anterior -NewName (Split-Path $app -Leaf)
    }
    Start-Service -Name $servicio -ErrorAction SilentlyContinue
    Write-Host 'Si la versión nueva alcanzó a migrar la base, restaure el respaldo "previo-actualizacion":' -ForegroundColor Yellow
    Write-Host '  .\Restaurar.ps1 -Archivo <carpeta respaldos\archivos>\parroquia-...-previo-actualizacion.zip' -ForegroundColor Yellow
    throw
}
