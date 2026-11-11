# Pruebas Pester (compatibles con Pester 3.4, el que trae Windows 10/11). Ejecutar:  Invoke-Pester deploy\pruebas
# No usan base de datos ni tocan el sistema: solo carpetas temporales.

$aqui = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $aqui '..\Comun.ps1')

function Nueva-Temporal {
    $d = Join-Path ([IO.Path]::GetTempPath()) ('parroquia-pester-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $d | Out-Null
    return $d
}

function Frase-Segura([string]$t) {
    $s = New-Object Security.SecureString
    foreach ($c in $t.ToCharArray()) { $s.AppendChar($c) }
    $s.MakeReadOnly(); return $s
}

Describe 'Retención de respaldos' {
    It 'conserva los últimos N y el más antiguo de cada mes reciente, y no toca archivos ajenos' {
        $nombres = @()
        # 60 días seguidos: del 1-sep al 29-oct-2026, uno por día a las 22:00
        $dia = Get-Date '2026-09-01'
        for ($i = 0; $i -lt 60; $i++) { $nombres += ('parroquia-{0:yyyyMMdd}-2200.zip' -f $dia.AddDays($i)) }
        $nombres += 'notas.txt'

        $borrar = Seleccionar-ArchivosAEliminar $nombres 30 12

        # 60 días: del 1-sep al 30-oct. Los 30 recientes son todo octubre; además queda el más antiguo de septiembre.
        ($borrar -contains 'notas.txt') | Should Be $false
        ($borrar -contains 'parroquia-20260901-2200.zip') | Should Be $false   # el más antiguo de septiembre
        ($borrar -contains 'parroquia-20261001-2200.zip') | Should Be $false   # el más antiguo de octubre (también entre los 30 recientes)
        ($borrar -contains 'parroquia-20261030-2200.zip') | Should Be $false   # el más reciente
        ($borrar -contains 'parroquia-20260902-2200.zip') | Should Be $true
        (60 - $borrar.Count) | Should Be 31
    }

    It 'distingue respaldos del mismo día con sufijo y no falla sin archivos' {
        @(Seleccionar-ArchivosAEliminar @() 30 12).Count | Should Be 0
        $borrar = Seleccionar-ArchivosAEliminar @('parroquia-20261110-2200.zip', 'parroquia-20261110-1500-previo-actualizacion.zip') 1 0
        $borrar | Should Be 'parroquia-20261110-1500-previo-actualizacion.zip'
    }

    It 'Aplicar-Retencion borra solo lo seleccionado' {
        $d = Nueva-Temporal
        try {
            foreach ($i in 1..5) { Set-Content (Join-Path $d ('parroquia-2026110{0}-2200.zip' -f $i)) 'x' }
            Set-Content (Join-Path $d 'ajeno.dat') 'x'
            Aplicar-Retencion $d 2 0 | Out-Null
            @(Get-ChildItem $d | ForEach-Object Name) | Should Be @('ajeno.dat', 'parroquia-20261104-2200.zip', 'parroquia-20261105-2200.zip')
        } finally { Remove-Item $d -Recurse -Force }
    }
}

Describe 'Cifrado de la copia fuera de sitio' {
    $d = Nueva-Temporal
    $frase = Frase-Segura 'una frase larga de prueba para el respaldo'

    It 'ida y vuelta recupera el contenido idéntico (tamaños: vacío, pequeño, múltiplo de 1 MB, y no múltiplo)' {
        foreach ($tam in 0, 10, 1MB, 2MB, (1MB + 12345)) {
            $claro = Join-Path $d "claro-$tam.bin"
            $enc = Join-Path $d "claro-$tam.enc"
            $out = Join-Path $d "claro-$tam.out"
            $bytes = New-Object byte[] $tam
            (New-Object Random 42).NextBytes($bytes)
            [IO.File]::WriteAllBytes($claro, $bytes)

            Proteger-Archivo $claro $enc $frase
            Desproteger-Archivo $enc $out $frase

            (Get-FileHash $claro -Algorithm SHA256).Hash | Should Be (Get-FileHash $out -Algorithm SHA256).Hash
        }
    }

    It 'el archivo cifrado no contiene el texto original' {
        $claro = Join-Path $d 'secreto.txt'
        Set-Content $claro 'APELLIDO-SINTETICO-DE-PRUEBA' -Encoding ASCII
        Proteger-Archivo $claro "$claro.enc" $frase
        ([IO.File]::ReadAllText("$claro.enc", [Text.Encoding]::GetEncoding(28591))) | Should Not Match 'SINTETICO'
    }

    It 'una frase equivocada falla sin producir archivo útil' {
        $claro = Join-Path $d 'f.bin'
        [IO.File]::WriteAllBytes($claro, (New-Object byte[] 1000))
        Proteger-Archivo $claro "$claro.enc" $frase
        { Desproteger-Archivo "$claro.enc" "$claro.out" (Frase-Segura 'otra frase') } | Should Throw 'incorrecta'
    }

    It 'detecta un archivo alterado' {
        $claro = Join-Path $d 'g.bin'
        [IO.File]::WriteAllBytes($claro, (New-Object byte[] 5000))
        Proteger-Archivo $claro "$claro.enc" $frase
        $b = [IO.File]::ReadAllBytes("$claro.enc")
        $b[100] = $b[100] -bxor 1
        [IO.File]::WriteAllBytes("$claro.enc", $b)
        { Desproteger-Archivo "$claro.enc" "$claro.out" $frase } | Should Throw 'dañado'
    }

    Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'Configuración' {
    It 'lee el ejemplo y trae todas las secciones' {
        $cfg = Cargar-Configuracion (Join-Path $aqui '..\config.ejemplo.psd1')
        $cfg.Servidor.Nombre | Should Be 'parroquia.local'
        $cfg.Respaldo.Diarios | Should Be 30
        $cfg.Respaldo.Mensuales | Should Be 12
        (Rutas-Parroquia $cfg).Archivos | Should Match 'respaldos\\archivos$'
    }

    It 'falla con un mensaje claro si no existe' {
        { Cargar-Configuracion 'C:\no\existe\deploy.psd1' } | Should Throw 'config.ejemplo.psd1'
    }
}
