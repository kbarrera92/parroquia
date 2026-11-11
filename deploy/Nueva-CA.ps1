<#
.SYNOPSIS
  Crea la autoridad certificadora (CA) LOCAL de la parroquia. Se ejecuta UNA vez en el servidor.
.DESCRIPTION
  Produce ca-parroquia.cer (certificado público: se instala en cada PC cliente con Instalar-Cliente.ps1) y un .pfx de respaldo
  de la CA protegido con contraseña (guárdelo FUERA del servidor, p. ej. en un USB en el sobre sellado). La CA vale 10 años y
  solo sirve para firmar el certificado del servidor: renovar ese certificado NO exige tocar los clientes.
#>
[CmdletBinding()]
param(
    [string]$ConfigRuta,
    [string]$CarpetaSalida = (Join-Path $env:USERPROFILE 'Desktop')
)

. (Join-Path $PSScriptRoot 'Comun.ps1')
Exigir-Administrador
$cfg = Cargar-Configuracion $ConfigRuta
$asunto = 'CN=Parroquia San Bartolome - CA local'

if (Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq $asunto }) {
    throw 'La CA ya existe en este equipo. Para emitir el certificado del servidor use Nuevo-CertificadoServidor.ps1.'
}

$ca = New-SelfSignedCertificate -Type Custom -Subject $asunto -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable -KeyUsage CertSign, CRLSign -KeyUsageProperty All `
    -TextExtension @('2.5.29.19={critical}{text}CA=true&pathlength=0') `
    -NotAfter (Get-Date).AddYears(10) -CertStoreLocation Cert:\LocalMachine\My

$cer = Join-Path $CarpetaSalida 'ca-parroquia.cer'
Export-Certificate -Cert $ca -FilePath $cer | Out-Null
# El propio servidor debe confiar en su CA (Kestrel valida la cadena del certificado del servidor).
Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null

$clave = Read-Host 'Contraseña para el respaldo de la CA (.pfx)' -AsSecureString
$pfx = Join-Path $CarpetaSalida 'ca-parroquia-RESPALDO.pfx'
Export-PfxCertificate -Cert $ca -FilePath $pfx -Password $clave | Out-Null

Write-Host ''
Write-Host "CA creada. Huella: $($ca.Thumbprint)"
Write-Host "  Para los clientes : $cer   (copiar a cada PC junto con Instalar-Cliente.ps1)"
Write-Host "  Respaldo de la CA : $pfx   (llevar a un USB guardado fuera del servidor y borrar de aquí)"
Write-Host 'Siguiente: Nuevo-CertificadoServidor.ps1'
