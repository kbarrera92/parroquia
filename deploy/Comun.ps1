# Funciones compartidas por los scripts de deploy/. Se usa con:  . (Join-Path $PSScriptRoot 'Comun.ps1')
# PowerShell 5.1 (Windows 10/11), sin módulos externos.

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Exigir-Administrador {
    # PARROQUIA_ENSAYO=1: ensayo en el equipo de desarrollo (sin servicio ni tareas reales). Nunca en el servidor.
    if ($env:PARROQUIA_ENSAYO -eq '1') { return }
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    if (-not $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Ejecute este script en una ventana de PowerShell abierta como Administrador.'
    }
}

function Cargar-Configuracion {
    param([string]$Ruta)
    if (-not $Ruta) { $Ruta = $env:PARROQUIA_DEPLOY_CONFIG }
    if (-not $Ruta) { $Ruta = Join-Path $env:ProgramData 'Parroquia\config\deploy.psd1' }
    if (-not (Test-Path -LiteralPath $Ruta)) {
        throw "No existe la configuración '$Ruta'. Copie deploy\config.ejemplo.psd1 allí y ajústela."
    }
    $cfg = Import-PowerShellDataFile -LiteralPath $Ruta
    foreach ($seccion in 'Servidor', 'Rutas', 'Servicio', 'PostgreSql', 'Respaldo') {
        if (-not $cfg.ContainsKey($seccion)) { throw "Falta la sección '$seccion' en $Ruta." }
    }
    return $cfg
}

function Rutas-Parroquia {
    param($Cfg)
    $datos = $Cfg.Rutas.Datos
    [pscustomobject]@{
        App        = $Cfg.Rutas.App
        Datos      = $datos
        Config     = Join-Path $datos 'config'
        Logs       = Join-Path $datos 'logs'
        Claves     = Join-Path $datos 'claves'
        Respaldos  = Join-Path $datos 'respaldos'          # aquí van los marcadores JSON que lee la página de estado
        Archivos   = Join-Path $datos 'respaldos\archivos' # aquí los respaldos (acceso solo de Administradores y SYSTEM)
        PgPass     = Join-Path $datos 'config\pgpass.conf'
        Frase      = Join-Path $datos 'config\frase.dat'
    }
}

# ---- Registro -------------------------------------------------------------------------------------------------------

$script:ArchivoLog = $null

function Iniciar-Log {
    param($Rutas, [string]$Nombre)
    New-Item -ItemType Directory -Force -Path $Rutas.Logs | Out-Null
    $script:ArchivoLog = Join-Path $Rutas.Logs $Nombre
}

function Escribir-Log {
    param([string]$Mensaje, [ValidateSet('INFO', 'AVISO', 'ERROR')][string]$Nivel = 'INFO')
    $linea = '{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}' -f (Get-Date), $Nivel, $Mensaje
    switch ($Nivel) {
        'ERROR' { Write-Host $linea -ForegroundColor Red }
        'AVISO' { Write-Host $linea -ForegroundColor Yellow }
        default { Write-Host $linea }
    }
    if ($script:ArchivoLog) { Add-Content -LiteralPath $script:ArchivoLog -Value $linea -Encoding UTF8 }
}

function Escribir-Json {
    # Escritura atómica y sin BOM (la página de estado lee estos marcadores).
    param([string]$Ruta, $Objeto)
    $json = $Objeto | ConvertTo-Json -Depth 6
    $tmp = "$Ruta.tmp"
    [IO.File]::WriteAllText($tmp, $json, (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmp -Destination $Ruta -Force
}

# ---- PostgreSQL ----------------------------------------------------------------------------------------------------

function Configurar-Pg {
    # Credenciales desde pgpass.conf (a menos que ya exista PGPASSWORD, p. ej. en un ensayo contra Docker).
    param($Cfg, $Rutas)
    if (-not $env:PGPASSWORD -and (Test-Path -LiteralPath $Rutas.PgPass)) { $env:PGPASSFILE = $Rutas.PgPass }
    $env:PGCLIENTENCODING = 'UTF8'
}

function Ejecutar-Pg {
    # Ejecuta una herramienta de PostgreSQL y falla si el código de salida no es 0. Devuelve la salida estándar.
    param($Cfg, [string]$Herramienta, [string[]]$Argumentos)
    $exe = Join-Path $Cfg.PostgreSql.Bin "$Herramienta.exe"
    if (-not (Test-Path -LiteralPath $exe)) { throw "No se encontró $exe (revise PostgreSql.Bin en deploy.psd1)." }
    $salida = & $exe @Argumentos
    if ($LASTEXITCODE -ne 0) { throw "$Herramienta terminó con código $LASTEXITCODE." }
    return $salida
}

function Consultar-Pg {
    param($Cfg, [string]$Base, [string]$Sql)
    $a = @('-h', $Cfg.PostgreSql.Host, '-p', [string]$Cfg.PostgreSql.Puerto, '-U', $Cfg.PostgreSql.Usuario,
           '-d', $Base, '-X', '-A', '-t', '-F', '|', '-v', 'ON_ERROR_STOP=1', '-c', $Sql)
    return @(Ejecutar-Pg $Cfg 'psql' $a)
}

function Contar-Tablas {
    # Filas por tabla (esquema public) como hashtable nombre -> conteo. Es el "contenido" que se compara al probar una restauración.
    param($Cfg, [string]$Base)
    $sql = "SELECT table_name, (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', table_schema, table_name), false, true, '')))[1]::text FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' ORDER BY 1"
    $conteos = [ordered]@{}
    foreach ($linea in (Consultar-Pg $Cfg $Base $sql)) {
        if ($linea -match '^([^|]+)\|(\d+)$') { $conteos[$Matches[1]] = [long]$Matches[2] }
    }
    return $conteos
}

function Ultima-Migracion {
    param($Cfg, [string]$Base)
    $f = @(Consultar-Pg $Cfg $Base 'SELECT * FROM migraciones_historial ORDER BY 1 DESC LIMIT 1')   # @(): una sola línea se desenrolla a texto
    if ($f.Count -eq 0) { return '' }
    return ($f[0] -split '\|')[0]
}

function Nueva-Base {
    param($Cfg, [string]$Nombre)
    Consultar-Pg $Cfg 'postgres' "CREATE DATABASE `"$Nombre`" TEMPLATE template0 ENCODING 'UTF8'" | Out-Null
}

function Borrar-Base {
    param($Cfg, [string]$Nombre)
    Consultar-Pg $Cfg 'postgres' "DROP DATABASE IF EXISTS `"$Nombre`" WITH (FORCE)" | Out-Null
}

function Restaurar-Dump {
    # --no-owner/--no-acl: todo queda a nombre del usuario de la app. --no-comments: COMMENT ON EXTENSION exige ser superusuario.
    param($Cfg, [string]$Base, [string]$Dump)
    $a = @('-h', $Cfg.PostgreSql.Host, '-p', [string]$Cfg.PostgreSql.Puerto, '-U', $Cfg.PostgreSql.Usuario,
           '-d', $Base, '--no-owner', '--no-acl', '--no-comments', '--exit-on-error', $Dump)
    Ejecutar-Pg $Cfg 'pg_restore' $a | Out-Null
}

# ---- Servicio y salud -------------------------------------------------------------------------------------------

function Esperar-Salud {
    # Espera a que /salud responda "Healthy". Acepta cualquier certificado: solo se consulta el propio equipo.
    param([int]$Puerto = 443, [int]$Segundos = 120)
    if (-not ([System.Management.Automation.PSTypeName]'ConfianzaLocal').Type) {
        Add-Type -TypeDefinition @'
public static class ConfianzaLocal {
    public static void Activar() {
        System.Net.ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
    }
}
'@
    }
    [ConfianzaLocal]::Activar()
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $limite = (Get-Date).AddSeconds($Segundos)
    while ((Get-Date) -lt $limite) {
        try {
            $r = Invoke-WebRequest -Uri "https://localhost:$Puerto/salud" -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -eq 200 -and "$($r.Content)".Trim() -eq 'Healthy') { return $true }
        } catch { }
        Start-Sleep -Seconds 3
    }
    return $false
}

# ---- Cifrado de la copia fuera de sitio ------------------------------------------------------------------------
# Formato: "PARR1" | sal(16) | IV(16) | AES-256-CBC(contenido) | HMAC-SHA256(todo lo anterior)(32).
# Claves: PBKDF2-SHA256 (200 000 iteraciones) de la frase -> 32 bytes de cifrado + 32 bytes de autenticación.

$script:MagicoCifrado = [Text.Encoding]::ASCII.GetBytes('PARR1')

function ConvertTo-TextoPlano {
    param([securestring]$Segura)
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Segura)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

function Derivar-Claves {
    param([securestring]$Frase, [byte[]]$Sal)
    $kdf = New-Object Security.Cryptography.Rfc2898DeriveBytes((ConvertTo-TextoPlano $Frase), $Sal, 200000, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $material = $kdf.GetBytes(64)
    $kdf.Dispose()
    [pscustomobject]@{ Cifrado = $material[0..31]; Mac = $material[32..63] }
}

function Proteger-Archivo {
    param([string]$Origen, [string]$Destino, [securestring]$Frase)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $sal = New-Object byte[] 16; $iv = New-Object byte[] 16
    $rng.GetBytes($sal); $rng.GetBytes($iv)
    $claves = Derivar-Claves $Frase $sal

    $aes = [Security.Cryptography.Aes]::Create()
    $aes.KeySize = 256; $aes.Mode = 'CBC'; $aes.Padding = 'PKCS7'
    $aes.Key = [byte[]]$claves.Cifrado; $aes.IV = $iv
    $enc = $aes.CreateEncryptor()
    $hmac = New-Object Security.Cryptography.HMACSHA256(, [byte[]]$claves.Mac)
    $hmac.Initialize()

    $entrada = [IO.File]::OpenRead($Origen)
    $salida = [IO.File]::Create($Destino)
    try {
        $cabecera = [byte[]]($script:MagicoCifrado + $sal + $iv)   # sin el cast, la suma de arreglos daría object[]
        $salida.Write($cabecera, 0, $cabecera.Length)
        [void]$hmac.TransformBlock($cabecera, 0, $cabecera.Length, $null, 0)

        $buffer = New-Object byte[] (1MB)   # múltiplo de 16
        $cifrado = New-Object byte[] (1MB + 32)
        while (($n = $entrada.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($n -eq $buffer.Length) {
                $m = $enc.TransformBlock($buffer, 0, $n, $cifrado, 0)
                $salida.Write($cifrado, 0, $m)
                [void]$hmac.TransformBlock($cifrado, 0, $m, $null, 0)
            } else {
                $final = $enc.TransformFinalBlock($buffer, 0, $n)
                $salida.Write($final, 0, $final.Length)
                [void]$hmac.TransformBlock($final, 0, $final.Length, $null, 0)
                $n = -1; break
            }
        }
        if ($n -eq 0) {   # tamaño múltiplo exacto del buffer (o vacío): falta el bloque de relleno
            $final = $enc.TransformFinalBlock($buffer, 0, 0)
            $salida.Write($final, 0, $final.Length)
            [void]$hmac.TransformBlock($final, 0, $final.Length, $null, 0)
        }
        [void]$hmac.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        $salida.Write($hmac.Hash, 0, 32)
    } finally {
        $entrada.Dispose(); $salida.Dispose(); $enc.Dispose(); $aes.Dispose(); $hmac.Dispose()
    }
}

function Desproteger-Archivo {
    param([string]$Origen, [string]$Destino, [securestring]$Frase)
    $info = Get-Item -LiteralPath $Origen
    $minimo = $script:MagicoCifrado.Length + 16 + 16 + 16 + 32
    if ($info.Length -lt $minimo) { throw "'$Origen' no es un archivo cifrado válido." }

    $entrada = [IO.File]::OpenRead($Origen)
    try {
        $cabecera = New-Object byte[] ($script:MagicoCifrado.Length + 32)
        [void]$entrada.Read($cabecera, 0, $cabecera.Length)
        for ($i = 0; $i -lt $script:MagicoCifrado.Length; $i++) {
            if ($cabecera[$i] -ne $script:MagicoCifrado[$i]) { throw "'$Origen' no es un archivo cifrado de la parroquia." }
        }
        $k = $script:MagicoCifrado.Length
        $sal = $cabecera[$k..($k + 15)]; $iv = $cabecera[($k + 16)..($k + 31)]
        $claves = Derivar-Claves $Frase ([byte[]]$sal)

        # 1.ª pasada: autenticar todo antes de descifrar (frase equivocada o archivo dañado).
        $hmac = New-Object Security.Cryptography.HMACSHA256(, [byte[]]$claves.Mac)
        $hmac.Initialize()
        $entrada.Position = 0
        $restante = $info.Length - 32
        $buffer = New-Object byte[] (1MB)
        while ($restante -gt 0) {
            $n = $entrada.Read($buffer, 0, [int][Math]::Min($buffer.Length, $restante))
            if ($n -le 0) { break }
            [void]$hmac.TransformBlock($buffer, 0, $n, $null, 0)
            $restante -= $n
        }
        [void]$hmac.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        $guardado = New-Object byte[] 32
        [void]$entrada.Read($guardado, 0, 32)
        $iguales = $true
        for ($i = 0; $i -lt 32; $i++) { if ($guardado[$i] -ne $hmac.Hash[$i]) { $iguales = $false } }
        $hmac.Dispose()
        if (-not $iguales) { throw 'La frase es incorrecta o el archivo está dañado: no se pudo verificar.' }

        # 2.ª pasada: descifrar el contenido.
        $aes = [Security.Cryptography.Aes]::Create()
        $aes.KeySize = 256; $aes.Mode = 'CBC'; $aes.Padding = 'PKCS7'
        $aes.Key = [byte[]]$claves.Cifrado; $aes.IV = [byte[]]$iv
        $dec = $aes.CreateDecryptor()
        $salida = [IO.File]::Create($Destino)
        try {
            $entrada.Position = $cabecera.Length
            $restante = $info.Length - 32 - $cabecera.Length
            $claro = New-Object byte[] (1MB + 32)
            while ($restante -gt 0) {
                $n = $entrada.Read($buffer, 0, [int][Math]::Min($buffer.Length, $restante))
                if ($n -le 0) { break }
                $restante -= $n
                if ($restante -gt 0) {
                    $m = $dec.TransformBlock($buffer, 0, $n, $claro, 0)
                    $salida.Write($claro, 0, $m)
                } else {
                    $final = $dec.TransformFinalBlock($buffer, 0, $n)
                    $salida.Write($final, 0, $final.Length)
                }
            }
        } finally { $salida.Dispose(); $dec.Dispose(); $aes.Dispose() }
    } finally { $entrada.Dispose() }
}

function Guardar-Frase {
    # La frase queda protegida con DPAPI (equipo): la tarea de respaldo (SYSTEM) puede usarla, un ladrón de la copia 3 no.
    param($Rutas, [securestring]$Frase)
    Add-Type -AssemblyName System.Security
    $bytes = [Text.Encoding]::UTF8.GetBytes((ConvertTo-TextoPlano $Frase))
    $prot = [Security.Cryptography.ProtectedData]::Protect($bytes, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    New-Item -ItemType Directory -Force -Path (Split-Path $Rutas.Frase) | Out-Null
    [IO.File]::WriteAllBytes($Rutas.Frase, $prot)
}

function Leer-Frase {
    param($Rutas)
    if (-not (Test-Path -LiteralPath $Rutas.Frase)) { return $null }
    Add-Type -AssemblyName System.Security
    $bytes = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($Rutas.Frase), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    $s = New-Object Security.SecureString
    foreach ($c in [Text.Encoding]::UTF8.GetString($bytes).ToCharArray()) { $s.AppendChar($c) }
    $s.MakeReadOnly()
    return $s
}

# ---- Retención -----------------------------------------------------------------------------------------------------

function Seleccionar-ArchivosAEliminar {
    # Conserva los últimos $Diarios respaldos y, además, el más antiguo de cada uno de los últimos $Mensuales meses.
    # Solo considera nombres "parroquia-AAAAMMDD-HHmm*": cualquier otro archivo nunca se toca.
    param([string[]]$Nombres, [int]$Diarios = 30, [int]$Mensuales = 12)
    $items = @()
    foreach ($n in $Nombres) {
        if ($n -match '^parroquia-(\d{8})-(\d{4})') {
            $fecha = [datetime]::ParseExact($Matches[1] + $Matches[2], 'yyyyMMddHHmm', [Globalization.CultureInfo]::InvariantCulture)
            $items += [pscustomobject]@{ Nombre = $n; Fecha = $fecha }
        }
    }
    $items = @($items | Sort-Object Fecha -Descending)
    $conservar = @{}
    foreach ($i in ($items | Select-Object -First $Diarios)) { $conservar[$i.Nombre] = $true }
    $meses = @($items | Group-Object { $_.Fecha.ToString('yyyyMM') } | Sort-Object Name -Descending | Select-Object -First $Mensuales)
    foreach ($m in $meses) {
        $primero = $m.Group | Sort-Object Fecha | Select-Object -First 1
        $conservar[$primero.Nombre] = $true
    }
    return @($items | Where-Object { -not $conservar.ContainsKey($_.Nombre) } | ForEach-Object { $_.Nombre })
}

function Aplicar-Retencion {
    param([string]$Carpeta, [int]$Diarios, [int]$Mensuales)
    if (-not (Test-Path -LiteralPath $Carpeta)) { return }
    $nombres = @(Get-ChildItem -LiteralPath $Carpeta -File | ForEach-Object { $_.Name })
    foreach ($n in (Seleccionar-ArchivosAEliminar $nombres $Diarios $Mensuales)) {
        Remove-Item -LiteralPath (Join-Path $Carpeta $n) -Force
        Escribir-Log "Retención: se eliminó $n de $Carpeta"
    }
}

# ---- Seguridad de archivos y certificados ---------------------------------------------------------------------------

function Proteger-Carpeta {
    # Quita la herencia y deja control total solo a Administradores y SYSTEM; $Lectura/$Modificacion son cuentas extra.
    param([string]$Ruta, [string[]]$Lectura = @(), [string[]]$Modificacion = @())
    New-Item -ItemType Directory -Force -Path $Ruta | Out-Null
    $acl = Get-Acl -LiteralPath $Ruta
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($r in @($acl.Access)) { [void]$acl.RemoveAccessRule($r) }
    $herencia = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    $nada = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow
    foreach ($cuenta in 'BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM') {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($cuenta, 'FullControl', $herencia, $nada, $allow)))
    }
    foreach ($cuenta in $Lectura) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($cuenta, 'ReadAndExecute', $herencia, $nada, $allow)))
    }
    foreach ($cuenta in $Modificacion) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($cuenta, 'Modify', $herencia, $nada, $allow)))
    }
    Set-Acl -LiteralPath $Ruta -AclObject $acl
}

function Dar-AccesoClave {
    # Permite a una cuenta (p. ej. NT SERVICE\ParroquiaWeb) leer la clave privada de un certificado de LocalMachine.
    param($Certificado, [string]$Cuenta)
    $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificado)
    $nombre = $null
    if ($rsa -is [Security.Cryptography.RSACng]) { $nombre = $rsa.Key.UniqueName }
    elseif ($rsa -is [Security.Cryptography.RSACryptoServiceProvider]) { $nombre = $rsa.CspKeyContainerInfo.UniqueKeyContainerName }
    if (-not $nombre) { throw 'No se pudo localizar la clave privada del certificado.' }
    $archivo = $null
    foreach ($base in "$env:ProgramData\Microsoft\Crypto\Keys", "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys") {
        $c = Join-Path $base $nombre
        if (Test-Path -LiteralPath $c) { $archivo = $c; break }
    }
    if (-not $archivo) { throw "No se encontró el archivo de la clave privada ($nombre)." }
    $acl = Get-Acl -LiteralPath $archivo
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($Cuenta, 'Read', 'Allow')))
    Set-Acl -LiteralPath $archivo -AclObject $acl
}
