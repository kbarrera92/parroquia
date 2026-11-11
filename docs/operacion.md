# Operación del servidor — Sistema Parroquial

> Para quien instala y mantiene el servidor (el desarrollador o la persona que él designe). Decisiones de fondo: `04-arquitectura.md` ADR-08, ADR-09 y ADR-10. Todos los scripts están en `deploy/` y se ejecutan en **PowerShell como Administrador**.

## 1. Cómo queda instalado

```
 Red local de la parroquia (sin acceso desde internet)
 ┌──────────── PC servidor (Windows 10/11, 24/7, UPS, IP fija) ────────────┐
 │  Servicio «ParroquiaWeb»  (Kestrel, .NET incluido en el paquete)        │
 │     :443 HTTPS (certificado firmado por la CA local)                    │
 │     :80  redirige a HTTPS                                               │
 │  Servicio «postgresql-x64-17»  (solo escucha en localhost)              │
 │  Tarea «Parroquia - Respaldo diario»  22:00 → deploy\Respaldar.ps1      │
 │                                                                         │
 │  C:\Parroquia\app\            binarios (app.anterior = versión previa)  │
 │  C:\Parroquia\deploy\         estos scripts                             │
 │  C:\ProgramData\Parroquia\                                              │
 │      config\    deploy.psd1, pgpass.conf, frase.dat   (solo Admins)     │
 │      claves\    claves de sesión/formularios (Data Protection)          │
 │      logs\      parroquia-AAAAMMDD.log, respaldo.log, instalacion.log   │
 │      respaldos\ ultimo.json, ultima-restauracion.json (los lee la web)  │
 │      respaldos\archivos\   los respaldos (solo Admins/SYSTEM)           │
 └─────────────────────────────────────────────────────────────────────────┘
      ▲  https://parroquia.local  (navegador)
 Capturista 1 · Capturista 2 · Capturista 3 · Secretaria (+ impresora) · Administrador
```

- **Usuarios:** 2–3 capturistas, una secretaria y el administrador. Cada quien con su usuario. Es una carga mínima para el servidor; no hay nada que dimensionar aparte del disco y la RAM habituales (SSD, ≥ 8 GB).
- **Concurrencia:** si dos personas editan el mismo registro, la segunda recibe un aviso de conflicto sin perder lo digitado. Los asientos nuevos (libro digital) se numeran con bloqueo en la base: nunca se repite una partida aunque dos capturistas guarden a la vez.
- **Seguridad de red:** PostgreSQL **no** escucha en la red. El firewall abre 80 y 443 solo a la subred local y solo en el perfil de red *Privada*. El servicio corre con una cuenta virtual (`NT SERVICE\ParroquiaWeb`), sin contraseña ni privilegios de administrador, y **no puede leer** los archivos de respaldo.
- **Nombre:** los clientes abren `https://parroquia.local`. Ese nombre lo resuelve el archivo `hosts` de cada PC (lo escribe `Instalar-Cliente.ps1`), así que no depende del router.
- **Certificado:** una CA propia de la parroquia (10 años) firma el certificado del servidor (2 años). Cada PC cliente confía una sola vez en la CA; renovar el certificado del servidor no obliga a tocar los clientes.

## 2. Preparar el equipo (una vez)

1. Windows 10/11 actualizado. Windows 10 dejó de tener soporte estándar el 14-oct-2025: confirmar actualizaciones de seguridad extendidas o pasar a Windows 11 (`06` — pendiente).
2. Cuenta de Windows dedicada, sin navegar ni leer correo. BitLocker si el equipo puede ser robado.
3. **Que no se duerma:** `powercfg /change standby-timeout-ac 0` y `powercfg /h off` (hibernación).
4. **Windows Update:** horas activas amplias (p. ej. 7:00–20:00) y reinicios fuera de horario. El servicio arranca solo tras un reinicio (inicio automático retrasado, depende de PostgreSQL).
5. **IP fija** (o reserva DHCP en el router) y red marcada como **Privada**.
6. **UPS** conectado y, si es posible, apagado ordenado configurado. Segundo disco o USB para la copia 2.
7. Copiar la carpeta `deploy\` a `C:\Parroquia\deploy\` y crear la configuración:
   ```powershell
   New-Item -ItemType Directory -Force C:\ProgramData\Parroquia\config | Out-Null
   Copy-Item C:\Parroquia\deploy\config.ejemplo.psd1 C:\ProgramData\Parroquia\config\deploy.psd1
   notepad C:\ProgramData\Parroquia\config\deploy.psd1     # IP, rutas, copias 2 y 3
   ```
   Si PowerShell bloquea los scripts: `Set-ExecutionPolicy -Scope Process Bypass`.

## 3. Instalar (primera vez)

En el **equipo de desarrollo**: `.\deploy\Publicar.ps1` genera `publish\<versión>\` (zip de la app + `.sha256` + `deploy\`). Llevarlo al servidor por USB. Luego, en el **servidor**, en este orden:

| Paso | Comando | Qué hace |
|---|---|---|
| 1 | `.\Instalar-PostgreSQL.ps1 -Instalador C:\ruta\postgresql-17.x-windows-x64.exe` | Instala PostgreSQL (solo localhost), crea el usuario `parroquia_app` y la base. Pide la contraseña del superusuario `postgres`: **anótela en el sobre sellado**. |
| 2 | `.\Nueva-CA.ps1` | Crea la CA local. Deja `ca-parroquia.cer` (para los clientes) y un `.pfx` de respaldo de la CA: **llévelo a un USB fuera del servidor y bórrelo del escritorio**. |
| 3 | `.\Nuevo-CertificadoServidor.ps1` | Emite el certificado de `parroquia.local` (+ IP y nombre del equipo). |
| 4 | `.\Instalar-App.ps1 -Paquete C:\ruta\Parroquia-1.0.0.zip` | Crea carpetas y permisos, `appsettings.Production.json`, el servicio, el firewall; arranca (migra la base y crea el usuario `admin`; pide su contraseña de 12+ caracteres) y registra el respaldo diario (pide la frase de cifrado si hay copia 3). |
| 5 | En **cada PC cliente**: `.\Instalar-Cliente.ps1 -Ip <IP del servidor>` con `ca-parroquia.cer` en la misma carpeta | Instala la CA, el nombre en `hosts` y un acceso directo. |

Después, desde un cliente: abrir `https://parroquia.local`, entrar como `admin`, y en *Administración*: **Configuración** (nombre, dirección, teléfono, logo), **Párroco**, **Ministros** y **Tratamientos**, **Libros digitales** y **Usuarios** (un usuario por persona; la contraseña temporal se entrega en persona y se cambia al entrar). Revisar **Estado del sistema**.

## 4. Actualizar

```powershell
.\Publicar.ps1                                  # en desarrollo (corre las pruebas)
.\Actualizar-App.ps1 -Paquete C:\ruta\Parroquia-1.1.0.zip    # en el servidor
```
Verifica el paquete, hace un respaldo `previo-actualizacion`, detiene el servicio, deja la versión anterior en `C:\Parroquia\app.anterior`, instala la nueva (las migraciones de la base corren al arrancar) y espera a que `/salud` responda. Si no responde, devuelve los binarios anteriores. **Si la versión nueva alcanzó a migrar la base**, volver a los binarios viejos no basta: restaurar el respaldo `previo-actualizacion` (sección 6). Actualizar fuera del horario de captura.

## 5. Respaldos

- **Qué incluye** cada `parroquia-AAAAMMDD-HHmm.zip`: volcado de la base (`pg_dump`, verificado con `pg_restore --list`), `claves\` (para que las sesiones sobrevivan a una reinstalación), `appsettings.Production.json` y un `manifiesto.json` (filas por tabla y última migración, para la prueba de restauración).
- **Cuándo:** todos los días a las 22:00 (si el equipo estaba apagado, al encender). Manual: `Start-ScheduledTask -TaskName "Parroquia - Respaldo diario"` o `.\Respaldar.ps1`.
- **Dónde (regla 3-2-1):**
  1. `C:\ProgramData\Parroquia\respaldos\archivos` (en el mismo equipo).
  2. **Copia 2** (`Respaldo.Copia2`): segundo disco o USB, en claro.
  3. **Copia 3** (`Respaldo.Copia3`): fuera de sitio, **cifrada** (`.zip.enc`, AES-256 + HMAC). Puede ser un USB que rota cada semana y se lleva fuera, un NAS o una carpeta sincronizada. Mientras no se decida el destino real queda pendiente en `06`.
- **Retención:** los últimos 30 respaldos más el más antiguo de cada uno de los últimos 12 meses, en cada carpeta. Los archivos que no se llamen `parroquia-…` nunca se borran.
- **La frase de la copia 3** vive protegida en `config\frase.dat` (solo la lee el equipo) y **debe estar también en un sobre sellado** fuera del servidor: sin ella la copia 3 no se puede abrir.
- **Vigilancia:** *Administración → Estado del sistema* avisa si el último respaldo falló o tiene más de 36 horas, si no hay prueba de restauración reciente, si queda poco disco y si el certificado vence en menos de 30 días. Revisarla cada día.

## 6. Restaurar

Dos operaciones distintas:

**a) Probar un respaldo (no toca producción)** — `Probar-Restauracion.ps1`:
```powershell
.\Probar-Restauracion.ps1                                   # el último respaldo local
.\Probar-Restauracion.ps1 -Archivo E:\copia3\parroquia-20261110-2200.zip.enc   # la copia fuera de sitio (pide la frase)
```
Restaura en una base temporal `parroquia_prueba_<fecha>`, compara **cada tabla** (debe tener al menos las filas respaldadas) y la última migración, borra la base temporal y escribe `ultima-restauracion.json` (lo muestra *Estado del sistema*). Código de salida 0 = correcta.

**b) Recuperarse de un desastre o de un error grave** — `Restaurar.ps1` (¡reemplaza TODA la base!):
```powershell
.\Restaurar.ps1 -Archivo C:\ProgramData\Parroquia\respaldos\archivos\parroquia-20261110-2200.zip
.\Restaurar.ps1 -Archivo E:\copia3\parroquia-20261110-2200.zip.enc -RestaurarClaves   # equipo nuevo
```
Pide escribir `RESTAURAR`, hace antes un respaldo de seguridad de lo que hay hoy, detiene el servicio, recrea la base, restaura, vuelve a arrancar y espera `/salud`. Lo digitado después del respaldo se pierde: avisar a los capturistas qué deben volver a digitar.

**Equipo nuevo (el servidor se dañó):** instalar Windows y PostgreSQL con los pasos 1–4 de la sección 3 (misma IP y mismo nombre; la CA se restaura desde el `.pfx` guardado: `Import-PfxCertificate` en `LocalMachine\My` y luego `Nuevo-CertificadoServidor.ps1`, así los clientes siguen confiando), y luego `Restaurar.ps1 -RestaurarClaves` con la última copia.

### Procedimiento de prueba de restauración en otra máquina (criterio 6 del PRD)

Se hace **antes de poner el sistema en producción** y después cada trimestre. Objetivo: demostrar que un respaldo abre con todos sus datos en un equipo distinto del servidor.

1. Tener un respaldo reciente del servidor y copiarlo (mejor la **copia 3 cifrada**, para probar también la frase) a un USB.
2. En la otra máquina (Windows con PostgreSQL 17 instalado y el usuario de la app creado con `Instalar-PostgreSQL.ps1`): copiar `deploy\` y una `deploy.psd1` con las rutas de esa máquina.
3. Ejecutar `.\Probar-Restauracion.ps1 -Archivo <USB>\parroquia-….zip.enc`. Debe terminar con *PRUEBA CORRECTA: N tablas, M filas*.
4. (Verificación completa) Con `.\Restaurar.ps1 -Archivo … -RestaurarClaves` dejar la base en esa máquina, instalar la app con `Instalar-App.ps1 -SinAdministrador` y abrirla: entrar con un usuario real, buscar a una persona sintética conocida, abrir su detalle e imprimir una constancia (PDF) y revisar el historial.
5. Anotar fecha, quién la hizo y resultado en el cuaderno de operación; si algo falló, no se considera lista la operación hasta repetirla bien.

## 7. Emergencias

| Situación | Qué hacer |
|---|---|
| **El único administrador olvidó su contraseña** | En el servidor, con el servicio detenido: definir `PARROQUIA_ADMIN_RESTABLECER=1` y `PARROQUIA_ADMIN_PASSWORD=<nueva>` (y `PARROQUIA_ADMIN_USUARIO` si no es `admin`) en `HKLM\SYSTEM\CurrentControlSet\Services\ParroquiaWeb\Environment` (multi-cadena), iniciar el servicio, entrar, y **quitar la variable** y reiniciar. Queda en la bitácora. |
| **El certificado va a vencer / venció** | `.\Nuevo-CertificadoServidor.ps1` (reinicia el servicio). Los clientes no necesitan nada. |
| **Un navegador avisa «conexión no privada»** | El cliente no tiene la CA o el `hosts`: repetir `Instalar-Cliente.ps1`. Verificar que escriba `https://parroquia.local` (no la IP). |
| **El servicio no arranca** | `Get-Service ParroquiaWeb`; revisar `C:\ProgramData\Parroquia\logs\parroquia-*.log` y el Visor de eventos. Causas comunes: PostgreSQL detenido, certificado no encontrado o sin permiso, puerto 443 ocupado. |
| **Disco lleno** | Revisar `respaldos\archivos` y `logs\` (los logs rotan y se guardan 60 días); la copia 1 nunca debería llenar el disco con la retención por defecto. |
| **Se apagó la luz** | Al volver, el servicio arranca solo. Revisar *Estado del sistema* y que el respaldo de esa noche se haya hecho. |
| **Sospecha de acceso indebido** | *Administración → Historial y bitácora* (inicios de sesión, vistas, impresiones, cambios). Desactivar al usuario y restablecer contraseñas. |

## 8. Calendario

| Frecuencia | Tarea |
|---|---|
| Cada día | Mirar *Estado del sistema* (respaldo, disco, certificado). Cambiar el USB de la copia 3 si rota. |
| Cada semana | Llevar fuera de sitio la copia 3 (si es USB rotado). |
| Cada trimestre | **Prueba de restauración** (sección 6) y revisar usuarios activos. |
| Cada 2 años | Renovar el certificado del servidor (la página de estado avisa 30 días antes). |
| Cada 10 años | Renovar la CA (reinstalar su `.cer` en todos los clientes). |
| Al actualizar | Sección 4; después, comprobar una búsqueda y una impresión. |

## 9. Rendimiento

Las mediciones de `04` («Rendimiento de la búsqueda global» y «de los listados») se hicieron en el equipo de desarrollo con 200 000 filas sintéticas; todas quedaron muy por debajo del objetivo (< 1 s). Pendiente repetirlas en el servidor real cuando esté instalado y con la base cargada (`06`).
