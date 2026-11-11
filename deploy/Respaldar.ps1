<#
.SYNOPSIS
  Respaldo completo: base de datos (pg_dump) + claves de Data Protection + configuración de la app.
.DESCRIPTION
  Lo ejecuta la tarea programada (Registrar-TareaRespaldo.ps1) y Actualizar-App.ps1 antes de cada actualización.
  Copia 1: {Datos}\respaldos\archivos.  Copia 2 (si está configurada): en claro, en otro disco.
  Copia 3 (si está configurada): CIFRADA (.zip.enc) en la carpeta fuera de sitio.
  Siempre deja {Datos}\respaldos\ultimo.json, también si falla: lo lee la página «Estado del sistema».
#>
[CmdletBinding()]
param(
    [string]$Motivo = 'diario',
    [string]$ConfigRuta
)

. (Join-Path $PSScriptRoot 'Comun.ps1')
Exigir-Administrador
$cfg = Cargar-Configuracion $ConfigRuta
$r = Rutas-Parroquia $cfg
Iniciar-Log $r 'respaldo.log'

$inicio = Get-Date
$sufijo = ''
if ($Motivo -ne 'diario') { $sufijo = '-' + ($Motivo -replace '[^a-zA-Z0-9-]', '') }
$nombre = 'parroquia-{0:yyyyMMdd-HHmm}{1}.zip' -f $inicio, $sufijo
$temp = Join-Path $r.Respaldos ('tmp-' + [guid]::NewGuid().ToString('N'))
$marcador = [ordered]@{
    fecha = $inicio.ToString('o'); resultado = 'error'; mensaje = ''; motivo = $Motivo
    archivo = $nombre; bytes = 0; sha256 = ''; copia2 = $false; copia3 = $false
    migracion = ''; conteos = @{}
}
$codigo = 0

try {
    New-Item -ItemType Directory -Force -Path $r.Archivos, $temp | Out-Null
    Configurar-Pg $cfg $r
    Escribir-Log "Respaldo '$Motivo' iniciado: $nombre"

    # El conteo va ANTES del volcado: si alguien guarda entre ambos, la restauración tendrá >= filas (nunca menos).
    $base = $cfg.PostgreSql.Base
    $marcador.conteos = Contar-Tablas $cfg $base
    $marcador.migracion = Ultima-Migracion $cfg $base

    $dump = Join-Path $temp 'base.dump'
    Ejecutar-Pg $cfg 'pg_dump' @('-h', $cfg.PostgreSql.Host, '-p', [string]$cfg.PostgreSql.Puerto, '-U', $cfg.PostgreSql.Usuario,
                                  '-d', $base, '-Fc', '-f', $dump) | Out-Null
    # Comprobar que el volcado se puede leer: un archivo corrupto no sirve de respaldo.
    Ejecutar-Pg $cfg 'pg_restore' @('--list', $dump) | Out-Null

    $extra = Join-Path $temp 'extra'
    New-Item -ItemType Directory -Force -Path $extra | Out-Null
    if (Test-Path -LiteralPath $r.Claves) { Copy-Item -LiteralPath $r.Claves -Destination (Join-Path $extra 'claves') -Recurse }
    $appsettings = Join-Path $r.App 'appsettings.Production.json'
    if (Test-Path -LiteralPath $appsettings) { Copy-Item -LiteralPath $appsettings -Destination $extra }
    $marcador | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $temp 'manifiesto.json') -Encoding UTF8

    $zip = Join-Path $r.Archivos $nombre
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($temp, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $marcador.bytes = (Get-Item -LiteralPath $zip).Length
    $marcador.sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Escribir-Log ("Copia 1 lista: {0} ({1:N1} MB)" -f $zip, ($marcador.bytes / 1MB))

    $fallos = @()

    if ($cfg.Respaldo.Copia2) {
        try {
            New-Item -ItemType Directory -Force -Path $cfg.Respaldo.Copia2 | Out-Null
            Copy-Item -LiteralPath $zip -Destination $cfg.Respaldo.Copia2 -Force
            Aplicar-Retencion $cfg.Respaldo.Copia2 $cfg.Respaldo.Diarios $cfg.Respaldo.Mensuales
            $marcador.copia2 = $true
            Escribir-Log "Copia 2 lista: $($cfg.Respaldo.Copia2)"
        } catch { $fallos += "copia 2: $($_.Exception.Message)" }
    }

    if ($cfg.Respaldo.Copia3) {
        try {
            $frase = Leer-Frase $r
            if (-not $frase) { throw 'no hay frase de cifrado (ejecute Registrar-TareaRespaldo.ps1 para guardarla).' }
            New-Item -ItemType Directory -Force -Path $cfg.Respaldo.Copia3 | Out-Null
            Proteger-Archivo $zip (Join-Path $cfg.Respaldo.Copia3 "$nombre.enc") $frase
            Aplicar-Retencion $cfg.Respaldo.Copia3 $cfg.Respaldo.Diarios $cfg.Respaldo.Mensuales
            $marcador.copia3 = $true
            Escribir-Log "Copia 3 (cifrada) lista: $($cfg.Respaldo.Copia3)"
        } catch { $fallos += "copia 3: $($_.Exception.Message)" }
    }

    Aplicar-Retencion $r.Archivos $cfg.Respaldo.Diarios $cfg.Respaldo.Mensuales

    if ($fallos.Count -gt 0) {
        $marcador.mensaje = "Respaldo local hecho, pero falló la " + ($fallos -join '; ')
        throw $marcador.mensaje
    }
    $marcador.resultado = 'ok'
    Escribir-Log 'Respaldo terminado correctamente.'
} catch {
    if (-not $marcador.mensaje) { $marcador.mensaje = $_.Exception.Message }
    Escribir-Log "Respaldo FALLÓ: $($marcador.mensaje)" 'ERROR'
    $codigo = 1
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    try {
        # El detalle de conteos va solo en el manifiesto del zip; el marcador que lee la web se mantiene pequeño.
        $publico = [ordered]@{}
        foreach ($k in $marcador.Keys) { if ($k -ne 'conteos') { $publico[$k] = $marcador[$k] } }
        Escribir-Json (Join-Path $r.Respaldos 'ultimo.json') $publico
    } catch { Escribir-Log "No se pudo escribir ultimo.json: $($_.Exception.Message)" 'ERROR'; $codigo = 1 }
}
exit $codigo
