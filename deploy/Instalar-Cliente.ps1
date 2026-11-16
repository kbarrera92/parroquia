<#
.SYNOPSIS
  Prepara una PC de la parroquia (capturista, secretaria, administrador) para abrir el sistema por HTTPS.
.DESCRIPTION
  Instala el certificado de la CA local (para que el navegador no avise), agrega el nombre del servidor al archivo hosts
  y deja un acceso directo en el escritorio. Ejecutar como Administrador, con ca-parroquia.cer en la misma carpeta.
.EXAMPLE
  .\Instalar-Cliente.ps1 -Ip 192.168.1.50
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Ip,
    [string]$Nombre = 'parroquia.local',
    [string]$CertificadoCa = (Join-Path $PSScriptRoot 'ca-parroquia.cer')
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Ejecute este script como Administrador.'
}
if (-not (Test-Path -LiteralPath $CertificadoCa)) { throw "No se encontró $CertificadoCa. Cópielo junto a este script." }

# 1. Confianza en la CA local.
Import-Certificate -FilePath $CertificadoCa -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
Write-Host 'Certificado de la parroquia instalado.'

# 2. Nombre del servidor en el archivo hosts (sin duplicar la entrada).
$hosts = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$lineas = @(Get-Content -LiteralPath $hosts | Where-Object { $_ -notmatch "(^|\s)$([regex]::Escape($Nombre))(\s|$)" })
$lineas += "$Ip`t$Nombre"
Set-Content -LiteralPath $hosts -Value $lineas -Encoding ASCII
Write-Host "Nombre registrado: $Nombre -> $Ip"

# 3. Firefox no usa el almacén de Windows: se le indica que confíe en él.
$firefox = 'C:\Program Files\Mozilla Firefox'
if (Test-Path -LiteralPath $firefox) {
    $dist = Join-Path $firefox 'distribution'
    $politicas = Join-Path $dist 'policies.json'
    if (-not (Test-Path -LiteralPath $politicas)) {
        New-Item -ItemType Directory -Force -Path $dist | Out-Null
        Set-Content -LiteralPath $politicas -Value '{"policies":{"Certificates":{"ImportEnterpriseRoots":true}}}' -Encoding ASCII
        Write-Host 'Firefox configurado para confiar en el almacén de Windows.'
    } else { Write-Warning 'Firefox ya tiene policies.json: agregue "Certificates": {"ImportEnterpriseRoots": true} a mano.' }
}

# 4. Acceso directo.
$escritorio = [Environment]::GetFolderPath('CommonDesktopDirectory')
Set-Content -LiteralPath (Join-Path $escritorio 'Sistema Parroquial.url') -Value "[InternetShortcut]`r`nURL=https://$Nombre" -Encoding ASCII
Write-Host "Acceso directo creado en el escritorio. Abra https://$Nombre"
