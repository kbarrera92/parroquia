<#
.SYNOPSIS
  Prueba un respaldo: lo restaura en una base TEMPORAL, compara su contenido con lo que se respaldó y la borra.
  No toca la base de producción. Se puede correr en el servidor o en OTRA máquina con PostgreSQL (criterio 6 del PRD).
.DESCRIPTION
  Comprueba: (1) que el zip (o .enc con la frase) se abre y el volcado se lee; (2) que pg_restore termina sin errores;
  (3) que cada tabla tiene al menos las filas que tenía al respaldar; (4) que la última migración coincide.
  Escribe {Datos}\respaldos\ultima-restauracion.json (lo muestra la página de estado). Código de salida 0 = prueba correcta.
.EXAMPLE
  .\Probar-Restauracion.ps1                              # último respaldo local
  .\Probar-Restauracion.ps1 -Archivo E:\copia3\parroquia-20261110-2200.zip.enc   # copia fuera de sitio (pide la frase)
#>
[CmdletBinding()]
param(
    [string]$Archivo,
    [securestring]$Frase,
    [string]$ConfigRuta
)

. (Join-Path $PSScriptRoot 'Comun.ps1')
$cfg = Cargar-Configuracion $ConfigRuta
$r = Rutas-Parroquia $cfg
Iniciar-Log $r 'restauracion.log'
Configurar-Pg $cfg $r

if (-not $Archivo) {
    $ultimo = Get-ChildItem -LiteralPath $r.Archivos -Filter 'parroquia-*.zip' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $ultimo) { throw "No hay respaldos en $($r.Archivos). Indique -Archivo." }
    $Archivo = $ultimo.FullName
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('parroquia-prueba-' + [guid]::NewGuid().ToString('N'))
$baseTemp = 'parroquia_prueba_' + (Get-Date -Format 'yyyyMMddHHmmss')
$marcador = [ordered]@{ fecha = (Get-Date).ToString('o'); resultado = 'error'; mensaje = ''; archivo = (Split-Path $Archivo -Leaf); tablas = 0; filas = 0; maquina = $env:COMPUTERNAME }
$codigo = 0
$baseCreada = $false

try {
    Escribir-Log "Prueba de restauración de $Archivo en la base temporal $baseTemp"
    New-Item -ItemType Directory -Force -Path $temp | Out-Null

    $zip = $Archivo
    if ($Archivo -like '*.enc') {
        if (-not $Frase) { $Frase = Leer-Frase $r }
        if (-not $Frase) { $Frase = Read-Host 'Frase de cifrado de la copia 3' -AsSecureString }
        $zip = Join-Path $temp 'respaldo.zip'
        Desproteger-Archivo $Archivo $zip $Frase
        Escribir-Log 'Archivo descifrado y verificado.'
    }

    $contenido = Join-Path $temp 'contenido'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $contenido)
    $dump = Join-Path $contenido 'base.dump'
    $manifiestoRuta = Join-Path $contenido 'manifiesto.json'
    if (-not (Test-Path -LiteralPath $dump) -or -not (Test-Path -LiteralPath $manifiestoRuta)) { throw 'El respaldo no trae base.dump o manifiesto.json.' }
    $manifiesto = Get-Content -LiteralPath $manifiestoRuta -Raw -Encoding UTF8 | ConvertFrom-Json

    Nueva-Base $cfg $baseTemp
    $baseCreada = $true
    Restaurar-Dump $cfg $baseTemp $dump
    Escribir-Log 'pg_restore terminó sin errores.'

    $restaurado = Contar-Tablas $cfg $baseTemp
    $problemas = @()
    $totalFilas = 0
    foreach ($prop in $manifiesto.conteos.PSObject.Properties) {
        $esperado = [long]$prop.Value
        if (-not $restaurado.Contains($prop.Name)) { $problemas += "falta la tabla $($prop.Name)"; continue }
        $real = [long]$restaurado[$prop.Name]
        $totalFilas += $real
        if ($real -lt $esperado) { $problemas += "$($prop.Name): $real filas (se respaldaron $esperado)" }
    }
    $marcador.tablas = @($manifiesto.conteos.PSObject.Properties).Count
    $marcador.filas = $totalFilas

    $migracion = Ultima-Migracion $cfg $baseTemp
    if ($migracion -ne $manifiesto.migracion) { $problemas += "migración $migracion (se respaldó $($manifiesto.migracion))" }

    if ($problemas.Count -gt 0) { throw ('La base restaurada no coincide: ' + ($problemas -join '; ')) }

    $marcador.resultado = 'ok'
    Escribir-Log ("PRUEBA CORRECTA: {0} tablas, {1} filas, migración {2}." -f $marcador.tablas, $totalFilas, $migracion)
} catch {
    $marcador.mensaje = $_.Exception.Message
    Escribir-Log "PRUEBA FALLÓ: $($marcador.mensaje)" 'ERROR'
    $codigo = 1
} finally {
    if ($baseCreada) { try { Borrar-Base $cfg $baseTemp } catch { Escribir-Log "No se pudo borrar la base temporal $baseTemp." 'AVISO' } }
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
    try {
        New-Item -ItemType Directory -Force -Path $r.Respaldos | Out-Null
        Escribir-Json (Join-Path $r.Respaldos 'ultima-restauracion.json') $marcador
    } catch { Escribir-Log "No se pudo escribir ultima-restauracion.json: $($_.Exception.Message)" 'AVISO' }
}
exit $codigo
