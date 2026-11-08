# Plan de implementación y prompts para Claude Code

Cómo usar: para cada hito, abrir Claude Code en el repo, activar **modo plan**, pegar el prompt, revisar el plan contra los documentos, aprobar, implementar, probar, commit, `/clear`.
Todos los prompts asumen que `CLAUDE.md` y `docs/` están en el repo.

---

## M0 — Preparación (humano + Claude)
**Humano:** fotografiar 3–5 páginas reales de cada libro (bautismos, confirmaciones, comuniones, matrimonios, incluyendo épocas distintas y páginas con notas marginales). Responder bloqueantes de `06-preguntas-abiertas.md`. Crear el repo (`git init`), copiar `docs/` y `CLAUDE.md`.
**Prompt (análisis de libros, con las fotos adjuntas):**
> Lee @docs/02-modelo-de-datos.md y @docs/03-constancias.md. Te adjunto fotos de páginas reales del libro de <sacramento>. Lista, campo por campo, qué trae el libro físico que **no** está en el modelo y qué está en el modelo pero el libro no trae. No modifiques archivos; devuélveme una tabla y tus dudas de transcripción. No guardes datos personales de las fotos en ningún archivo.

## M1 — Esqueleto
> Plan primero, no escribas código aún. Crea la solución descrita en @docs/04-arquitectura.md (ADR-07): proyectos Web (Razor Pages), Dominio, Aplicacion, Infraestructura y Pruebas. EF Core con PostgreSQL (Npgsql), docker-compose solo para desarrollo, ASP.NET Core Identity con roles Capturista/Secretaria/Administrador y políticas por permiso según la matriz de @docs/04-arquitectura.md ADR-05, htmx incluido, layout base en español, usuario administrador inicial por *seed* con contraseña tomada de variable de entorno. Añade `dotnet test` con una prueba de humo. Dime los comandos exactos para ejecutar y probar. Actualiza CLAUDE.md con los comandos reales.

## M2 — Núcleo transversal
> Plan primero. Implementa en Dominio/Infraestructura: (1) `FechaParcial` como value object con las reglas de @docs/02-modelo-de-datos.md §2, mapeado como owned type, con pruebas (parseo, texto largo en español, orden); (2) interceptor de `SaveChanges` que escribe `Auditoria` (campo→anterior/nuevo) y rellena CreadoPor/ModificadoPor; pruebas con Testcontainers; (3) servicio de normalización de nombres para `BusquedaNormalizada` (minúsculas, sin tildes, sin "de/del/la") con pruebas; (4) `ConfiguracionParroquia` y `ParrocoVigente` con pantalla de administración; (5) catálogos administrables `Ministro` y `Tratamiento`; (6) clave de orden natural para Libro/Folio (texto con letras/bis); (7) bitácora de acceso (ver, imprimir, sesión) además de `Auditoria`. Escribe las pruebas **antes** que la implementación.

## M3 — Comunión (patrón de todos los módulos)
> Plan primero. Implementa el módulo Comunión completo y vertical: entidad + configuración EF + migración; formulario de captura/edición (teclado primero, "Guardar y nuevo" que conserva Libro y sugiere Folio+1); validaciones de @docs/02 §6; aviso de duplicados; marca Revisar; búsqueda y listado paginado; vista de detalle con historial; plantilla de constancia según @docs/03 §2.3 usando una función pura que devuelve un modelo de documento; PDF con QuestPDF fiel a @docs/referencias/constancia_comunion.jpeg; bitácora `ConstanciaEmitida` con instantánea. **Antes** de la plantilla, escribe la prueba golden con el texto esperado del ejemplo. Implementa `IModuloSacramental` para que menú y búsqueda global lo descubran. Al terminar, documenta en CLAUDE.md los pasos para agregar un módulo nuevo.

## M4 / M5 / M6 — Bautismo, Confirmación, Matrimonio
> Plan primero. Crea el módulo <Bautismo|Confirmación|Matrimonio> **replicando exactamente el patrón del módulo Comunión** (revisa su código y la sección "agregar un módulo" de CLAUDE.md). Modelo según @docs/02 §3.2, texto según @docs/03 §2.x, ejemplo visual @docs/referencias/constancia_<x>.jpeg. Colecciones dinámicas (padrinos/testigos) con alta/baja de filas sin recargar. Prueba golden primero. No cambies el módulo Comunión ni el núcleo salvo que sea imprescindible; si lo es, explícame por qué antes.
*Para Matrimonio añadir:* "Dos contrayentes como owned type; búsqueda por cualquiera de los dos; texto 'literalmente dice' con las correcciones aprobadas en @docs/06."

## M7 — Búsqueda global y vínculos
> Plan primero. Implementa la pantalla de inicio con búsqueda global (nombres/apellidos parcial, sin tildes, filtros de sacramento/fecha/libro) sobre los módulos registrados, resultados ordenados por apellidos, matrimonio por ambos contrayentes, y enlace al detalle y a imprimir constancia. Implementa `Vinculo` (misma persona, opcional) con UI para vincular/desvincular y mostrar vinculados en el detalle. Mide la búsqueda con 200 000 filas de datos sintéticos y reporta tiempos.

## M8 — Producción
> Plan primero. Prepara el despliegue en la PC de oficina con Windows 10/11 según @docs/04 ADR-08/09. Entrega: scripts PowerShell de instalación/actualización, PostgreSQL como servicio, la app como servicio de Windows con arranque automático, configuración HTTPS interna, tarea programada de respaldo diario con retención, script de restauración y **procedimiento de prueba de restauración**, página de estado para el Administrador, `docs/operacion.md` y manual de usuario de 2 páginas (capturista y secretaria). No toques datos de producción; todo con datos sintéticos.

---

## Patrón para cualquier hito (plantilla)
> Lee @CLAUDE.md y @docs/<archivos relevantes>. Objetivo: <una frase>. Alcance: <qué entra / qué no entra>. Criterio de terminado: <pruebas + comportamiento visible>. Hazme primero un plan paso a paso con riesgos y preguntas; no escribas código hasta que lo apruebe.

## Fase 2 (esquema, no construir aún)
Solicitudes (solicitante, documento pedido, fecha, estado), cobros e ingresos (monto, recibo, caja, corte diario), reportes, rol Secretaría/Caja. Ancla ya prevista: `ConstanciaEmitida.SolicitudId`. Iniciar con un documento `docs/fase2-*.md` y su propia ronda de preguntas.
