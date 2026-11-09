# PRD — Fase 1: Digitalización de registros sacramentales

## 1. Objetivo
Que dos empleados digiten los libros físicos de la Parroquia San Bartolomé (Catedral de Mazatenango) para que (a) los sacramentos nuevos nazcan digitales y (b) los antiguos queden disponibles para **reimprimir constancias** a solicitud del feligrés.

## 2. Usuarios y roles
| Rol | Quién | Puede |
|---|---|---|
| **Capturista** | Los empleados que digitan | Buscar y ver registros; **crear, editar y anular** (anulación lógica, con motivo). **No** imprime constancias |
| **Secretaria** | Quien atiende al feligrés | Buscar y ver registros (solo lectura); **imprimir y reimprimir** constancias. No crea ni edita |
| **Administrador** | Párroco / encargado designado | Todo lo anterior + usuarios, configuración, catálogos, ver historial y bitácora, cambiar fecha de emisión de una constancia |

Todo lo que cualquier rol haga (ver, crear, editar, anular, imprimir, iniciar sesión, cambios de permisos) deja **bitácora** para auditorías.

## 3. Alcance
**Dentro:**
1. Formularios de captura/edición: Bautismo, Confirmación, Comunión, Matrimonio.
2. Fechas parciales y marca "Revisar" (datos antiguos dudosos).
3. Búsqueda por persona (sin distinguir tildes/mayúsculas) por sacramento y global.
4. Vínculo opcional entre registros de la misma persona.
5. Constancia en PDF por sacramento, **fiel al formato actual** (`referencias/`).
6. Bitácora de constancias impresas (quién, cuándo, qué registro).
7. Usuarios, roles, historial completo de cambios.
8. Respaldos automáticos y guía de restauración.

**Fuera (Fase 2+):** solicitudes, cobros e ingresos, reportes financieros, **defunciones y anulación de matrimonio** (módulos pendientes: aún sin constancias), otras constancias con redacción distinta, escaneos de páginas, acceso desde internet, firma electrónica.

## 4. Requisitos funcionales

**RF-01 Captura.** Un formulario por sacramento con los campos de `02-modelo-de-datos.md`. Navegación completa por teclado (Tab/Enter), foco inicial en Libro, mensajes de validación en español.

**RF-02 Agilidad de captura masiva.** "Guardar y nuevo" conserva Libro (y sugiere Folio/Partida siguiente editable) para no reescribir en cada registro. Las partidas se digitan en el orden del libro.

**RF-03 Fechas parciales.** Cada fecha admite año / año-mes / fecha completa / vacía. Se ordena y filtra correctamente. La constancia imprime lo que exista y **advierte** al imprimir si falta un dato requerido.

**RF-04 Marca "Revisar".** Casilla + motivo opcional por registro. Listado filtrable "Pendientes de revisar". Imprimir un registro marcado exige confirmación explícita.

**RF-05 Duplicados.** Al guardar, advertir (no bloquear) si existe otro registro del mismo sacramento con igual Libro+Folio+Partida, o igual persona+fecha.

**RF-06 Búsqueda.** Por nombres y/o apellidos (parcial, sin tildes ni mayúsculas, tolerante a "de"), filtro por sacramento, rango de fechas, libro/folio. Resultado ordenado por apellidos, nombres. Matrimonio se encuentra por cualquiera de los dos contrayentes. Respuesta <1 s con ≥100 000 registros.

**RF-07 Vínculos opcionales.** Desde un registro, "Vincular con…" busca otro registro y los relaciona como misma persona. Desde cualquiera se ven los vinculados. Quitar un vínculo no borra registros.

**RF-08 Constancia PDF.** Una plantilla por sacramento (`03-constancias.md`), con membrete, texto, fecha de emisión actual y firmante. Vista previa → imprimir/descargar. Solo Secretaria y Administrador imprimen. La fecha de emisión es la de hoy; solo el Administrador puede cambiarla (queda en bitácora). Firma siempre el párroco vigente. Cada emisión crea una entrada en la bitácora con instantánea del contenido.

**RF-09 Historial.** Todo alta/modificación/anulación guarda usuario, fecha-hora y valores anterior/nuevo por campo. Consultable por registro. Los registros **no se borran físicamente**: el Capturista (o Administrador) los anula con motivo, y la anulación se puede revertir solo por Administrador.

**RF-10 Usuarios.** Inicio de sesión individual, contraseñas con hash, bloqueo por intentos fallidos, cierre por inactividad, cambio de contraseña. Administrador crea, edita (nombre y rol; un rol por usuario), desactiva, desbloquea y restablece la contraseña de los usuarios (pantalla *Administración → Usuarios*). **No hay recuperación por correo** (sin internet): el Administrador genera una contraseña temporal que se muestra una sola vez y que el usuario debe cambiar al entrar. Política de contraseñas y demás controles: `04` ADR-05.

**RF-11 Configuración y catálogos.** Nombre de parroquia, dirección, teléfono, logo, párroco vigente (con historial de vigencia), texto fijo del membrete. Catálogo de **ministros** (sacerdotes; el campo permite elegir de la lista o escribir otro nombre) y de **tratamientos** de contrayentes.

**RF-12 Concurrencia.** Si dos usuarios editan el mismo registro, el segundo recibe aviso de conflicto sin perder lo digitado.

## 5. Requisitos no funcionales
- **Disponibilidad:** PC dedicada con Windows 10/11 encendida 24/7; uso en horario de oficina; debe funcionar sin internet.
- **Seguridad:** solo red local; HTTPS interno; sin exposición a internet; datos personales y sacramentales sensibles → mínimo privilegio y auditoría.
- **Respaldo:** diario automático, retención ≥30 días, copia externa; restauración probada.
- **Rendimiento:** listas paginadas; búsqueda indexada (sin tildes) sobre cientos de miles de filas.
- **Usabilidad:** pantalla única de búsqueda en el inicio; textos grandes y legibles; mínimo de clics para capturar.
- **Mantenibilidad:** stack mainstream, módulo por sacramento, pruebas automáticas, docs al día.
- **Idioma/formato:** español de Guatemala; fechas "13 de agosto de 1978"; zona `America/Guatemala`.

## 6. Criterios de aceptación de Fase 1
1. Cada constancia generada coincide con su ejemplo de `referencias/` (texto y estructura) al usar los mismos datos.
2. Un empleado captura un registro nuevo de cada sacramento sin asistencia.
3. Buscar "hidalgo arevalo" encuentra a *Juan Carlos Hidalgo Arévalo* en los libros donde exista.
4. Toda edición queda en el historial con usuario y valores anterior/nuevo.
5. Reimprimir deja entrada en la bitácora.
6. Respaldo restaurado en otra máquina abre con todos los datos.
7. Piloto: 20 registros reales por sacramento capturados y constancias validadas por el párroco.
