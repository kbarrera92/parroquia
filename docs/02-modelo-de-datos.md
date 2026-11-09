# Modelo de datos — Fase 1 (propuesta)

> Base: lo que imprimen las 4 constancias de ejemplo + lo mínimo para trazabilidad. 🟡 **Pendiente M0:** comparar con páginas reales de cada libro físico; si el libro trae más datos que la constancia (p. ej. padres en comunión/confirmación, parroquia de bautismo, lugar de nacimiento), se agregan como campos opcionales **antes** de programar.

## 1. Convenciones
- PK `Id` (bigint). En entidades auditables se genera con una secuencia **HiLo** (`secuencia_id`), no identity: el Id existe al hacer `Add` y la auditoría de Alta lo lleva en el mismo guardado. Nombres de tablas y columnas en español, sin tildes, `PascalCase` en C#, `snake_case` en PostgreSQL.
- `Libro` y `Folio` son **texto** (admiten letras y "bis"; el folio reinicia por libro) y se guardan además con una **clave de orden natural** para ordenar "2" antes que "10". `Partida` es **obligatoria** en todos los sacramentos.
- Todo registro sacramental lleva: `Libro`, `Folio`, `Partida`, estado de revisión, anulación y datos de auditoría de fila (`CreadoPor`, `CreadoEn`, `ModificadoPor`, `ModificadoEn`) más token de concurrencia (`xmin` en PostgreSQL / `rowversion` en SQL Server).
- Nombres de personas tal como constan en el libro: `Nombres` y `Apellidos` separados (apellido de casada, p. ej. "López de Pérez", se respeta como esté escrito). **`BusquedaNormalizada`** (minúsculas, sin tildes ni puntuación, sin las partículas "de/del/la/las/los/y") se **calcula en C#** al guardar con `NormalizadorBusqueda` (la misma función normaliza la consulta) y lleva índice trigram GIN (`pg_trgm`; creado en M3 con `comunion`, y lo usa cada tabla sacramental con `ConfigurarRegistro`).
- Texto libre solo donde el libro lo es (notas marginales). Nada de "padres" en un único campo de texto.

## 2. Tipo valor `FechaParcial`
Mapeado como *complex type* de EF (no owned: con las tres columnas nulas un owned type se leería como `null`; el complex type siempre existe, con valor "vacía"). Tres columnas por fecha `{prefijo}_anio/_mes/_dia` más una columna generada almacenada `{prefijo}_clave_orden` para ordenar y filtrar:
| Campo | Tipo | Regla |
|---|---|---|
| `Anio` | smallint? | 1500–año actual |
| `Mes` | smallint? | 1–12, requiere `Anio` |
| `Dia` | smallint? | 1–31 válido para el mes, requiere `Mes` |
Propiedades: `EsCompleta`, `ATexto()` → "13 de agosto de 1978" / "agosto de 1978" / "1978" / vacío; `ClaveOrden` (año*10000+mes*100+día, ceros si falta) para ordenar/filtrar.

## 3. Entidades

### 3.1 Comunes
**`RegistroBase`** (campos compartidos; cada sacramento hereda por composición/clase base abstracta, **tablas separadas**, sin TPH)
`Libro` (string, ej. "80"), `Folio` (string), `Partida` (string, obligatoria), `Revisar` (bool), `MotivoRevision` (string?), `Anulado` (bool), `MotivoAnulacion` (string?), `NotasMarginales` (text libre, ?), `MinistroId` (FK?) + `MinistroTexto` (string?; si no está en el catálogo).
Implementada como clase base abstracta `RegistroSacramental` (M3). **Columnas derivadas** (no se auditan, las recalcula `ActualizarDerivados()` al guardar): `LibroOrden`, `FolioOrden`, `PartidaOrden` (`ClaveOrdenNatural`) y `BusquedaNormalizada` (cada sacramento define su texto: Comunión usa nombres + apellidos). Límites: Libro/Folio/Partida ≤ 30 caracteres, nombres ≤ 150, motivos ≤ 500, notas ≤ 2000. Índices por tabla: `(LibroOrden, FolioOrden, PartidaOrden)` no único, GIN trigram en `BusquedaNormalizada`, `Anulado`, `Revisar`, y la clave de orden de la fecha principal.

**`ConfiguracionParroquia`** (1 fila): `Nombre` ("Catedral — Parroquia San Bartolomé"), `Direccion`, `Municipio`, `Telefono`, `Logo` (bytea, PNG/JPEG ≤ 1 MB, en la base para que un solo respaldo lo cubra) + `LogoTipoContenido`, `TextoLugarEmision` ("Mazatenango, Such."), `CiudadAbreviada`.

**`ParrocoVigente`**: `Nombre` ("P. Neil Contreras"), `Titulo` ("Párroco"), `VigenteDesde`, `VigenteHasta?`, `EsActual` (índice único parcial: a lo sumo uno vigente; al registrar uno nuevo, el anterior se cierra el día previo; no puede iniciar en fecha futura). Toda constancia, incluso la reimpresión de un registro antiguo, lleva al párroco **vigente al emitir**.

**`Ministro`** (catálogo): `Id`, `Nombre`, `Activo`. Administrable (sin borrado: se desactiva; no admite nombres repetidos ignorando tildes y mayúsculas); el formulario permite elegir de la lista o escribir otro nombre.

**`Tratamiento`** (catálogo): `Id`, `Texto` ("don", "Sr.", "la Srita.", "la Sra.", "viudo…" según defina el párroco), `Activo`. Se siembran "don" y "la Srita." si la tabla está vacía.

**`Usuario` / `Rol`**: ASP.NET Core Identity. Roles: `Capturista`, `Secretaria`, `Administrador`. Permisos por política (ver `04`).

**`Vinculo`** (M7; tabla `vinculo`, `EntidadAuditable` + `IAnulable`): `TipoOrigen`/`TipoDestino` (clave del módulo), `IdOrigen`/`IdDestino`, `PersonaOrigen`/`PersonaDestino` ("" salvo Matrimonio: `esposo` / `esposa`, para no confundir a los dos contrayentes). Relación simétrica "misma persona": el par se guarda en **orden canónico** (`ExtremoVinculo.CompareTo`, el menor como origen) con índice **único**. No hay FK dura entre tablas distintas (se valida en servicio: módulo, persona y registro existentes y no anulados; no se vincula consigo mismo ni dos veces la misma cadena). **Desvincular = anular** (motivo por defecto "Desvinculado"; queda en `Auditoria`); volver a vincular el par reactiva la misma fila (`RevertirAnulacion`). Los vínculos forman cadenas: si A↔B y B↔C, desde A se ve C "por medio de B" (tope 50 personas). Permiso: `Registros.Editar` para vincular/desvincular, `Registros.Ver` para verlos.

**`ConstanciaEmitida`** (bitácora): `Id`, `TipoSacramento`, `RegistroId`, `EmitidaEn`, `EmitidaPor`, `UsuarioId?`, `FechaImpresa` (`DateOnly`; hoy salvo ajuste), `FechaAjustadaPor?` (solo Administrador), `Firmante` (párroco vigente al emitir), `InstantaneaJson` (jsonb: el `DocumentoConstancia` tal como se imprimió, sin logo), `HashPdf` (SHA-256 hex), `ConAdvertencias` (bool: se imprimió confirmando avisos), `SolicitudId?` ← **reservado Fase 2, sin usar**. Solo se agrega (no es auditable). `TipoSacramento` es la `Clave` del módulo ("comunion"). **Sin `Correlativo` por ahora:** se agregará con migración cuando se resuelva qué es "Ref" (ver `06`). Índice `(TipoSacramento, RegistroId)`.

**`Auditoria`**: `Id`, `Entidad`, `EntidadId`, `Accion` (Alta/Modificacion/Anulacion/RevertirAnulacion), `UsuarioId`, `UsuarioNombre` (instantánea), `Fecha`, `CambiosJson` jsonb (campo → anterior/nuevo; las fechas parciales como texto de libro; el logo como "[N bytes]"). Alimentada por un *SaveChanges interceptor*, no a mano; un borrado físico de una entidad auditable lanza excepción.

**`BitacoraAcceso`** (solo se agrega): `Id`, `Fecha`, `UsuarioId?`, `UsuarioNombre`, `Evento` (InicioSesion, InicioSesionFallido, CierreSesion, CuentaBloqueada, VerRegistro, ImprimirConstancia), `Entidad?`, `EntidadId?`, `Detalle?`, `Ip?`. No registra contraseñas ni búsquedas. `VerRegistro` (al abrir el detalle de un registro) e `ImprimirConstancia` (al emitir) se registran desde M3.

### 3.2 Sacramentos (campos de captura)

**`Comunion`** — constancia: nombre, fecha, libro, folio. **Implementada en M3** con campos mínimos.
`Nombres`, `Apellidos`, `FechaComunion` (FechaParcial; tabla `comunion`, columnas `fecha_comunion_*`). *(Opcionales a confirmar en M0, aún sin fotos de páginas reales: `Padre`, `Madre`, `ParroquiaBautismo`; si el libro los trae se agregan con una migración.)*
Reglas de captura: `Nombres` y `Apellidos` obligatorios salvo registro "Revisar" con motivo; la fecha exige al menos el año; ministro opcional (catálogo o texto libre).

**`Bautismo`** — **Implementado en M4** (tabla `bautismo`; fechas `fecha_bautismo_*` y `fecha_nacimiento_*`).
`Nombres`, `Apellidos`, `FechaBautismo` (FechaParcial; obligatoria al menos el año), `FechaNacimiento` (FechaParcial; opcional; no puede ser posterior al bautismo, se compara solo hasta donde ambas fechas tienen dato), `LugarNacimiento?` *(opcional, no se imprime)*, `Padre?`, `Madre?` (nombre completo c/u; el libro puede traer solo uno), `Padrinos` (lista de texto 0..n, máx. 10, en el orden del libro; columna `text[]`, no tabla hija: quitar uno sería un borrado físico fuera de la auditoría; el historial la muestra unida con "; "; la constancia los une con ", " y " y "), `Ministro` (quien bautizó; catálogo o texto libre; sale en "Firma de ministro"), `NotasMarginales` (si vacío → "Sin anotaciones"). Duplicados: igual libro+folio+partida, o igual persona + fecha de bautismo.

**`Confirmacion`** — **Implementada en M5** con campos mínimos (tabla `confirmacion`; fecha `fecha_confirmacion_*`).
`Nombres`, `Apellidos`, `FechaConfirmacion` (FechaParcial; obligatoria al menos el año), `Padrinos` (lista de texto 0..n, máx. 10, orden del libro; `text[]`, auditada unida con "; "), `Ministro?` *(opcional: obispo; catálogo o texto libre; no se imprime)*, `NotasMarginales?` *(no se imprimen)*. Duplicados: igual libro+folio+partida, o igual persona + fecha. *(Opcionales a confirmar en M0, aún sin fotos del libro: `Padre`, `Madre`, `ParroquiaBautismo`; se agregarían con una migración.)*

**`Matrimonio`** — **Implementado en M6** (tabla `matrimonio`; fecha `fecha_matrimonio_*`; contrayentes `esposo_*` / `esposa_*`).
`FechaMatrimonio` (FechaParcial; obligatoria al menos el año), `LugarCelebracion` (default "Iglesia de Mazatenango, Suchitepéquez"; si se deja vacío se guarda el default), `Testigos` (lista de texto 0..n, máx. 10, en el orden del libro; `text[]`, auditada unida con "; "; como `Padrinos` de Bautismo), `Ministro` ("Firma:"; catálogo o texto libre), `NotasMarginales` (si vacío → "Sin anotaciones"), más dos contrayentes `Esposo` y `Esposa`.
**`Contrayente` es un *complex type*** (no owned: así el interceptor de auditoría lo recorre y registra cada campo como `Esposo.Nombres`, `Esposa.Edad`…; siempre existe, con valores vacíos): `Tratamiento` (texto tomado del catálogo `Tratamiento` y guardado **como texto**, no `TratamientoId`: un complex type no admite FK y el historial queda legible; por defecto "don" / "la Srita." si existen), `Nombres`, `Apellidos` (ambos obligatorios salvo registro "Revisar" con motivo), `Edad` (smallint?, 12–120 si se informa), `OriginarioDe?`, `FeligresDe?`, `Padre?`, `Madre?`. La **legitimidad** no es campo: la constancia dice siempre "hijo legítimo de:" / "hija legítima de:" mientras siga abierta la pregunta de `06`.
Derivadas (`.NoAuditar()`): `BusquedaEsposo` y `BusquedaEsposa` (nombres + apellidos normalizados de cada uno, con índice trigram GIN) y `BusquedaNormalizada` (ambas juntas). La búsqueda por texto encuentra el registro si **todas** las palabras están en el esposo **o todas** en la esposa (no se mezclan), y la búsqueda global aporta **una fila por contrayente que coincide**. Duplicados: igual libro+folio+partida, o iguales esposo **y** esposa + fecha de matrimonio. Listado ordenado por apellidos y nombres del esposo.

## 4. Índices y restricciones
- Índice **no único** `(Libro, Folio, Partida)` por tabla + advertencia de duplicado en servicio (no bloqueo: pueden existir varias partidas por folio y libros reiniciados).
- GIN trigram sobre `BusquedaNormalizada` (extensión `pg_trgm`; `unaccent` ya no hace falta porque la normalización se hace en C#).
- Índice sobre `ClaveOrden` de la fecha principal y `Anulado`.
- `Matrimonio`: búsqueda indexada por **ambos** contrayentes (columnas normalizadas `busqueda_esposo`, `busqueda_esposa`, GIN trigram).
- Anulados excluidos por defecto en listados y búsquedas (filtro global EF), visibles al Administrador.

## 5. Búsqueda global
Vista/consulta `BusquedaGlobal` que unifica los 4 sacramentos con: `TipoSacramento`, `RegistroId`, `NombreMostrado`, `FechaPrincipal`, `Libro`, `Folio`, `Partida`, `Revisar`. Matrimonio aporta **dos filas** (una por contrayente) apuntando al mismo registro. Cuando se agregue un módulo nuevo, solo se añade su `SELECT` (o se registra vía `IModuloSacramental`).

## 6. Reglas de validación clave
- `Libro`, `Folio` y `Partida` obligatorios (texto libre sin validación numérica estricta); orden natural al listar.
- Nombres y apellidos obligatorios (excepto que el registro esté marcado `Revisar`, caso en que se admite vacío con motivo).
- Fecha del sacramento obligatoria al menos con año; `FechaNacimiento` ≤ `FechaBautismo`.
- Matrimonio: edades 12–120 si se informan; ambos contrayentes con nombres y apellidos.
- Normalización al guardar: recorte de espacios, espacios múltiples, capitalización **no** se altera (el libro manda); mayúsculas solo en impresión de nombres propios principales, como en las constancias.
