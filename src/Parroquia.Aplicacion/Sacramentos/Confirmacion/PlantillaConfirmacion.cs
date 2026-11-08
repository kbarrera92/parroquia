using Parroquia.Aplicacion.Constancias;
using Parroquia.Dominio.Comun;

// Namespace plano por capa (la carpeta separa los módulos): un namespace ".Confirmacion" chocaría con la entidad Confirmacion.
namespace Parroquia.Aplicacion.Sacramentos;

/// <summary>Datos del registro que usa la constancia de Confirmación.</summary>
public sealed record DatosConfirmacion(
    string? Nombres, string? Apellidos, FechaParcial FechaConfirmacion, IReadOnlyList<string> Padrinos,
    string? Libro, string? Folio, bool Revisar);

/// <summary>
/// Texto de la constancia de Confirmación (docs/03 §2.2). Funciones puras: sin base de datos ni reloj.
/// Cambiar la redacción exige actualizar docs/03 y la prueba golden.
/// </summary>
public static class PlantillaConfirmacion
{
    public const string Titulo = "CERTIFICACIÓN DE CONFIRMACIÓN";

    public static DocumentoConstancia Render(DatosConfirmacion d, ContextoConstancia c)
    {
        var libro = TextoLibro.Limpiar(d.Libro) ?? "";
        var folio = TextoLibro.Limpiar(d.Folio) ?? "";

        return new DocumentoConstancia(
            Membrete: FormatoConstancia.Membrete(c),
            Titulo: Titulo,
            Cuerpo:
            [
                Parrafo.De("Por la presente se hace constar que:", Alineacion.Izquierda),
                Parrafo.De(FormatoConstancia.NombreEnMayusculas(d.Nombres, d.Apellidos), negrita: true, espacioAntes: 6),
                Parrafo.De("Recibió el Sacramento de la CONFIRMACIÓN en esta parroquia el día:"),
                Parrafo.De(d.FechaConfirmacion.ATexto(), negrita: true, espacioAntes: 6),
                Parrafo.De("Sus padrinos fueron:", espacioAntes: 6),
                Parrafo.De(FormatoConstancia.UnirLista(d.Padrinos), negrita: true),
                Parrafo.De($"Según consta en el libro de Confirmación No. {libro} folio {folio}", negrita: true, espacioAntes: 6),
            ],
            Cierre: Parrafo.De($"{c.TextoLugarEmision} {FormatoConstancia.FechaLarga(c.FechaEmision)},",
                Alineacion.Izquierda, espacioAntes: 40),
            Firmante: Parrafo.De(c.FirmanteNombre, Alineacion.Derecha, negrita: true, espacioAntes: 60),
            TituloFirmante: FormatoConstancia.Titulo(c.FirmanteTitulo));
    }

    /// <summary>Avisos para mostrar en pantalla antes de imprimir. El PDF nunca los imprime.</summary>
    public static IReadOnlyList<string> Advertencias(DatosConfirmacion d)
    {
        var avisos = new List<string>();

        if (TextoLibro.Limpiar(d.Nombres) is null || TextoLibro.Limpiar(d.Apellidos) is null)
            avisos.Add("Falta el nombre o el apellido de la persona.");
        if (!d.FechaConfirmacion.EsCompleta)
            avisos.Add("La fecha de la confirmación está incompleta.");
        if (!d.Padrinos.Any(p => TextoLibro.Limpiar(p) is not null))
            avisos.Add("Faltan los padrinos.");
        if (TextoLibro.Limpiar(d.Libro) is null)
            avisos.Add("Falta el número de libro.");
        if (TextoLibro.Limpiar(d.Folio) is null)
            avisos.Add("Falta el folio.");
        if (d.Revisar)
            avisos.Add("El registro está marcado para revisar; confirme antes de imprimir.");

        return avisos;
    }
}
