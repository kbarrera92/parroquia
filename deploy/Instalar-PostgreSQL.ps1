<#
.SYNOPSIS
  Instala PostgreSQL 17 como servicio de Windows (solo escucha en este equipo) y crea la base y el usuario de la aplicación.
.DESCRIPTION
  1. Si se indica -Instalador (postgresql-17.x-windows-x64.exe, ya descargado de postgresql.org/EDB), lo instala en silencio.
  2. Configura listen_addresses = 'localhost' (la base NUNCA se abre a la red).
  3. Crea el rol de la aplicación (sin superusuario; con CREATEDB para poder probar restauraciones) y la base.
  4. Guarda la contraseña de la app en {Datos}\config\pgpass.conf (solo Administradores/SYSTEM). La contraseña del
     superusuario 'postgres' NO se guarda: anótela en un sobre sellado.
#>
[CmdletBinding()]
param(
    [string]$Instalador,
    [string]$ConfigRuta
)

. (Join-Path $PSScriptRoot 'Comun.ps1')
Exigir-Administrador
$cfg = Cargar-Configuracion $ConfigRuta
$r = Rutas-Parroquia $cfg
$pg = $cfg.PostgreSql
Proteger-Carpeta $r.Datos
Proteger-Carpeta $r.Config   # pgpass.conf, frase.dat y deploy.psd1: solo Administradores y SYSTEM

$superClave = Read-Host 'Contraseña del superusuario postgres (12+ caracteres; anótela en un sobre sellado)' -AsSecureString
$superTexto = ConvertTo-TextoPlano $superClave
if ($superTexto.Length -lt 12) { throw 'Use al menos 12 caracteres.' }

if ($Instalador) {
    if (Get-Service -Name $pg.Servicio -ErrorAction SilentlyContinue) { Write-Host "El servicio $($pg.Servicio) ya existe: no se reinstala." }
    else {
        Write-Host 'Instalando PostgreSQL (unos minutos)...'
        $opciones = @('--mode', 'unattended', '--unattendedmodeui', 'none', '--superpassword', $superTexto,
                      '--servicename', $pg.Servicio, '--serverport', [string]$pg.Puerto, '--disable-components', 'stackbuilder')
        $p = Start-Process -FilePath $Instalador -ArgumentList $opciones -Wait -PassThru
        if ($p.ExitCode -ne 0) { throw "El instalador terminó con código $($p.ExitCode)." }
    }
}

$servicio = Get-Service -Name $pg.Servicio -ErrorAction SilentlyContinue
if (-not $servicio) { throw "No existe el servicio $($pg.Servicio). Instale PostgreSQL (use -Instalador) o corrija el nombre en deploy.psd1." }

# Solo escucha en este equipo.
$datosPg = (Join-Path (Split-Path $pg.Bin -Parent) 'data')
$conf = Join-Path $datosPg 'postgresql.conf'
if (Test-Path -LiteralPath $conf) {
    $texto = Get-Content -LiteralPath $conf -Raw
    if ($texto -notmatch "(?m)^\s*listen_addresses\s*=\s*'localhost'") {
        Add-Content -LiteralPath $conf -Value "`r`n# Sistema parroquial: la base no se expone a la red`r`nlisten_addresses = 'localhost'"
        Restart-Service -Name $pg.Servicio -Force
        Start-Sleep -Seconds 5
    }
}
if ((Get-Service -Name $pg.Servicio).Status -ne 'Running') { Start-Service -Name $pg.Servicio }

# Contraseña de la app: se reutiliza si ya existe (reinstalación), si no se genera (sin caracteres que compliquen pgpass ni SQL).
$claveApp = $null
if (Test-Path -LiteralPath $r.PgPass) {
    $linea = Get-Content -LiteralPath $r.PgPass | Where-Object { $_ -match ":$($pg.Usuario):" } | Select-Object -First 1
    if ($linea) { $claveApp = ($linea -split ':', 5)[4] }
}
if (-not $claveApp) {
    $alfabeto = [char[]]('abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789')
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $claveApp = -join ($bytes | ForEach-Object { $alfabeto[$_ % $alfabeto.Length] })
}

$env:PGPASSWORD = $superTexto
$psql = Join-Path $pg.Bin 'psql.exe'
function Sql([string]$base, [string]$sql) {
    $s = & $psql -h localhost -p $pg.Puerto -U postgres -d $base -X -A -t -v ON_ERROR_STOP=1 -c $sql
    if ($LASTEXITCODE -ne 0) { throw "psql falló: $sql" }
    return $s
}
try {
    $existe = Sql 'postgres' "SELECT 1 FROM pg_roles WHERE rolname = '$($pg.Usuario)'"
    if ($existe) { Sql 'postgres' "ALTER ROLE $($pg.Usuario) WITH LOGIN CREATEDB PASSWORD '$claveApp'" | Out-Null }
    else { Sql 'postgres' "CREATE ROLE $($pg.Usuario) WITH LOGIN CREATEDB PASSWORD '$claveApp'" | Out-Null }

    $hayBase = Sql 'postgres' "SELECT 1 FROM pg_database WHERE datname = '$($pg.Base)'"
    if (-not $hayBase) { Sql 'postgres' "CREATE DATABASE $($pg.Base) OWNER $($pg.Usuario) TEMPLATE template0 ENCODING 'UTF8'" | Out-Null }
} finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }

"$($pg.Host):$($pg.Puerto):*:$($pg.Usuario):$claveApp" | Set-Content -LiteralPath $r.PgPass -Encoding ASCII
Write-Host "Listo: base '$($pg.Base)' y usuario '$($pg.Usuario)'. Credenciales en $($r.PgPass) (solo Administradores)."
Write-Host 'Siguiente: Nueva-CA.ps1 → Nuevo-CertificadoServidor.ps1 → Instalar-App.ps1'
