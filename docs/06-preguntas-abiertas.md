# Decisiones y preguntas abiertas

Última actualización: 7-nov-2026. Las decisiones ✅ ya están reflejadas en los demás documentos.

## ✅ Resueltas (7-nov-2026; 20–23 durante M3)
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

## 🟡 Pendientes

### Bloquea M0 — datos reales de los libros
- **[B]** Fotografiar 3–5 páginas de cada libro físico (épocas distintas, con notas marginales). Verificar: ¿padres en comunión y confirmación? ¿parroquia de bautismo? ¿lugar de nacimiento? ¿ministro/obispo en confirmación? ¿máximo de padrinos y de testigos? Define las columnas finales de `02`.

### Constancias
- **Licencia de QuestPDF (antes de M8):** el párroco debe confirmar que la parroquia cumple la licencia Community (organización de beneficio público, o ingresos anuales < 1 M USD; no aplica a entidades del sector público). Detalle en `04`, ADR-06. Si no cumpliera, hay que adquirir otra licencia o cambiar de motor.
- **Fecha "de" vs "del"**: confirmar con el párroco que no le molesta el cambio respecto al papel actual (decisión 20).
- **"Ref:" en matrimonio**: qué es (correlativo, dato libre, se elimina). *El usuario lo averiguará.* Mientras tanto la línea queda en blanco.
- Matrimonio: ¿"hijo/a **legítimo/a** de" se mantiene siempre o hay hijos naturales? ¿Se necesita un campo de legitimidad?
- Matrimonio: la fecha de cierre usa formato dd/MM/yyyy; las otras constancias usan fecha larga. ¿Unificar?
- Valores iniciales del catálogo de **tratamientos** y de **ministros** (lista de sacerdotes actuales y anteriores).

### Servidor y operación (antes de M8)
- Versión exacta de Windows (si es Windows 10: ¿tiene actualizaciones de seguridad extendidas o se migra a Windows 11?). Marca y capacidad de disco, ¿hay UPS?, ¿disco externo para respaldos?, ¿dónde irá la copia fuera de sitio?
- Confirmar que no habrá acceso desde fuera de la red de la parroquia.

### Alcance futuro
- Fase 2: tarifas por tipo de constancia, recibos, quién cobra, corte diario, exoneraciones, otras constancias (solicitar redacción de cada una).
- Defunciones y anulación de matrimonio: conseguir constancias de ejemplo y datos de los libros.
- ¿Exportar listados (Excel) o estadísticas (bautizos por año)?
- ¿Obligaciones legales/diocesanas sobre cómo conservar los registros digitales? Consultar a la curia si aplica.
- Plazo objetivo para terminar la digitación histórica (sirve para planear la captura masiva).
