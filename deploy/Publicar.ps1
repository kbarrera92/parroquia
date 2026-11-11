<#
.SYNOPSIS
  Genera el paquete de instalación (equipo de desarrollo): pruebas → dotnet publish self-contained win-x64 → zip + sha256.
.DESCRIPTION
  Resultado en publish\<versión>\: Parroquia-<versión>.zip (la app), su .sha256 y una copia de deploy\ para llevar al servidor.
  El servidor NO necesita instalar .NET (el paquete lo incluye).
#>
[CmdletBinding()]
param([switch]$OmitirPruebas)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz

[xml]$props = Get-Content -LiteralPath (Join-Path $raiz 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
if (-not $version) { throw 'Directory.Build.props no define <Version>.' }
$commit = 'sin-git'
try { $c = (& git rev-parse --short HEAD) 2>$null; if ($LASTEXITCODE -eq 0 -and $c) { $commit = $c.Trim() } } catch { }

if (-not $OmitirPruebas) {
    Write-Host 'Pruebas (necesitan Docker en marcha)...'
    & dotnet test --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet test falló: no se publica.' }
}

$salida = Join-Path $raiz "publish\$version"
$app = Join-Path $salida 'app'
Remove-Item -LiteralPath $salida -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $app | Out-Null

& dotnet publish src\Parroquia.Web -c Release -r win-x64 --self-contained true -o $app `
    -p:SourceRevisionId=$commit -p:DebugType=none --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falló.' }
Remove-Item -LiteralPath (Join-Path $app 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue

$zip = Join-Path $salida "Parroquia-$version.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ASCII
Remove-Item -LiteralPath $app -Recurse -Force

Copy-Item -LiteralPath (Join-Path $raiz 'deploy') -Destination (Join-Path $salida 'deploy') -Recurse
Remove-Item -LiteralPath (Join-Path $salida 'deploy\pruebas') -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host "Paquete listo: $zip  (commit $commit)"
Write-Host "  sha256: $hash"
Write-Host "Lleve la carpeta $salida al servidor (USB) y siga docs\operacion.md."
