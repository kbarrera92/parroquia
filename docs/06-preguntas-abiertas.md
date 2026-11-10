# Decisiones y preguntas abiertas

Última actualización: 8-nov-2026. Las decisiones ✅ ya están reflejadas en los demás documentos.

## ✅ Resueltas (7-nov-2026; 20–23 durante M3; 24–26 en M4; 27 en M5; 28–30 en M6; 31–33 en M7; 34–36 en M8; 37–39 libro digital)
| # | Tema | Decisión |
|---|---|---|
| 1 | Servidor | PC de oficina, Windows 10/11, dedicada, encendida 24/7 |
| 2 | Base de datos | PostgreSQL |
| 3 | UI | Razor Pages + htmx |
| 4 | Bautismo, rótulo del nombre | Sin rótulo (se elimina el primer "Al margen se lee:") |
| 5 | Erratas | Se corrigen las tres: "Confirmación", "San Bartolomé", "ante los testigos" |
| 6 | Firmante | Siempre el párroco vigente al emitir |
| 7 | Tratamiento en matrimonio | Campo por contrayente (catálogo: don, la Srita., etc.) |
| 8 | Fecha de emisión | Hoy; editable solo por Administrador (con bitácora) |
| 9 | Otras constancias | Existen, van a Fase 2 |
| 10 | Ministro | Catálogo de sacerdotes + opción de escribir otro |
| 11 | Partida | Existe y es obligatoria en todos los sacramentos |
| 12 | Libro/Folio | Folio reinicia por libro; hay letras/bis → texto con orden natural |
| 13 | Notas marginales | Texto libre ("Sin anotaciones" si vacío) |
| 14 | Verificación | Sin doble verificación; basta marca "Revisar" |
| 15 | Roles | Capturista: ver/buscar, crear, editar, anular. Secretaria: ver/buscar, imprimir/reimprimir. Admin: todo. Bitácora total |
| 16 | Defunciones / anulación de matrimonio | Módulos pendientes (aún sin constancias); fuera de Fase 1 |
| 17 | Papel | En blanco, membrete en el PDF |
| 18 | Volumen | 20 000–100 000 registros históricos |
| 19 | Soporte | Lo mantiene el desarrollador |
| 20 | Formato de fecha en constancias (M3) | "de" en todas ("2 de octubre de 2004"), aunque el ejemplo en papel diga "del 2004". Corrección consciente, ya en la prueba golden de Comunión |
| 21 | PDF (M3) | QuestPDF Community + fuentes Liberation (OFL) incrustadas; maquetación validada contra el ejemplo de Comunión |
| 22 | Campos de Comunión (M3) | Mínimos (nombres, apellidos, fecha, libro, folio, partida, ministro, notas) hasta tener fotos de páginas reales (M0) |
| 23 | Avisos al imprimir (M3) | Cualquier aviso (dato faltante o "Revisar") exige una casilla de confirmación; queda `ConAdvertencias` en la bitácora |
| 24 | Padrinos de Bautismo (M4) | Lista de texto 0..n (máx. 10) en el orden del libro, columna `text[]`; auditada como texto unido. Tabla hija descartada (borrado físico prohibido) |
| 25 | "Nació" en la constancia de Bautismo (M4) | Con tilde (el papel dice "Nacio"); corrección consciente, ya en el golden |
| 26 | Nacimiento vs. bautismo (M4) | Opcional; no puede ser posterior al bautismo, comparando solo hasta la precisión común |
| 27 | Campos de Confirmación (M5) | Mínimos (nombres, apellidos, fecha, padrinos 0..10, libro, folio, partida, ministro/obispo, notas) hasta tener fotos del libro real (M0 [B]); `Padre`, `Madre` y `ParroquiaBautismo` quedan pendientes |
| 28 | Contrayentes de Matrimonio (M6) | *Complex type* `Contrayente` (columnas `esposo_*` / `esposa_*`), no owned: la auditoría lo registra campo por campo (`Esposo.Nombres`). Cambio mínimo en el interceptor; ver `04` ADR-04 |
| 29 | Tratamiento del contrayente (M6) | Se guarda el **texto** del catálogo (select con los activos; por defecto "don" / "la Srita."), no `TratamientoId`: un complex type no admite FK y el historial queda legible |
| 30 | Legitimidad y raya de firma (M6) | Mientras la pregunta de legitimidad siga abierta, "hijo/a legítimo/a de:" es texto fijo, sin campo. La constancia de Matrimonio lleva raya sobre la firma (`DocumentoConstancia.LineaFirma`, opcional; las otras no) |
| 31 | Vínculo y persona (M7) | El vínculo guarda **qué persona** del registro ("" / `esposo` / `esposa`): Bautismo(él)↔Matrimonio↔Bautismo(ella) no los declara la misma persona |
| 32 | Cadena de vínculos (M7) | El detalle muestra **toda la cadena** (directos + "por medio de"); desvincular solo quita enlaces directos |
| 33 | Desvincular y permiso (M7) | Desvincular **anula** (auditado, sin borrado físico); volver a vincular reactiva la fila. Permiso `Registros.Editar`, sin permiso nuevo |
| 34 | Política de contraseñas (M8) | Mínimo **12** caracteres, sin reglas de composición ni caducidad, con lista de contraseñas comunes; ver `04` ADR-05 |
| 35 | Recuperación de contraseña (M8) | Sin correo: el Administrador restablece (temporal de un solo uso + cambio obligatorio). Emergencia con `PARROQUIA_ADMIN_RESTABLECER` en el servidor |
| 36 | Usuarios (M8) | Un rol por usuario; desactivar en vez de borrar; cambios de usuarios en `BitacoraAcceso` (no en `Auditoria`); límite de 10 intentos de login por IP y minuto además del bloqueo por cuenta |
| 37 | Sacramentos nuevos (asientos del sistema) | Nacen en el sistema, que asigna libro/folio/partida **continuando la numeración de los libros físicos**; partidas por folio y folios por libro configurables; al llenarse un libro se abre el siguiente solo (`04` ADR-11) |
| 38 | Partida anulada | **Conserva su número**; nunca se reutiliza |
| 39 | Transcribir vs. asentar | Predeterminado = transcribir libro físico; el asiento nuevo se elige a propósito. No se puede transcribir en un libro digital ni abrir uno digital con el número de un libro ya transcrito |

## 🟡 Pendientes

### Bloquea M0 — datos reales de los libros
- **[B]** Fotografiar 3–5 páginas de cada libro físico (épocas distintas, con notas marginales). Verificar: ¿padres en comunión y confirmación? ¿parroquia de bautismo? ¿lugar de nacimiento? ¿ministro/obispo en confirmación? ¿máximo de padrinos y de testigos? Define las columnas finales de `02`.

### Constancias
- **Licencia de QuestPDF (antes de M8):** el párroco debe confirmar que la parroquia cumple la licencia Community (organización de beneficio público, o ingresos anuales < 1 M USD; no aplica a entidades del sector público). Detalle en `04`, ADR-06. Si no cumpliera, hay que adquirir otra licencia o cambiar de motor.
- **Fecha "de" vs "del"**: confirmar con el párroco que no le molesta el cambio respecto al papel actual (decisión 20).
- **"Ref:" en matrimonio**: qué es (correlativo, dato libre, se elimina). *El usuario lo averiguará.* Mientras tanto la línea queda en blanco.
- Matrimonio: ¿"hijo/a **legítimo/a** de" se mantiene siempre o hay hijos naturales? ¿Se necesita un campo de legitimidad? *(M6 lo imprime como texto fijo; si cambia, columna nueva con migración.)*
- Matrimonio: la fecha de cierre usa formato dd/MM/yyyy; las otras constancias usan fecha larga. ¿Unificar?
- Valores iniciales del catálogo de **tratamientos** y de **ministros** (lista de sacerdotes actuales y anteriores).

### Servidor y operación (antes de M8)
- Versión exacta de Windows (si es Windows 10: ¿tiene actualizaciones de seguridad extendidas o se migra a Windows 11?). Marca y capacidad de disco, ¿hay UPS?, ¿disco externo para respaldos?, ¿dónde irá la copia fuera de sitio?
- Confirmar que no habrá acceso desde fuera de la red de la parroquia.

### Libro digital (asientos nuevos)
- **¿Se necesita un libro impreso?** Consultar al párroco/diócesis si la partida nueva debe imprimirse (con firmas del ministro y párroco) y encuadernarse por libro, o si basta el registro digital. Hoy solo existe la constancia; el acta de partida sería una plantilla nueva por módulo (con `LibroDigitalId` y folio/partida ya asignados no hay que rehacer la numeración).
- Por cada sacramento: último libro, folio y partida **físicos** usados, partidas por folio, folios por libro y cómo se numera la partida en el papel: correlativa, reinicia por libro o reinicia por folio (para abrir el primer libro digital sin saltos).
- Fecha de corte entre el último asiento en papel y el primero digital.

### Alcance futuro
- Fase 2: tarifas por tipo de constancia, recibos, quién cobra, corte diario, exoneraciones, otras constancias (solicitar redacción de cada una).
- Defunciones y anulación de matrimonio: conseguir constancias de ejemplo y datos de los libros.
- ¿Exportar listados (Excel) o estadísticas (bautizos por año)?
- ¿Obligaciones legales/diocesanas sobre cómo conservar los registros digitales? Consultar a la curia si aplica.
- Plazo objetivo para terminar la digitación histórica (sirve para planear la captura masiva).
