# Estrategia de desarrollo con Claude Code — Sistema Parroquial (Mazatenango)

> Documento rector. Léelo primero. Estado: **borrador v2 (7-nov-2026)**. Las decisiones marcadas ✅ las confirmó el usuario; las marcadas 🟡 son propuestas pendientes de confirmar (ver `06-preguntas-abiertas.md`).

## 1. Decisiones confirmadas

| Tema | Decisión |
|---|---|
| Despliegue | ✅ Aplicación web en **servidor local**: PC de oficina con **Windows 10/11**, dedicada y **encendida 24/7**; acceso solo por red local |
| Stack | ✅ **C# / .NET**, ASP.NET Core **Razor Pages + htmx**, **PostgreSQL** (ver `04-arquitectura.md`) |
| Alcance Fase 1 | ✅ Captura + búsqueda + reimpresión de constancias (Bautismo, Confirmación, Comunión, Matrimonio). **Sin** solicitudes ni ingresos |
| Modelo de personas | ✅ Registros independientes + **vínculo opcional** entre registros de la misma persona |
| Escaneos del libro | ✅ **No** se guardan imágenes de las páginas |
| Datos dudosos | ✅ **Fechas parciales** (año / año-mes / completa) + marca **"Revisar"** |
| Seguridad | ✅ Usuarios individuales + 3 roles (**Capturista**, **Secretaria**, **Administrador**) + **bitácora/historial completo de todo** + bitácora de cada constancia impresa |
| Constancias | ✅ Papel en blanco con membrete en el PDF · firma siempre el **párroco vigente** · fecha de emisión = hoy (solo Administrador puede cambiarla) · se **corrigen las erratas** de las plantillas · otras constancias → Fase 2 |
| Datos de libros | ✅ `Partida` obligatoria en todos · `Libro` y `Folio` como **texto** (hay libros con letras/bis; el folio reinicia por libro) · notas marginales en **texto libre** · **ministro**: catálogo + texto libre · **sin** doble verificación (basta "Revisar") |
| Volumen y soporte | ✅ 20 000–100 000 registros históricos · el sistema lo mantendrá el **desarrollador** |
| Futuro | Más sacramentos/módulos; Fase 2 = solicitudes e ingresos. **Módulos pendientes** (aún sin constancias de ejemplo): *Defunciones* y *Anulación de matrimonio* |

## 2. Qué se concluye del material revisado

- Las 4 constancias de `referencias/` **no comparten estructura**: cada sacramento necesita su entidad, su formulario y su plantilla de impresión. Solo el membrete y la firma son comunes. Detalle en `03-constancias.md`.
- El prototipo de Access (`Sistema_Parroquial.accdb` + `modSistemaParroquial.bas`) solo contiene registros `[EJEMPLO]`: **no hay datos que migrar**. Sirve como referencia de campos y de la idea de búsqueda sin tildes. No se reutiliza código. Sus brechas (matrimonio sin edad/originario/feligrés, padres como texto único, una sola plantilla genérica, sin usuarios ni auditoría) son las que este diseño corrige.
- El prototipo incluye *Defunciones*; existen además las *anulaciones de matrimonio*. Ambos son **módulos pendientes** (el usuario aún no tiene sus constancias): fuera de Fase 1, con el diseño modular listo para agregarlos.

## 3. Principios de trabajo con Claude Code

1. **Los documentos en el repo son la fuente de verdad.** Claude Code arranca sin memoria entre sesiones; todo lo que importa vive en `CLAUDE.md` + `docs/`. Si una decisión cambia, se edita el documento en el mismo commit.
2. **`CLAUDE.md` corto** (<150 líneas): comandos, convenciones, reglas innegociables y enlaces a `docs/`. El detalle va en los otros archivos, que se referencian con `@docs/...`.
3. **Rebanadas verticales, no capas.** Cada hito entrega algo usable de punta a punta (captura → guardado → búsqueda → PDF). Nada de "primero toda la base de datos, luego toda la UI".
4. **Primero la plantilla, luego la repetición.** Comunión (la más simple) se construye completa y se endurece; sirve de patrón para Bautismo, Confirmación y Matrimonio. Así el cuarto módulo cuesta una fracción del primero y los futuros también.
5. **Pruebas antes que PDF.** El texto de cada constancia se verifica con pruebas "golden" contra los 4 ejemplos reales. La redacción legal-eclesiástica no puede cambiar por accidente.
6. **Modo plan por hito.** Para cada hito: pedir plan → revisarlo contra `docs/` → aprobar → implementar → `dotnet test` → commit. `/clear` entre hitos.
7. **Commits pequeños y frecuentes**, uno por rebanada o sub-paso, con la suite de pruebas en verde.
8. **Datos reales, poco a poco.** Piloto con ~20 registros reales por sacramento *antes* de la captura masiva (hito M8). Los empleados detectan lo que ningún documento anticipa.
9. **Nunca datos personales reales en el repo ni en prompts.** Usar datos de ejemplo. Las imágenes de `referencias/` son ejemplos.

## 4. Hitos

| Hito | Entrega | Criterio de salida |
|---|---|---|
| **M0 Preparación** | Repo, `CLAUDE.md`, docs, decisiones cerradas, fotos de páginas reales de cada libro físico | Preguntas bloqueantes de `06` respondidas |
| **M1 Esqueleto** | Solución .NET, EF Core + PostgreSQL (Docker en dev), Identity con roles, layout base, seed de admin | Login funcional, `dotnet test` verde |
| **M2 Núcleo transversal** | `FechaParcial`, auditoría automática (interceptor), configuración de parroquia, normalización de búsqueda, bitácora de constancias | Pruebas de auditoría y fechas parciales verdes |
| **M3 Comunión (patrón)** | Captura, edición, validaciones, duplicados, búsqueda, PDF idéntico al ejemplo, reimpresión con bitácora | PDF coincide con `constancia_comunion.jpeg`; un empleado lo usa sin ayuda |
| **M4 Bautismo** | Ídem (padres, padrinos múltiples, notas marginales, ministro) | Ídem |
| **M5 Confirmación** | Ídem | Ídem |
| **M6 Matrimonio** | Ídem (dos contrayentes, testigos, texto "literalmente dice") | Ídem |
| **M7 Búsqueda global y vínculos** | Búsqueda por persona en todos los libros, enlace opcional entre registros | Encontrar a una persona en los 4 libros desde una sola pantalla |
| **M8 Puesta en producción** | Instalación en servidor local, HTTPS interno, respaldos automáticos **con restauración probada**, manual breve, capacitación, piloto | Restauración probada en otra máquina; piloto sin bloqueos |

Luego: **Fase 2** (solicitudes e ingresos) y módulos futuros (ver §6).

## 5. Cómo se ve una sesión con Claude Code

1. Abrir la carpeta del repo. `CLAUDE.md` se carga solo.
2. Prompt de hito (plantillas en `05-plan-implementacion.md`), en modo plan.
3. Revisar el plan contra `@docs/02-modelo-de-datos.md` y `@docs/03-constancias.md`. Corregir antes de aprobar.
4. Implementar por pasos pequeños; pedir pruebas primero donde aplique (constancias, fechas, auditoría).
5. Ejecutar la app, probar a mano el flujo del hito, revisar el diff.
6. Commit. Actualizar docs si algo cambió. `/clear`.

## 6. Diseñar hoy para crecer mañana (sin construirlo todavía)

- **Un sacramento = un módulo autocontenido**: entidad + configuración EF + formulario + plantilla de constancia + pruebas. Registrarlo en una lista central (`IModuloSacramental`) para que menú, búsqueda global y bitácora lo recojan sin tocar código ajeno. Agregar Defunciones, Primera Comunión de adultos, etc. = agregar un módulo.
- **`ConstanciaEmitida` ya trae `SolicitudId` nullable** (sin usar en Fase 1). En Fase 2, una solicitud agrupa constancias emitidas y un cobro.
- **Roles extensibles**: la `Secretaria` ya existe en Fase 1 (imprime); en Fase 2 se le suman solicitudes y cobros sin rehacer autorización (políticas por permiso, no por nombre de rol en el código).
- **Configuración de parroquia en base de datos**, no en código: nombre, dirección, teléfono, párroco vigente, logo. El párroco cambia; toda constancia (nueva o reimpresión de un registro antiguo) lleva al **párroco vigente** al momento de emitirla.

## 7. Riesgos principales y mitigación

| Riesgo | Mitigación |
|---|---|
| Pérdida de datos en servidor local | Respaldo automático diario + copia en disco externo + copia fuera de sitio; **prueba de restauración** obligatoria en M8; UPS |
| Libros físicos con más campos que la constancia | Fotografiar páginas reales de cada libro en M0 y ajustar el modelo antes de programar |
| Errores de digitación masiva | Marca "Revisar", detección de duplicados (libro+folio+partida), auditoría total (sin doble verificación, por decisión) |
| Cambio de redacción accidental en constancias | Pruebas golden contra los ejemplos; plantillas en un solo lugar |
| Dependencia del desarrollador/IA | Docs completos, convenciones simples, stack mainstream, sin dependencias exóticas |
| Concurrencia (2 empleados editando) | Concurrencia optimista (token de versión) con mensaje claro al conflicto |
