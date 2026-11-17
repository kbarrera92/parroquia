# Plan de liberación — Servidor de la parroquia (Windows 10/11)

> Guía paso a paso para dejar el sistema en producción. Complementa `operacion.md` (operación diaria, respaldos, emergencias) y `04-arquitectura.md` (ADR-08/09/10). Los comandos son los reales de los scripts de `deploy/`.
>
> Nota: los scripts `Instalar-PostgreSQL`, `Nueva-CA`, `Nuevo-CertificadoServidor`, `Instalar-App` e `Instalar-Cliente` no se han ensayado en un servidor real (requieren administrador). Si alguno falla, revisar el log en `C:\ProgramData\Parroquia\logs\instalacion.log` y corregir el script.

Participan dos equipos:

- **DEV:** el equipo de desarrollo. Genera el paquete.
- **SERVIDOR:** la PC de la parroquia. Todos los comandos en PowerShell **como Administrador**.

---

## Fase 0 — Antes de empezar

1. Confirmar la versión de Windows del servidor. Windows 10 perdió el soporte estándar el 14-oct-2025: verificar actualizaciones de seguridad extendidas (ESU) o pasar a Windows 11.
2. Definir la **IP fija** del servidor (o una reserva DHCP en el router).
3. Tener listos: un USB de transferencia, un USB o disco para la copia 2 y el destino de la copia 3 (fuera de sitio, cifrada).
4. Tener un sobre sellado y papel para anotar:
   - la contraseña del superusuario `postgres`;
   - la frase de cifrado de la copia 3;
   - la contraseña del usuario `admin`.
5. Descargar en DEV el instalador de **PostgreSQL 17 para Windows x64** (`postgresql-17.x-windows-x64.exe`, de postgresql.org o EDB) y copiarlo al USB.
6. Pendiente de `06`: el párroco debe confirmar que la parroquia cumple la licencia Community de QuestPDF.

---

## Fase 1 — Generar el paquete (en DEV)

Requiere Docker Desktop en marcha, porque `Publicar.ps1` corre `dotnet test`.

```powershell
cd C:\Users\PC\Desktop\Proyectos\ParroquiaMazate
docker compose up -d db
.\deploy\Publicar.ps1
```

Resultado en `publish\<versión>\`:

- `Parroquia-<versión>.zip` (la app, autocontenida: el servidor **no** necesita instalar .NET);
- `Parroquia-<versión>.zip.sha256`;
- `deploy\` (los scripts).

Copiar al USB la carpeta `publish\<versión>\` completa y el instalador de PostgreSQL.

---

## Fase 2 — Preparar el servidor (una vez)

**2.1 Energía.** Que el equipo no se duerma:

```powershell
powercfg /change standby-timeout-ac 0
powercfg /h off
```

**2.2 Ajustes manuales en Windows:**

- Windows Update: horas activas amplias (p. ej. 7:00–20:00) y reinicios fuera de horario.
- IP fija en el adaptador (o reserva DHCP).
- Red marcada como **Privada**.
- Cuenta de Windows dedicada, sin navegar ni leer correo. BitLocker si hay riesgo de robo.
- UPS conectado.

**2.3 Copiar los scripts y crear la configuración** (ajustar la letra del USB):

```powershell
New-Item -ItemType Directory -Force C:\Parroquia | Out-Null
Copy-Item E:\<versión>\deploy C:\Parroquia\deploy -Recurse
New-Item -ItemType Directory -Force C:\ProgramData\Parroquia\config | Out-Null
Copy-Item C:\Parroquia\deploy\config.ejemplo.psd1 C:\ProgramData\Parroquia\config\deploy.psd1
notepad C:\ProgramData\Parroquia\config\deploy.psd1
```

Valores a editar en `deploy.psd1`:

| Clave | Qué poner |
|---|---|
| `Servidor.Ip` | La IP fija real del servidor |
| `Respaldo.Copia2` | Carpeta del segundo disco o USB (p. ej. `'D:\RespaldosParroquia'`); vacío = sin copia 2 |
| `Respaldo.Copia3` | Carpeta de la copia fuera de sitio (USB rotado, NAS, carpeta sincronizada); vacío = sin copia 3 |
| `Respaldo.Hora` | `'22:00'` por defecto |

El resto (`parroquia.local`, puertos 443/80, rutas `C:\Parroquia\app` y `C:\ProgramData\Parroquia`, PostgreSQL 17) se deja igual. **No subir nunca el `deploy.psd1` real al repositorio.**

Si PowerShell bloquea los scripts:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

---

## Fase 3 — Instalar (en SERVIDOR, en este orden)

```powershell
cd C:\Parroquia\deploy
```

### Paso 1 — PostgreSQL

```powershell
.\Instalar-PostgreSQL.ps1 -Instalador E:\postgresql-17.x-windows-x64.exe
```

- Pide la contraseña de `postgres` (12+ caracteres): **anotarla en el sobre sellado**.
- Instala el servicio `postgresql-x64-17` y fija `listen_addresses = 'localhost'` (la base nunca se abre a la red).
- Crea el usuario `parroquia_app` (sin superusuario, con `CREATEDB`) y la base `parroquia`.
- Guarda la contraseña de la app en `config\pgpass.conf` (solo Administradores/SYSTEM).

### Paso 2 — CA local

```powershell
.\Nueva-CA.ps1
```

- Deja `ca-parroquia.cer` en el Escritorio: es lo que se entrega a los clientes.
- Deja un `.pfx` de respaldo de la CA: **llevarlo a un USB fuera del servidor y borrarlo del Escritorio.** Sirve para recuperar el servidor si se daña.

### Paso 3 — Certificado del servidor

```powershell
.\Nuevo-CertificadoServidor.ps1
```

Emite el certificado de `parroquia.local` (más la IP y el nombre del equipo). Vale 2 años.

### Paso 4 — La aplicación

```powershell
.\Instalar-App.ps1 -Paquete E:\<versión>\Parroquia-<versión>.zip
```

Qué hace:

- Verifica el sha256 del paquete.
- Crea las carpetas con permisos mínimos.
- Escribe `appsettings.Production.json`.
- Crea el servicio `ParroquiaWeb` con la cuenta virtual `NT SERVICE\ParroquiaWeb`.
- Abre el firewall en 80/443 solo a la subred local.
- Arranca la app: migra la base y crea el usuario `admin`.
- Registra la tarea de respaldo diario.

Pide:

- La contraseña de `admin` (12+ caracteres).
- La frase de cifrado de la copia 3, si está configurada. **También va al sobre sellado.**

### Paso 5 — Verificar y primer respaldo

```powershell
Get-Service ParroquiaWeb, postgresql-x64-17
Start-ScheduledTask -TaskName "Parroquia - Respaldo diario"
```

Comprobar que existan `C:\ProgramData\Parroquia\respaldos\ultimo.json` y un zip en `respaldos\archivos`. El endpoint `https://localhost/salud` debe responder `Healthy` (abrirlo en el navegador; el certificado es de la CA local).

---

## Fase 4 — Cada PC cliente

Copiar `ca-parroquia.cer` y `Instalar-Cliente.ps1` a una misma carpeta. Como Administrador:

```powershell
.\Instalar-Cliente.ps1 -Ip 192.168.1.50
```

Sustituir la IP por la real del servidor. El script instala la CA, escribe `parroquia.local` en el archivo `hosts` y crea un acceso directo. Abrir siempre `https://parroquia.local`, nunca la IP.

---

## Fase 5 — Configuración inicial (en el navegador)

Entrar como `admin` en `https://parroquia.local`. En *Administración*, en este orden:

1. **Configuración:** nombre, dirección, teléfono, logo.
2. **Párroco:** el párroco vigente.
3. **Ministros** y **Tratamientos:** catálogos.
4. **Libros digitales:** abrir uno por sacramento y uno con clave `supletoria`. Hace falta, por cada uno, el último libro, folio y partida físicos, las partidas por folio y el modo de numeración (pendiente en `06`).
5. **Usuarios:** uno por persona. La contraseña temporal se entrega en persona y se cambia al entrar. Para capturistas temporales, fijar «Acceso hasta».
6. **Perfiles:** revisar los permisos de Capturista y Secretaria.
7. **Estado del sistema:** debe quedar sin avisos.

---

## Fase 6 — Prueba de restauración en OTRA máquina (obligatoria antes de producción)

1. Copiar al USB el último respaldo. Mejor la **copia 3 cifrada**, para probar también la frase.
2. En otra PC Windows con PostgreSQL 17 instalado (`Instalar-PostgreSQL.ps1` ya ejecutado): copiar `deploy\` y una `deploy.psd1` con las rutas de esa máquina. Luego:

```powershell
.\Probar-Restauracion.ps1 -Archivo E:\parroquia-AAAAMMDD-HHmm.zip.enc
```

Debe terminar con `PRUEBA CORRECTA: N tablas, M filas`.

3. Verificación completa (recomendada):

```powershell
.\Restaurar.ps1 -Archivo E:\parroquia-AAAAMMDD-HHmm.zip.enc -RestaurarClaves
.\Instalar-App.ps1 -Paquete E:\<versión>\Parroquia-<versión>.zip -SinAdministrador
```

Abrir la app, entrar con un usuario real, buscar a una persona sintética conocida, imprimir una constancia y revisar el historial.

4. Anotar fecha, quién la hizo y el resultado en el cuaderno de operación. Si algo falló, no se considera lista la operación hasta repetirla bien.

---

## Fase 7 — Piloto y salida a producción

1. Capacitar al personal con `manual-usuario.md`.
2. Piloto de 20 registros reales por sacramento; el párroco valida las constancias.
3. Repetir las mediciones de rendimiento en el servidor real:

```powershell
$env:PARROQUIA_MEDIR = "1"; dotnet test --filter "FullyQualifiedName~BusquedaRendimiento" --logger "console;verbosity=detailed"
```

4. Si todo pasa, iniciar la captura masiva.

---

## Operación posterior (resumen)

| Cuándo | Qué | Comando |
|---|---|---|
| Cada día | Revisar *Estado del sistema* | (web) |
| Actualizar | Respalda, instala y revierte si no responde `/salud` | `.\Publicar.ps1` en DEV; `.\Actualizar-App.ps1 -Paquete C:\ruta\Parroquia-X.zip` en el servidor |
| Respaldo manual | | `.\Respaldar.ps1` |
| Cada trimestre | Prueba de restauración | `.\Probar-Restauracion.ps1` |
| Cada 2 años | Renovar el certificado (los clientes no hacen nada) | `.\Nuevo-CertificadoServidor.ps1` |
| Desastre | Reemplaza **toda** la base; pide escribir `RESTAURAR` | `.\Restaurar.ps1 -Archivo <zip> [-RestaurarClaves]` |

Emergencias (olvido de la contraseña del único administrador, certificado vencido, servicio que no arranca, etc.): `operacion.md` §7.

---

## Decisiones pendientes que afectan la instalación

- Destino real de la copia 3 y quién custodia el sobre con las frases y contraseñas.
- Versión exacta de Windows del servidor (ESU o migración).
- Datos de los libros físicos (último libro, folio, partida, partidas por folio, modo de numeración) para abrir los libros digitales.
- Confirmación de la licencia de QuestPDF Community por el párroco.
