# Arquitectura (propuesta) — Sistema Parroquial

> ✅ = decidido · 🟡 = pendiente de confirmar. Registro de decisiones en formato corto (ADR).

## ADR-01 Aplicación web monolítica en servidor local ✅
Un solo proceso ASP.NET Core que sirve la UI y accede a la base de datos. Sin microservicios, sin SPA separada. Dos usuarios concurrentes; la prioridad es robustez y mantenimiento simple.

## ADR-02 .NET (LTS) + ASP.NET Core Razor Pages + htmx ✅
- *Razor Pages* con HTML renderizado en servidor + htmx puntual para búsqueda en vivo y colecciones dinámicas (padrinos/testigos). Pocas partes móviles, formularios nativos, fácil de depurar.
- Descartado: Blazor Server (dependencia de conexión SignalR y más estado). No mezclar enfoques.
- Verificar que la versión LTS vigente sea la usada al iniciar (`dotnet --list-sdks`). **Usado: .NET 10 (SDK 10.0.403)**, fijado en `global.json`.

## ADR-03 Base de datos: PostgreSQL ✅
**Decidido: PostgreSQL** (gratuito, sin límite práctico de tamaño, `unaccent` + `pg_trgm` resuelven la búsqueda sin tildes y parcial con índices, `pg_dump` simple de automatizar).
**SQL Server** es viable (Express: 10 GB por base, sin SQL Agent; búsqueda sin tildes con *collation* `AI`, parcial con índices full-text o `LIKE` con costo). Elegir SQL Server solo si la parroquia ya tiene licencia/experiencia local de soporte.
El acceso va por EF Core; el resto del código no depende del motor salvo la búsqueda normalizada (aislada en un servicio).

## ADR-04 Acceso a datos: EF Core + migraciones
- Migraciones versionadas en el repo; cada despliegue aplica migraciones.
- *Complex types* para `FechaParcial` (con columna generada `*_clave_orden`) y para `Contrayente` (Matrimonio; **no** owned: un owned type es otra entrada del ChangeTracker y el interceptor, que solo revisa entidades `IAuditable`, no vería sus cambios). El interceptor audita un complex type de varios campos **campo por campo** con clave `"{Propiedad}.{Campo}"` (p. ej. `Esposo.Nombres`) y `FechaParcial` como texto de libro. Ids por secuencia HiLo en entidades auditables.
- **Interceptor `SaveChanges`** genera `Auditoria` y rellena `CreadoPor/ModificadoPor` desde el usuario actual. Ningún código de negocio escribe auditoría a mano. Solo audita cambios reales de valor; `Anulado` false→true es `Anulacion` y true→false `RevertirAnulacion`; el borrado físico lanza excepción.
- **Colecciones de texto** (padrinos de Bautismo): `List<string>` mapeada a `text[]`. Asignar siempre una lista nueva al editar. El interceptor las compara por contenido y en orden (null = vacía) y las audita unidas con "; " (vacía = sin valor).
- Concurrencia optimista (`xmin` / `rowversion`).
- Filtro global EF: los `Anulado` no salen en listados, búsquedas ni constancias. El Administrador los ve con "Incluir anulados" en el listado y en el detalle (`IgnoreQueryFilters`, protegido por `Registros.RevertirAnulacion`). La búsqueda de Inicio nunca los incluye.
- Columnas **derivadas** (órdenes naturales, `BusquedaNormalizada`) se marcan con `.NoAuditar()`: se guardan pero no ensucian el historial; un cambio solo en ellas se guarda sin crear `Auditoria`.
- Paginación común: `IQueryable<T>.PaginarAsync` (`Persistencia/Paginacion`) y `PaginaResultado<T>` (Aplicacion/Comun).

## ADR-05 Identidad y autorización
- ASP.NET Core Identity (hash PBKDF2/Argon según versión), bloqueo por intentos, cookie con expiración deslizante corta, sin registro público.
- Los permisos se guardan como **claims de rol** (tipo `permiso`) y se sincronizan desde `MatrizPermisos` en cada arranque.
- Autorización por **políticas basadas en permisos** asignadas a roles; evita `if (rol == "...")` y facilita Fase 2.

| Permiso | Capturista | Secretaria | Administrador |
|---|:-:|:-:|:-:|
| `Registros.Ver` / `Buscar` | ✅ | ✅ | ✅ |
| `Registros.Crear` / `Editar` | ✅ | — | ✅ |
| `Registros.Anular` | ✅ | — | ✅ |
| `Registros.RevertirAnulacion` | — | — | ✅ |
| `Constancias.Imprimir` / `Reimprimir` | — | ✅ | ✅ |
| `Constancias.CambiarFecha` | — | — | ✅ |
| `Catalogos.Gestionar`, `Configuracion`, `Usuarios.Gestionar` | — | — | ✅ |
| `Auditoria.Ver` | — | — | ✅ |

### Autenticación y contraseñas (estrategia)
Basada en NIST SP 800-63B y OWASP ASVS, sin MFA ni correo (red local sin internet, 2–4 usuarios).

| Tema | Decisión |
|---|---|
| Longitud | 12–128 caracteres (`PoliticaContrasena`); se admiten espacios y Unicode (frases). |
| Composición y caducidad | **Sin** reglas de mayúscula/dígito/símbolo y **sin** cambio periódico (NIST lo desaconseja). |
| Lista negra | `ValidadorContrasenaComun`: lista pública de ~92 000 contraseñas comunes embebida (offline, `Identidad/contrasenas-comunes.txt`) más términos locales; compara sin mayúsculas ni tildes y sin dígitos/símbolos finales ("Contraseña2026" cae). Rechaza también las que contienen el usuario o palabras de la parroquia. |
| Hash | PBKDF2-HMAC-SHA512 (formato v3 de Identity) con 210 000 iteraciones; Identity re-hashea al iniciar sesión. |
| Bloqueo por cuenta | 5 fallos → 15 min (`Lockout`); el Administrador puede desbloquear. |
| Límite por IP | `AddRateLimiter` política `login`: solo el POST de `/Cuenta/Login`, `Seguridad:LoginPorMinuto` (10 por defecto) por IP y minuto; 429 con `Retry-After`. Contra el rociado de contraseñas que el bloqueo por cuenta no ve. |
| Enumeración | Mismo mensaje para usuario inexistente, desactivado o contraseña errónea; el motivo real solo queda en la bitácora. |
| Sesión | Cookie HttpOnly, SameSite=Strict, `Secure` siempre fuera de Development (también la de antiforgery), 30 min deslizantes. El sello de seguridad se revalida **cada minuto**: desactivar, cambiar rol, restablecer o cambiar contraseña cierra las otras sesiones. |
| Usuario desactivado | `RequireConfirmedAccount` + `ConfirmacionUsuarioActivo`: no inicia sesión aunque la contraseña sea correcta. |
| Alta y recuperación | El Administrador crea el usuario o restablece su contraseña: se genera una temporal aleatoria (16 caracteres, CSPRNG, sin ambiguos 0/O/1/l/I), se muestra **una sola vez** (TempData, página sin caché) y el usuario queda con `DebeCambiarPassword`: `FiltroCambioObligatorio` lo manda a `/Cuenta/CambiarPassword` hasta que la cambie. Entregarla en persona. |
| Cambio propio | Pide la contraseña actual; la nueva debe ser distinta; renueva la cookie y cierra otras sesiones. |
| Salvaguardas | Nadie cambia su propio rol, se desactiva ni se restablece; nunca queda el sistema sin un Administrador activo. |
| Emergencia | Si el único administrador pierde su contraseña (requiere acceso al servidor): definir `PARROQUIA_ADMIN_RESTABLECER=1` y `PARROQUIA_ADMIN_PASSWORD` y reiniciar. Restablece/activa/desbloquea al usuario (`PARROQUIA_ADMIN_USUARIO`, por defecto `admin`; lo crea si no existe; no asciende a quien no es Administrador), exige cambio y deja bitácora. **Quitar las variables después.** |
| Cabeceras | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, y CSP `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`. Por eso **no hay scripts ni manejadores de eventos en línea** (`wwwroot/js/confirmar.js` hace los avisos de confirmación; una prueba lo vigila). Los `style` en línea se permiten (vista previa de la constancia). Las respuestas PDF no llevan CSP para no afectar al visor del navegador. |
| Fuera de alcance | MFA, correo/SMS, preguntas secretas, CAPTCHA: desproporcionado para este entorno. Reconsiderar si algún día hay acceso desde fuera de la red local. |

**Bitácora total:** toda acción relevante (inicio/cierre de sesión, ver detalle, crear, editar, anular, imprimir, cambios de usuarios/permisos/catálogos) se registra con usuario, fecha-hora e IP del equipo. Los cambios de datos van en `Auditoria`; sesión, vistas e impresiones, en `BitacoraAcceso` (solo se agrega; las búsquedas no se registran). El Administrador las consulta en *Administración → Historial y bitácora* (filtros por usuario, entidad y fechas en hora de Guatemala).

## ADR-06 PDF: QuestPDF ✅ (licencia Community; confirmar elegibilidad 🟡)
Maquetación programática en C#, sin motor de navegador, determinista y rápida. **Adoptado en M3** (versión 2026.9.1, fijada en `Directory.Packages.props`) con `LicenseType.Community`, `UseSystemFonts = false` y fuentes Liberation (SIL OFL 1.1) incrustadas.
- **Licencia:** revisado `LICENSE.md` del paquete (guía v3.0, 6-jul-2026). La Community es gratuita para organizaciones de beneficio público (categoría 3) y para organizaciones con ingresos anuales < 1 M USD (categoría 6); excluye a entidades del sector público y a empresas que cotizan en bolsa. La parroquia debería calificar por ambas vías. 🟡 **Pendiente:** que el párroco confirme la elegibilidad (ver `06`); si no calificara, cambia la licencia, no el código. Producción exige cumplirla ("Production Use").
- Alternativa descartada: plantillas HTML → PDF con un motor headless (más pesado de operar).

## ADR-07 Módulo por sacramento
```
src/
  Parroquia.Web/            UI (Razor Pages), DI, autenticación
      Pages/<Sacramento>/   Index, Editar, Detalle de cada libro
      Pages/Constancias/    impresión genérica; Presentacion/ (historial, vista de constancia)
  Parroquia.Dominio/        Entidades, FechaParcial, reglas (sin dependencias externas)
      Sacramentos/          RegistroSacramental (base) + una carpeta por sacramento
  Parroquia.Aplicacion/     Casos de uso, búsqueda, constancias (modelo de documento)
      Constancias/          DocumentoConstancia, ContextoConstancia, FormatoConstancia
      Sacramentos/          IModuloSacramental, BusquedaGlobal + plantilla por sacramento
  Parroquia.Infraestructura/ EF Core, migraciones, Identity, PDF, respaldos
      Sacramentos/          ValidadorRegistro, ConfiguracionRegistroSacramental + servicio, EF y módulo por sacramento
      Constancias/          GeneradorPdfConstancia, ServicioConstancias, ConstanciaEmitida, Fuentes/
tests/
  Parroquia.Pruebas/        xUnit (dominio, constancias golden, auditoría, búsqueda)
```
Un sacramento es una **carpeta** en cada capa (no un proyecto aparte) y los **namespaces son planos por capa** (`Parroquia.Dominio.Sacramentos`, …): un namespace `…Sacramentos.Comunion` chocaría con la entidad `Comunion`.

Cada módulo implementa `IModuloSacramental` (clave, nombre, entidad, rutas, búsqueda, `Personas` (`[""]`; Matrimonio `["esposo","esposa"]`), `ResumenesAsync(ids)` para mostrar vínculos y preparación de la constancia con su plantilla) y se registra con `AddModuloSacramental<T>()`. Menú "Libros", búsqueda de Inicio (`BusquedaGlobal`, que une resultados por apellidos y nombres) y página de impresión iteran sobre los módulos registrados. Pasos para agregar uno: `CLAUDE.md`.

## ADR-08 Servidor local: PC de oficina con Windows 10/11, dedicada, 24/7 ✅
- La app corre como **servicio de Windows** (Kestrel + `UseWindowsService`), arranque automático. **PostgreSQL** instalado de forma nativa como servicio de Windows. **Sin Docker en producción** (Docker/Testcontainers solo en el equipo de desarrollo).
- **Soporte del SO:** el soporte estándar de Windows 10 terminó el 14-oct-2025; si el equipo sigue en Windows 10, confirmar que tenga actualizaciones de seguridad extendidas o migrarlo a Windows 11. 🟡 verificar versión y edición del equipo.
- Configuración del equipo: **desactivar suspensión/hibernación**; horas activas de Windows Update y reinicios fuera de horario (el servicio debe volver solo tras un reinicio); cuenta de Windows dedicada, sin uso para navegar ni correo; BitLocker si el equipo puede ser robado.
- Red: **IP fija o reserva DHCP**, firewall limitado a la red local, **no** publicar a internet. HTTPS con certificado interno (CA local) instalado en los equipos cliente y nombre fijo (ej. `https://parroquia.local`).
- **UPS** obligatorio; SSD; un segundo disco interno o externo para la copia de respaldo.
- Operación a cargo del **desarrollador**: scripts PowerShell simples (instalar, actualizar, respaldar, restaurar), documentados en `docs/operacion.md` (se crea en M8).

## ADR-09 Respaldos (3-2-1)
- **Diario automático** con el Programador de tareas de Windows (PowerShell + `pg_dump`): volcado de la base + configuración/logo, comprimido, con fecha en el nombre.
- Retención: 30 diarios + 12 mensuales.
- **Copia 2:** disco externo o segundo disco. **Copia 3:** fuera de sitio (nube cifrada o equipo de otra sede).
- **Prueba de restauración** documentada y ejecutada antes de producción y luego trimestralmente.
- Alerta visible en la página de estado si el último respaldo tiene >36 h.

## ADR-10 Observabilidad mínima
Serilog a archivo con rotación; errores sin datos personales en los logs; página de estado simple para el Administrador (versión, último respaldo, tamaño de BD).

## ADR-11 Numeración de asientos nuevos (libro digital) ✅
Los sacramentos que se celebran desde ahora nacen en el sistema y necesitan libro, folio y partida propios, sin repetidos ni huecos aunque dos capturistas guarden a la vez.
- **Libro digital** (`LibroDigital`, ver `02`): lo abre el Administrador en *Administración → Libros digitales*, continuando la numeración del último libro físico (número, folio y partida iniciales, partidas por folio, folios por libro, cómo se numera la partida: correlativa, reinicia por libro o reinicia por folio). Se llena → se cierra y abre el siguiente solo.
- **Concurrencia:** `ServicioNumeracion.AsignarAsync` exige una transacción abierta y lee el libro con `SELECT … FOR UPDATE` (hay que pedir `xmin` explícito: `SELECT *, xmin`); el bloqueo dura hasta el commit del registro, que se guarda en la **misma** transacción. Antes de releer se sueltan las instancias de `LibroDigital` ya rastreadas por el contexto (EF no las refrescaría). Cada `ServicioX` crea su `ServicioNumeracion` con el mismo `DbContext` (sin cambiar constructores ni acoplar módulos): se identifican por `Clave`.
- **Alta:** `DatosXFormulario.AsientoNuevo` → se ignoran los Libro/Folio/Partida digitados; sin libro abierto el guardado falla con un mensaje claro y no consume número. Los avisos de duplicados (persona + fecha) corren **antes** de numerar. En la página, pestañas «Transcribir de un libro físico» (predeterminada) / «Asiento nuevo (numera el sistema)» con `?modo=sistema`.
- **Reglas:** un asiento del sistema no cambia su libro/folio/partida (el servicio lo rechaza); no se puede transcribir en el número de un libro digital; anular no libera el número.
- Pendiente (ver `06`): acta/hoja impresa de cada partida para encuadernar, si la diócesis lo exige.

## Rendimiento de la búsqueda global (medido en M7)
`PARROQUIA_MEDIR=1` ejecuta `BusquedaRendimientoPruebas`: 200 000 filas sintéticas (50 000 por libro, índices ya creados, `ANALYZE`), `BusquedaGlobal` real (4 módulos + EF, sin HTTP), 1 calentamiento + 20 repeticiones, PostgreSQL 17 en Docker (equipo de desarrollo). Resultado (ms): apellido raro + nombre p50 125 / p95 167; solo apellido raro 146 / 233; apellido común (~1,7 % por libro) 160 / 191; dos palabras comunes 24 / 32; nombre común + años 58 / 68; token de 2 letras (sin trigram) 114 / 144; apellido común + sacramento + libro 5 / 6; apellido común + folio 31 / 62. Todo p95 < 250 ms (RF-06: < 1 s). Hay que repetirla en el servidor real en M8.

## Rendimiento de los listados de cada libro (medido tras M8)
`PARROQUIA_MEDIR=1` ejecuta también `Listados_con_50000_filas_por_libro_…`: 50 000 filas por libro, `BuscarAsync` sin texto (COUNT + ORDER BY + OFFSET/LIMIT 25), 1 calentamiento + 20 repeticiones. Comunión/Bautismo/Confirmación (índice `(apellidos, nombres)`): pág. 1 p50 15–20 ms, pág. 1000 ~95–105 ms, con libro o años 6 ms. Matrimonio **sin** índice de orden: pág. 1 p50 79 ms (Seq Scan + Sort), pág. 1000 292 ms. Con el índice `(esposo_apellidos, esposo_nombres)` (migración `IndiceOrdenMatrimonio`): pág. 1 p50 6,5 ms (Index Scan + Incremental Sort), pág. 1000 122 ms, años 6,6 ms. Las páginas muy avanzadas siguen costando por `OFFSET`. Repetir en el servidor real en M8.

## Pruebas
xUnit (v2) + AwesomeAssertions (fork Apache-2.0 de FluentAssertions, cuya v8 es de licencia comercial); Testcontainers (PostgreSQL) para pruebas de integración; pruebas golden de constancias; pruebas de auditoría y de fechas parciales; un conjunto pequeño de pruebas de interfaz (Playwright) solo para el flujo crítico captura→buscar→imprimir.

## Seguridad — lista corta
Cifrado de disco del servidor si hay robo físico posible; contraseñas fuertes; sin cuentas compartidas; antiforgery en formularios; cabeceras de seguridad; datos de ejemplo únicamente en desarrollo; **nunca** subir respaldos al repo (`.gitignore`).
