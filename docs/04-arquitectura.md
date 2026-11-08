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
- *Complex types* para `FechaParcial` (con columna generada `*_clave_orden`) y *owned type* para `Contrayente`. Ids por secuencia HiLo en entidades auditables.
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

Cada módulo implementa `IModuloSacramental` (clave, nombre, entidad, rutas, búsqueda y preparación de la constancia con su plantilla) y se registra con `AddModuloSacramental<T>()`. Menú "Libros", búsqueda de Inicio (`BusquedaGlobal`, que une resultados por apellidos y nombres) y página de impresión iteran sobre los módulos registrados. Pasos para agregar uno: `CLAUDE.md`.

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

## Pruebas
xUnit (v2) + AwesomeAssertions (fork Apache-2.0 de FluentAssertions, cuya v8 es de licencia comercial); Testcontainers (PostgreSQL) para pruebas de integración; pruebas golden de constancias; pruebas de auditoría y de fechas parciales; un conjunto pequeño de pruebas de interfaz (Playwright) solo para el flujo crítico captura→buscar→imprimir.

## Seguridad — lista corta
Cifrado de disco del servidor si hay robo físico posible; contraseñas fuertes; sin cuentas compartidas; antiforgery en formularios; cabeceras de seguridad; datos de ejemplo únicamente en desarrollo; **nunca** subir respaldos al repo (`.gitignore`).
