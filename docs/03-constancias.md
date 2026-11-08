# Constancias — especificación de impresión

Fuente: `referencias/constancia_*.jpeg` (ejemplos marcados "Ejemplo" a mano). Los datos de los ejemplos son ficticios o de prueba; **no usar datos reales en pruebas ni prompts**.

## 1. Elementos comunes
- **Membrete:** logo `referencias/logo_parroquia.jpeg` (izquierda) · a la derecha, en tipografía serif en negrita: "6a. Avenida y 7a. Calle zona 1 -:- Teléfono: (502) 7872-0051" / "Mazatenango, Suchitepéquez Guatemala C.A." · línea doble horizontal debajo.
- **Título** centrado en un recuadro de bordes redondeados, negrita, mayúsculas: `CERTIFICACIÓN DE <SACRAMENTO>`.
- **Cierre:** lugar y fecha de emisión a la izquierda (formato largo, ej. "Mazatenango, 4 de noviembre de 2026"); a la derecha **P. Neil Contreras / Párroco.** (negrita).
- Hoja carta **en blanco** (el membrete sale en el PDF), márgenes ~2 cm, impresión monocromática, espacio para firma y sello manuales sobre el nombre del párroco.
- Valores del membrete y párroco **salen de `ConfiguracionParroquia` / `ParrocoVigente`**, no de la plantilla. Firma siempre el **párroco vigente** al emitir. La fecha de emisión es hoy; solo el Administrador puede cambiarla (queda en bitácora).
- Fechas: "13 de agosto de 1978" (día sin cero, mes en minúscula). `FechaParcial` imprime lo que exista. **Decisión (M3):** los ejemplos en papel dicen "del 2004" / "del 2026"; el sistema imprime siempre **"de"** ("2 de octubre de 2004"), igual en todas las constancias. Es una corrección consciente: la prueba golden ya la refleja.
- Nombre de la persona: mayúsculas y negrita, centrado/destacado.
- Si falta un dato requerido o el registro está en "Revisar": advertencia en pantalla antes de imprimir; el PDF **no** imprime marcas de advertencia. Cualquier aviso exige marcar una casilla de confirmación para imprimir (queda `ConAdvertencias` en la bitácora).
- Impresión: página única `/Constancias/{clave}/{id}` para todos los sacramentos (vista previa con el mismo modelo de documento + PDF en pestaña nueva). Primera impresión: permiso `Constancias.Imprimir`; las siguientes, `Constancias.Reimprimir`.

## 2. Texto por sacramento (`{campo}` = dato del registro)

### 2.1 Bautismo — `CERTIFICACIÓN DE BAUTISMO`
```
El infrascrito Párroco, certifica tener a la vista el Libro Bautizo No. {Libro} Folio {Folio}
en el que aparece inscrito el Bautismo de:
{NOMBRES APELLIDOS}
Bautizado el día:   {FechaBautismo}, Nació el {FechaNacimiento}
hijo (a) de:        {Padre} y {Madre}
Sus padrinos fueron: {Padrino1} y {Padrino2}
Firma de ministro:  {Ministro}
Al margen se lee:   {NotasMarginales | "Sin anotaciones"}

Mazatenango, {FechaEmision}                         {Párroco} / Párroco.
```
Decisión: el nombre va **sin rótulo** (se elimina el primer "Al margen se lee:" del original, que era un rótulo equivocado). Valores subrayados en el original. Si solo hay un progenitor o un padrino, no imprimir " y "; con 3 o más padrinos: "A, B y C".

**Implementado en M4** (`PlantillaBautismo`). Prueba golden: `tests/Parroquia.Pruebas/Constancias/Golden/bautismo.txt` (datos del ejemplo; fechas con "de"; emisión 4/11/2026). Una línea de texto plano por párrafo: la introducción ("El infrascrito Párroco… Bautismo de:") es un solo párrafo; las cinco filas "rótulo: valor" salen como `Parrafo.Fila` (rótulo en columna fija a la izquierda, valor subrayado; en texto plano "{rótulo} {valor}"). Reglas: "Nació el {fecha}" solo si hay fecha de nacimiento (**"Nació" con tilde**, corrección del "Nacio" del papel); padres y padrinos se unen con `FormatoConstancia.UnirLista`; ministro vacío deja la fila con el valor en blanco (y avisa); cierre `Mazatenango, {fecha larga}`. Avisos antes de imprimir: falta nombre/apellido, fecha de bautismo incompleta, falta nacimiento, faltan padres, faltan padrinos, falta ministro, falta libro/folio, "Revisar". Esta constancia no imprime la partida.

### 2.2 Confirmación — `CERTIFICACIÓN DE CONFIRMACIÓN`
```
Por la presente se hace constar que:
{NOMBRES APELLIDOS}
Recibió el Sacramento de la CONFIRMACIÓN en esta parroquia el día:
{FechaConfirmacion}
Sus padrinos fueron:
{Padrino1} [y {Padrino2}]
Según consta en el libro de Confirmación No. {Libro} folio {Folio}

Mazatenango, Such. {FechaEmision},                 {Párroco} / Párroco.
```
Decisión: se corrige la errata "Confirmacion" → "Confirmación".

**Implementado en M5** (`PlantillaConfirmacion`). Prueba golden: `tests/Parroquia.Pruebas/Constancias/Golden/confirmacion.txt` (datos del ejemplo; fechas con "de"; emisión 6/11/2026). Una línea por párrafo; los padrinos van en su propia línea bajo "Sus padrinos fueron:", unidos con `FormatoConstancia.UnirLista` (sin padrinos queda la línea en blanco y avisa). Cierre `{TextoLugarEmision} {fecha larga},` (con la coma final del papel). Estilos del ejemplo: nombre, fecha, padrinos y "Según consta…" en negrita; la fecha **no** va subrayada. Esta constancia no imprime partida, ministro ni notas marginales. Avisos antes de imprimir: falta nombre/apellido, fecha incompleta, faltan padrinos, falta libro/folio, "Revisar".

### 2.3 Primera Comunión — `CERTIFICACIÓN DE COMUNIÓN`
```
Por la presente se hace constar que:
{NOMBRES APELLIDOS}
Recibió el Sacramento de la primera comunión en esta parroquia el día:
{FechaComunion}
Según consta en el libro de primeras comuniones No. {Libro} folio {Folio}

A solicitud de la parte interesada, se extiende la presente constancia, en la ciudad de
Mazatenango, {FechaEmision}                         {Párroco} / Párroco.
```
Implementado en `PlantillaComunion` (M3). Prueba golden: `tests/Parroquia.Pruebas/Constancias/Golden/comunion.txt` (datos del ejemplo; fechas con "de"). Estilos: nombre en negrita con línea inferior; fecha del sacramento en negrita y subrayada; "Según consta…" en negrita; cierre a la izquierda; firmante en negrita a la derecha. Como el ejemplo no trae "Partida", esta constancia no la imprime.

### 2.4 Matrimonio — `CERTIFICACIÓN DE MATRIMONIO`
```
El infrascrito párroco, por medio de la presente hace constar que, teniendo a la vista
el libro de Matrimonio #: {Libro} en el folio #: {Folio}, de esta parroquia
de "San Bartolomé", Mazatenango, Suchitepéquez, se encuentra la partida No. {Partida}
que literalmente dice: <<En la Iglesia de Mazatenango, Suchitepéquez en
fecha: {FechaMatrimonio}
previo los trámites de Derecho confesé y comulgué y sin ningún impedimento autoricé el
matrimonio que IN FACIE ECCLESIAE, en mi presencia y ante los testigos: {Testigo1} y {Testigo2}
contrajo {Esposo.Tratamiento}: {Esposo.NombreCompleto} de {Esposo.Edad} años, originario de: {Esposo.OriginarioDe},
feligrés de: {Esposo.FeligresDe}, hijo legítimo de: {Esposo.Padre} y {Esposo.Madre}
con {Esposa.Tratamiento} {Esposa.NombreCompleto} de {Esposa.Edad} años, originaria de: {Esposa.OriginarioDe},
feligrés de {Esposa.FeligresDe}, hija legítima de: {Esposa.Padre} y {Esposa.Madre}
Firma: {Ministro}
Al margen se lee: " {NotasMarginales | "Sin anotaciones"} ">>

Para los usos que a los interesados convenga, extiendo, firmo y sello la presente, en el
despacho de la Parroquia "San Bartolomé", Mazatenango Such, a los: {FechaEmision dd/MM/yyyy}
Ref: {Ref}

______________________
{Párroco} / Párroco.
```
Decisiones: se corrigen "antes los testigos" → "ante los testigos" y "San Barolomé" → "San Bartolomé"; el tratamiento ("don", "la Srita.", etc.) es un **campo por contrayente** tomado del catálogo `Tratamiento` (valores por defecto: "don" / "la Srita."). Pendiente 🟡: qué es **"Ref:"** (correlativo, dato libre o se elimina) — el usuario lo averiguará; hasta entonces se imprime la línea en blanco. El formato de fecha dd/MM/yyyy del cierre solo aparece en esta constancia (🟡 unificar a formato largo si el párroco lo aprueba).

**Implementado en M6** (`PlantillaMatrimonio`). Prueba golden: `tests/Parroquia.Pruebas/Constancias/Golden/matrimonio.txt` (datos del ejemplo; emisión 6/11/2026). Una línea de texto plano por párrafo:
1. La introducción ("El infrascrito párroco… que literalmente dice:") es un párrafo.
2. Todo lo que va entre `<<` y la firma es **un solo párrafo** ("<<En la {lugar} en fecha: … contrajo {trat}: {NOMBRE} de {edad} años, originario de: …, hijo legítimo de: A y B con {trat} {NOMBRE} de {edad} años, originaria de: …, feligrés de …, hija legítima de: C y D").
3. `Firma:` es una fila `Parrafo.Fila` ("Firma: {ministro}").
4. `Al margen se lee: "{notas | Sin anotaciones}">>`.
5. El párrafo "Para los usos…" con la fecha `dd/MM/yyyy`.
6. `Ref:` (en blanco).
7. Raya de firma (`DocumentoConstancia.LineaFirma`; en texto plano `______________________`), nombre y "Párroco.".

Reglas: nombres de contrayentes **tal cual el libro** (no se pasan a mayúsculas; en negrita y subrayados, como en el papel); lugar vacío → "Iglesia de Mazatenango, Suchitepéquez"; testigos y padres con `FormatoConstancia.UnirLista`; sin tratamiento se omite "don:" / "la Srita." sin dejar signos sueltos; **sin edad se omite "de N años"**; la esposa lleva "feligrés de {x}" sin dos puntos, como el papel; la errata "parriquia" del ejemplo no se reproduce (el dato es lo que digite el capturista). La legitimidad es texto fijo (pregunta abierta en `06`). Avisos antes de imprimir (por contrayente, "del esposo" / "de la esposa"): falta nombre/apellido, edad, lugar de origen, feligresía, padres; y en general fecha incompleta, faltan testigos, falta ministro, falta libro/folio/partida, "Revisar".

## 3. Reglas de plantilla
- **Una función pura por sacramento** `Render(registro, contexto) → modelo de documento`; el PDF solo maqueta. Así el texto se prueba sin renderizar PDF.
- **Pruebas golden:** con los datos de cada ejemplo, el texto resultante debe coincidir **carácter por carácter** con el esperado (tras decidir las correcciones 🟡 arriba, el archivo esperado se actualiza **conscientemente**, no por accidente).
- **Maquetación PDF:** QuestPDF (`GeneradorPdfConstancia`, genérico: maqueta cualquier `DocumentoConstancia`); validar visualmente contra la imagen del ejemplo (tamaño de recuadro del título, alineaciones, subrayado de valores) con `PARROQUIA_PDF_SALIDA` (ver `CLAUDE.md`). Validado en M3 con Comunión; el margen blanco del propio JPEG del logo alarga un poco el membrete.
- **Tipografías:** serif negrita para el membrete (Liberation Serif Bold); sans para el cuerpo (Liberation Sans Regular/Bold), parecidas a las de los ejemplos. Son recursos incrustados en el ensamblado (licencia SIL OFL 1.1 incluida en `Infraestructura/Constancias/Fuentes`), no fuentes del sistema: el PDF sale igual en cualquier equipo.
- **Instantánea:** al emitir, se guarda en `ConstanciaEmitida` el modelo de documento tal cual se imprimió (JSON, sin el logo) + hash SHA-256 del PDF. Constancia y bitácora de acceso (`ImprimirConstancia`) se confirman en el mismo guardado.
