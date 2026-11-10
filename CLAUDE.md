# CLAUDE.md — Sistema Parroquial (Catedral / Parroquia San Bartolomé, Mazatenango)

> Copiar este archivo a la **raíz del repositorio** (junto a la carpeta `docs/`). Mantenerlo corto; el detalle vive en `docs/`.

## Qué es
Aplicación web en servidor **local** (red interna) para digitalizar libros sacramentales (Bautismo, Confirmación, Comunión, Matrimonio) y reimprimir constancias. Dos capturistas. Fase 1 = captura + búsqueda + reimpresión. Fase 2 = solicitudes e ingresos. Crecerá con más módulos.

## Documentos (léelos antes de planear)
- @docs/00-estrategia.md — decisiones, principios, hitos
- @docs/01-prd-fase1.md — requisitos y criterios de aceptación
- @docs/02-modelo-de-datos.md — entidades y reglas
- @docs/03-constancias.md — texto y formato de cada constancia (+ `docs/referencias/*.jpeg`)
- @docs/04-arquitectura.md — stack y decisiones técnicas
- @docs/05-plan-implementacion.md — hitos y prompts
- @docs/06-preguntas-abiertas.md — lo no decidido. **Si una tarea depende de una pregunta abierta, pregunta; no asumas.**

## Stack
C# / .NET (LTS vigente), ASP.NET Core **Razor Pages + htmx**, EF Core + **PostgreSQL** (Npgsql), ASP.NET Core Identity, QuestPDF (licencia Community; fuentes Liberation incrustadas), xUnit + Testcontainers, Serilog. Producción: **Windows 10/11** dedicado 24/7, app como servicio de Windows, sin Docker. Roles: **Capturista** (crea/edita/anula), **Secretaria** (imprime), **Administrador** (todo).

## Comandos
Requiere Docker Desktop en marcha (PostgreSQL de desarrollo y Testcontainers). SDK fijado en `global.json` (.NET 10).
```powershell
docker compose up -d db                                  # PostgreSQL de desarrollo (localhost:5432)
$env:PARROQUIA_ADMIN_PASSWORD = "<contraseña-de-12+>"    # solo la 1.ª vez: crea el admin (usuario por defecto "admin"; cambiar con PARROQUIA_ADMIN_USUARIO). Con PARROQUIA_ADMIN_RESTABLECER=1 restablece al admin existente (emergencia; quitar después)
dotnet build
dotnet run --project src/Parroquia.Web                   # aplica migraciones y siembra roles/admin al arrancar; URL en consola (https://localhost:7155)
dotnet test                                              # unitarias + humo con Testcontainers (necesita Docker)
dotnet test --filter "FullyQualifiedName~MatrizPermisosPruebas"   # solo pruebas sin Docker
$env:PARROQUIA_PDF_SALIDA = "<carpeta>"; dotnet test --filter "FullyQualifiedName~Muestra_visual"   # deja comunion.pdf y .png para comparar con docs/referencias
dotnet ef migrations add <Nombre> -p src/Parroquia.Infraestructura -s src/Parroquia.Web -o Persistencia/Migraciones
docker compose down                                      # detener BD (datos persisten en el volumen)
```
Cadena de conexión: `ConnectionStrings:Parroquia` (dev en `appsettings.Development.json`; producción vía variable `ConnectionStrings__Parroquia`).

## Seguridad (M1)
Permisos = claims `permiso` en los roles, sincronizados con `MatrizPermisos` (Aplicacion/Seguridad) en cada arranque. Una política por permiso: usar `[Authorize(Policy = Permisos.X)]`, nunca nombres de rol. Todo requiere sesión salvo Login/AccesoDenegado/Error.
**Usuarios y autenticación (M8):** política NIST/ASVS en `docs/04` ADR-05 (12+ caracteres, lista de comunes, sin composición; PBKDF2 210k; límite de login por IP; cookie `Secure` fuera de Development; sello revalidado cada minuto). `ServicioUsuarios` (Infraestructura/Identidad) hace el alta, edición, estado y restablecimiento y escribe la bitácora; las páginas `Admin/Usuarios` solo lo llaman. Contraseña temporal = `DebeCambiarPassword` (claim `debe_cambiar_password` + `FiltroCambioObligatorio`). **Sin scripts ni manejadores de eventos en línea** (CSP `script-src 'self'`): usar archivos en `wwwroot/js` (p. ej. `data-confirmar` en un `<form>`). Pruebas de servicio sin la app: `ServidorPostgresFixture` + `AddInfraestructura` (ver `ServicioUsuariosPruebas`); las web fijan `Seguridad__LoginPorMinuto=1000` en `AppFixture`.

## Núcleo transversal (M2)
`FechaParcial`, `NormalizadorBusqueda`, `ClaveOrdenNatural` y `TextoLibro` viven en `Dominio/Comun`. Entidades que se editan: heredar `EntidadAuditable` (+ `IAnulable` si se anulan); `ConfiguracionAuditable.Aplicar` (ya invocado en `ParroquiaDbContext`) les da HiLo y `xmin`, y `MapearFechaParcial` mapea fechas. El interceptor escribe `Auditoria`; sesión/ver/imprimir van a `IBitacoraAcceso`. Tests de Postgres sin la app: `ServidorPostgresFixture` (una base nueva por prueba); con la app: `AppFixture` + `ClienteAdminAsync()`/`ClienteConRolAsync(rol)`.

## Reglas innegociables
1. **Plan antes de código** en cada hito; espera aprobación.
2. **Pruebas primero** para: constancias (golden contra los ejemplos), `FechaParcial`, auditoría, normalización de búsqueda.
3. **Nunca** datos personales reales en código, pruebas, logs, commits ni prompts. Solo datos sintéticos o los ejemplos de `docs/referencias/`.
4. **No borrar registros**: se anulan con motivo (Capturista y Administrador). Toda modificación pasa por el interceptor de auditoría (no escribir auditoría a mano) y toda acción relevante deja bitácora.
5. **El texto de las constancias vive en un solo lugar** (plantillas por módulo). No cambiar redacción sin actualizar `docs/03` y la prueba golden.
6. **Fechas parciales** (año / año-mes / completa) en todo campo de fecha de libro. Nunca usar `DateTime` para ellas.
7. **`Libro`/`Folio` son texto** (letras, "bis") con orden natural; `Partida` obligatoria. **Nombres tal cual el libro** (no cambiar mayúsculas ni tildes); la búsqueda usa `BusquedaNormalizada`.
8. **Un sacramento = un módulo autocontenido** que implementa `IModuloSacramental`. No acoplar módulos entre sí.
9. Solo red local: no añadir dependencias que requieran internet en tiempo de ejecución.
10. Español en UI, mensajes y comentarios de dominio; nombres de código en español sin tildes para el dominio (`Bautismo`, `Folio`) e inglés técnico para infraestructura.

## Convenciones
- Una rebanada vertical por commit; `dotnet test` en verde antes de commitear.
- Mensajes de commit en español, imperativo corto.
- Migraciones EF con nombre descriptivo; nunca editar una migración ya aplicada.
- Nullable reference types activados; sin `#pragma warning disable` sin justificación.

## Constancias (M3)
La plantilla de cada sacramento es una **función pura** `Render(datos, ContextoConstancia) → DocumentoConstancia` (`Aplicacion/Sacramentos`). `GeneradorPdfConstancia` (QuestPDF) solo maqueta ese modelo; `ServicioConstancias` junta membrete + párroco vigente + fecha, genera el PDF y guarda `ConstanciaEmitida` (con instantánea y hash) y la bitácora en un mismo guardado. La página `/Constancias/{clave}/{id}` sirve a **todos** los módulos: `Constancias.Imprimir` la 1.ª vez y `Reimprimir` después; cualquier aviso (dato faltante o "Revisar") exige confirmar. Las fuentes Liberation (OFL) van como recursos en `Infraestructura/Constancias/Fuentes`.

## Cómo agregar un módulo sacramental
Patrón: **Comunión** (copiar y adaptar; `X` = el sacramento). Carpeta por módulo y **namespace plano por capa** (`Parroquia.Dominio.Sacramentos`, etc.): un namespace `…Sacramentos.X` chocaría con la entidad `X`. Por lo mismo, las páginas `Pages/X/*` no nombran la entidad: usan los DTO/`VistaX` del servicio. En este orden (pruebas primero):
1. **Golden**: `tests/.../Constancias/Golden/x.txt` con el texto esperado del ejemplo de `docs/referencias/` + `XPlantillaPruebas` (fecha parcial, mayúsculas, advertencias). Actualizar `docs/03`.
2. **Plantilla**: `Aplicacion/Sacramentos/X/PlantillaX.cs` con `DatosX`, `Render` y `Advertencias` (reusar `FormatoConstancia`).
3. **Entidad**: `Dominio/Sacramentos/X/X.cs` hereda `RegistroSacramental`, implementa `TextoBusqueda()` (nombres normalizados; Matrimonio concatena ambos contrayentes) y usa `FechaParcial` para toda fecha.
4. **EF**: `Infraestructura/Sacramentos/X/ConfiguracionX.cs` (`ConfigurarRegistro("tabla")` da longitudes, índices, trigram y filtro de anulados; `MapearFechaParcial` + índice de la clave de fecha). Registrar `DbSet` y `ApplyConfiguration` en `ParroquiaDbContext` **antes** de `ConfiguracionAuditable.Aplicar`; luego `dotnet ef migrations add AgregarX`.
5. **Servicio**: `ServicioX` calcado de `ServicioComunion` (`ValidadorRegistro`, `ActualizarDerivados()`, duplicados que avisan sin bloquear, `Version` para concurrencia, anular/revertir, `BuscarAsync` con `PaginarAsync`, `DatosParaConstanciaAsync`). Columnas derivadas se marcan `.NoAuditar()`.
6. **Módulo**: `ModuloX : IModuloSacramental` (clave en minúsculas, entidad, ruta, `BuscarAsync`, `PrepararConstanciaAsync`). Registrar en `DependencyInjection`: `AddScoped<ServicioX>()` + `AddModuloSacramental<ModuloX>()`. **Menú "Libros", búsqueda de Inicio y página de impresión lo descubren solos.**
7. **Páginas**: `Pages/X/{Index,_Resultados,Editar,Detalle}` copiando las de Comunión (autorización manual con `IAuthorizationService` según crear/editar; "Guardar y nuevo"; botón de imprimir hacia `/Constancias/{clave}/{id}`).
8. **Pruebas**: servicio con `ServidorPostgresFixture` (`ServicioComunionPruebas`), web con `AppFixture` (`ComunionWebPruebas`, `ConstanciaWebPruebas`) y datos sintéticos únicos por prueba (la base web se comparte).
9. Si el libro trae más campos que la constancia, agregarlos como opcionales y reflejarlos en `docs/02`.
10. **Colecciones** (padrinos, testigos): `List<string>` → `text[]` (patrón: **Bautismo**). Formulario: filas `name="Input.X"` repetido + parcial `_FilaX` y handler `OnGetFilaXAsync` para htmx; baja con `data-quitar-fila` (`wwwroot/js/filas-dinamicas.js`). Constancia: filas "rótulo: valor" con `Parrafo.Fila` y listas con `FormatoConstancia.UnirLista`.
11. **Persona doble o varios campos por parte** (patrón: **Matrimonio**): un *complex type* (`Contrayente`, columnas `esposo_*`/`esposa_*`), nunca owned (la auditoría solo ve entidades `IAuditable`; el interceptor registra cada campo como `Esposo.Nombres`). Una columna `Busqueda*` derivada por persona (`.NoAuditar()`, GIN trigram) además de `BusquedaNormalizada`; `TextoBusqueda()` las recalcula. El módulo devuelve **una fila por persona que coincide** en `BuscarAsync`. Texto de catálogo (tratamiento) se guarda como texto. Un párrafo con partes subrayadas/negritas = `Parrafo` con varios `Segmento`; `DocumentoConstancia.LineaFirma` dibuja la raya sobre el firmante.

## Búsqueda global y vínculos (M7)
`BusquedaGlobal.BuscarAsync(FiltroGlobal)` (texto ≥2 letras obligatorio; sacramento, años, libro, folio solo acotan) pasa los filtros por `ConsultaBusqueda` a cada módulo. Vínculos "misma persona": `Dominio/Vinculos/Vinculo` + `ServicioVinculos` (Infraestructura/Vinculos); UI genérica `/Vinculos/{clave}/{id}/{persona?}` (fragmento htmx). **Al agregar un módulo**: implementar `ResumenesAsync` (y `Personas` si tiene más de una persona) y añadir en su `Detalle.cshtml` un `<div hx-get="@Url.Page("/Vinculos/Index", new { clave = Model.Clave, id = r.Id })" hx-trigger="load" hx-swap="outerHTML"></div>` (una por persona).
Medición: `$env:PARROQUIA_MEDIR = "1"; dotnet test --filter "FullyQualifiedName~BusquedaRendimiento" --logger "console;verbosity=detailed"` (200 000 filas, tarda ~1 min; sin la variable se omite).

## Al terminar una tarea
Resume qué cambió, qué pruebas corriste y qué decisiones de `docs/06` quedaron afectadas.
