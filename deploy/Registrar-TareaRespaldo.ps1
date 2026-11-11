<#
.SYNOPSIS
  Registra la tarea diaria de respaldo en el Programador de tareas de Windows (como SYSTEM).
  Si hay copia 3 configurada y aún no existe la frase de cifrado, la pide y la guarda protegida en el equipo.
.NOTES
  Guarde la frase en un sobre sellado fuera del servidor: sin ella la copia 3 no se puede abrir.
#>
[CmdletBinding()]
param([string]$ConfigRuta)

. (Join-Path $PSScriptRoot 'Comun.ps1')
Exigir-Administrador
$cfg = Cargar-Configuracion $ConfigRuta
$r = Rutas-Parroquia $cfg

if ($cfg.Respaldo.Copia3 -and -not (Test-Path -LiteralPath $r.Frase)) {
    Write-Host 'La copia 3 (fuera de sitio) se cifra. Escriba la frase de cifrado (12+ caracteres) y anótela en un sobre sellado.'
    $f1 = Read-Host 'Frase' -AsSecureString
    $f2 = Read-Host 'Repita la frase' -AsSecureString
    if ((ConvertTo-TextoPlano $f1) -ne (ConvertTo-TextoPlano $f2)) { throw 'Las frases no coinciden.' }
    if ((ConvertTo-TextoPlano $f1).Length -lt 12) { throw 'La frase debe tener al menos 12 caracteres.' }
    Guardar-Frase $r $f1
    Write-Host 'Frase guardada (protegida para este equipo).'
}

$script = Join-Path $PSScriptRoot 'Respaldar.ps1'
$argumento = "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
if ($ConfigRuta) { $argumento += " -ConfigRuta `"$ConfigRuta`"" }

$accion = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argumento
$disparador = New-ScheduledTaskTrigger -Daily -At $cfg.Respaldo.Hora
$ajustes = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 1 -RestartInterval (New-TimeSpan -Minutes 15) `
    -ExecutionTimeLimit (New-TimeSpan -Hours 2) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

Register-ScheduledTask -TaskName 'Parroquia - Respaldo diario' -Action $accion -Trigger $disparador `
    -Settings $ajustes -Principal $principal -Description 'Respaldo diario del sistema parroquial (docs/operacion.md)' -Force | Out-Null

Write-Host "Tarea registrada: todos los días a las $($cfg.Respaldo.Hora) (si el equipo estaba apagado, corre al encender)."
Write-Host 'Pruébela ahora con:  Start-ScheduledTask -TaskName "Parroquia - Respaldo diario"   y revise Administración → Estado del sistema.'
