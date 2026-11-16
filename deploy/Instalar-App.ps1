<#
.SYNOPSIS
  Instala la aplicación como servicio de Windows (arranque automático) con HTTPS y firewall limitado a la red local.
.DESCRIPTION
  Requisitos previos: Instalar-PostgreSQL.ps1, Nueva-CA.ps1 y Nuevo-CertificadoServidor.ps1 ya ejecutados.
  Pasos: verifica el paquete (sha256) → crea carpetas con permisos mínimos → escribe appsettings.Production.json →
  crea el servicio (cuenta virtual NT SERVICE\ParroquiaWeb) → permiso a la clave del certificado → firewall →
  primer arranque (migra la base y crea el usuario administrador) → registra el respaldo diario.
.PARAMETER Paquete
  Parroquia-<versión>.zip generado por Publicar.ps1 (junto a su .sha256).
.PARAMETER SinAdministrador
  Reinstalación sobre una base que ya tiene administrador: no pide contraseña de admin.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Paquete,
    [switch]$SinAdministrador,
    [string]$ConfigRuta
)

. (Join-Path $PSScriptRoot 'Comun.ps1')
Exigir-Administrador
$cfg = Cargar-Configuracion $ConfigRuta
$r = Rutas-Parroquia $cfg
Iniciar-Log $r 'instalacion.log'
$nombreServicio = $cfg.Servicio.Nombre
$cuenta = "NT SERVICE\$nombreServicio"

if (Get-Service -Name $nombreServicio -ErrorAction SilentlyContinue) { throw "El servicio $nombreServicio ya existe. Para actualizar use Actualizar-App.ps1." }
if (-not (Test-Path -LiteralPath $r.PgPass)) { throw 'Falta pgpass.conf: ejecute primero Instalar-PostgreSQL.ps1.' }

# 1. Verificar el paquete.
$hashArchivo = "$Paquete.sha256"
if (Test-Path -LiteralPath $hashArchivo) {
    $esperado = ((Get-Content -LiteralPath $hashArchivo -Raw) -split '\s+')[0].Trim().ToLowerInvariant()
    $real = (Get-FileHash -LiteralPath $Paquete -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($esperado -ne $real) { throw 'El paquete no coincide con su .sha256: está dañado o fue alterado.' }
    Escribir-Log 'Paquete verificado (sha256).'
} else { Escribir-Log "No hay ${hashArchivo}: no se pudo verificar la integridad del paquete." 'AVISO' }

# 2. Certificado del servidor presente.
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq "CN=$($cfg.Servidor.Nombre)" } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) { throw "No hay certificado CN=$($cfg.Servidor.Nombre) en LocalMachine\My. Ejecute Nuevo-CertificadoServidor.ps1." }

# 3. Carpetas (solo Administradores y SYSTEM por ahora). Windows resuelve "NT SERVICE\<nombre>" únicamente cuando el servicio
#    existe, así que los permisos del servicio se dan en el paso 6, justo después de crearlo.
Proteger-Carpeta $r.Datos
Proteger-Carpeta $r.Config
Proteger-Carpeta $r.Logs
Proteger-Carpeta $r.Claves
Proteger-Carpeta $r.Respaldos
Proteger-Carpeta $r.Archivos
Proteger-Carpeta $r.App

# 4. Binarios.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($Paquete, $r.App)
$exe = Join-Path $r.App 'Parroquia.Web.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "El paquete no contiene Parroquia.Web.exe." }

# 5. Configuración de producción (cadena de conexión + Kestrel + carpeta de datos).
$pg = $cfg.PostgreSql
$clave = ((Get-Content -LiteralPath $r.PgPass | Where-Object { $_ -match ":$($pg.Usuario):" } | Select-Object -First 1) -split ':', 5)[4]
$produccion = [ordered]@{
    ConnectionStrings = @{ Parroquia = "Host=$($pg.Host);Port=$($pg.Puerto);Database=$($pg.Base);Username=$($pg.Usuario);Password=$clave" }
    Parroquia = @{ CarpetaDatos = $r.Datos; PuertoHttps = $cfg.Servidor.PuertoHttps }   # PuertoHttps: destino de la redirección desde el puerto 80
    AllowedHosts = "$($cfg.Servidor.Nombre);localhost"
    Kestrel = @{
        Endpoints = @{
            Https = @{
                Url = "https://*:$($cfg.Servidor.PuertoHttps)"
                Certificate = @{ Subject = $cfg.Servidor.Nombre; Store = 'My'; Location = 'LocalMachine'; AllowInvalid = $false }
            }
            Http = @{ Url = "http://*:$($cfg.Servidor.PuertoHttp)" }
        }
    }
}
$destino = Join-Path $r.App 'appsettings.Production.json'
[IO.File]::WriteAllText($destino, ($produccion | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
Remove-Item -LiteralPath (Join-Path $r.App 'appsettings.Development.json') -Force -ErrorAction SilentlyContinue

# 6. Servicio de Windows.
$ruta = '"{0}"' -f $exe
& sc.exe create $nombreServicio binPath= $ruta start= delayed-auto obj= $cuenta DisplayName= 'Sistema Parroquial (San Bartolomé)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc create falló ($LASTEXITCODE)." }
& sc.exe description $nombreServicio 'Sistema parroquial: libros sacramentales y constancias.' | Out-Null
& sc.exe failure $nombreServicio reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
$dependencia = $pg.Servicio
& sc.exe config $nombreServicio depend= $dependencia | Out-Null

# Ahora que la cuenta existe: el servicio lee la app, escribe logs y claves, y solo LEE los marcadores de respaldo.
# Los archivos de respaldo (datos de personas) quedan solo para Administradores y SYSTEM: el servicio NO entra.
Proteger-Carpeta $r.Logs -Modificacion @($cuenta)
Proteger-Carpeta $r.Claves -Modificacion @($cuenta)
Proteger-Carpeta $r.Respaldos -Lectura @($cuenta)
Proteger-Carpeta $r.Archivos
Proteger-Carpeta $r.App -Lectura @($cuenta)
Dar-AccesoClave $cert $cuenta

# 7. Firewall: solo la red local, perfil privado.
foreach ($puerto in $cfg.Servidor.PuertoHttps, $cfg.Servidor.PuertoHttp) {
    $regla = "Parroquia web $puerto"
    Get-NetFirewallRule -DisplayName $regla -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $regla -Direction Inbound -Protocol TCP -LocalPort $puerto -RemoteAddress LocalSubnet `
        -Profile Private -Action Allow | Out-Null
}
Escribir-Log 'Firewall: puertos web abiertos solo a la subred local (perfil Privado). Confirme que la red de Windows esté como "Privada".'

# 8. Primer arranque: la contraseña del administrador viaja en una variable del servicio solo durante este arranque.
$claveRegistro = "HKLM:\SYSTEM\CurrentControlSet\Services\$nombreServicio"
try {
    if (-not $SinAdministrador) {
        $adm = Read-Host 'Contraseña del usuario administrador de la aplicación (12+ caracteres)' -AsSecureString
        $admTexto = ConvertTo-TextoPlano $adm
        if ($admTexto.Length -lt 12) { throw 'Use al menos 12 caracteres.' }
        Set-ItemProperty -Path $claveRegistro -Name Environment -Type MultiString -Value @("PARROQUIA_ADMIN_PASSWORD=$admTexto")
    }
    Start-Service -Name $nombreServicio
    if (-not (Esperar-Salud -Puerto $cfg.Servidor.PuertoHttps -Segundos 180)) {
        throw "La aplicación no respondió en /salud. Revise $($r.Logs) y el Visor de eventos."
    }
    Escribir-Log 'Aplicación en marcha (/salud correcto).'
} finally {
    Remove-ItemProperty -Path $claveRegistro -Name Environment -ErrorAction SilentlyContinue   # la contraseña no queda guardada
}

# 9. Respaldo diario.
& (Join-Path $PSScriptRoot 'Registrar-TareaRespaldo.ps1') -ConfigRuta $ConfigRuta

Write-Host ''
Write-Host "Instalación terminada. Abra https://$($cfg.Servidor.Nombre) desde un cliente (después de Instalar-Cliente.ps1)."
Write-Host 'Entre como administrador, cree los usuarios en Administración → Usuarios y ejecute un respaldo de prueba.'
