<#
.SYNOPSIS
  Emite (o renueva) el certificado HTTPS del servidor, firmado por la CA local. Vale 2 años.
.DESCRIPTION
  SAN: el nombre fijo (p. ej. parroquia.local), el nombre del equipo, localhost y la IP. Queda en LocalMachine\My; Kestrel lo
  toma de allí por su Subject (appsettings.Production.json), así que no hay contraseñas de .pfx en archivos.
  Si el servicio ya existe, le da permiso de lectura sobre la clave privada y lo reinicia para que cargue el nuevo.
#>
[CmdletBinding()]
param([string]$ConfigRuta)

. (Join-Path $PSScriptRoot 'Comun.ps1')
Exigir-Administrador
$cfg = Cargar-Configuracion $ConfigRuta
$nombre = $cfg.Servidor.Nombre
$ip = $cfg.Servidor.Ip

$ca = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq 'CN=Parroquia San Bartolome - CA local' } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $ca) { throw 'No hay CA en este equipo. Ejecute primero Nueva-CA.ps1.' }

$san = "2.5.29.17={text}DNS=$nombre&DNS=$($env:COMPUTERNAME)&DNS=localhost&IPAddress=$ip"
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=$nombre" -Signer $ca `
    -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable `
    -KeyUsage DigitalSignature, KeyEncipherment `
    -TextExtension @($san, '2.5.29.37={text}1.3.6.1.5.5.7.3.1') `
    -NotAfter (Get-Date).AddYears(2) -CertStoreLocation Cert:\LocalMachine\My

Write-Host "Certificado emitido: CN=$nombre, vence $($cert.NotAfter.ToString('yyyy-MM-dd')). Huella: $($cert.Thumbprint)"

# Los certificados anteriores con el mismo nombre se retiran para que Kestrel no tome uno vencido.
Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq "CN=$nombre" -and $_.Thumbprint -ne $cert.Thumbprint } |
    ForEach-Object { Remove-Item -LiteralPath $_.PSPath -Force; Write-Host "Se retiró el certificado anterior ($($_.NotAfter.ToString('yyyy-MM-dd')))." }

$servicio = Get-Service -Name $cfg.Servicio.Nombre -ErrorAction SilentlyContinue
if ($servicio) {
    Dar-AccesoClave $cert "NT SERVICE\$($cfg.Servicio.Nombre)"
    Restart-Service -Name $cfg.Servicio.Nombre -Force
    if (Esperar-Salud -Puerto $cfg.Servidor.PuertoHttps) { Write-Host 'La aplicación cargó el certificado nuevo.' }
    else { Write-Warning 'La aplicación no respondió en /salud: revise los logs.' }
} else {
    Write-Host 'El servicio aún no existe; Instalar-App.ps1 dará el permiso a la clave.'
}
