using Parroquia.Aplicacion.Constancias;
using Parroquia.Dominio.Comun;

// Namespace plano por capa (la carpeta separa los módulos): un namespace ".Bautismo" chocaría con la entidad Bautismo.
namespace Parroquia.Aplicacion.Sacramentos;

/// <summary>Datos del registro que usa la constancia de Bautismo.</summary>
public sealed record DatosBautismo(
    string? Nombres, string? Apellidos, FechaParcial FechaBautismo, FechaParcial FechaNacimiento,
    string? Padre, string? Madre, IReadOnlyList<string> Padrinos, string? Ministro, string? NotasMarginales,
    string? Libro, string? Folio, bool Revisar);

/// <summary>
/// Texto de la constancia de Bautismo (docs/03 §2.1). Funciones puras: sin base de datos ni reloj.
/// Cambiar la redacción exige actualizar docs/03 y la prueba golden.
/// </summary>
public static class PlantillaBautismo
{
    public const string Titulo = "CERTIFICACIÓN DE BAUTISMO";
    public const string SinAnotaciones = "Sin anotaciones";

    public static DocumentoConstancia Render(DatosBautismo d, ContextoConstancia c)
    {
        var libro = TextoLibro.Limpiar(d.Libro) ?? "";
        var folio = TextoLibro.Limpiar(d.Folio) ?? "";

        var bautizado = d.FechaBautismo.ATexto();
        var nacimiento = d.FechaNacimiento.ATexto();
        if (nacimiento.Length > 0) bautizado = $"{bautizado}, Nació el {nacimiento}";

        return new DocumentoConstancia(
            Membrete: FormatoConstancia.Membrete(c),
            Titulo: Titulo,
            Cuerpo:
            [
                Parrafo.De(
                    $"El infrascrito Párroco, certifica tener a la vista el Libro Bautizo No. {libro} Folio {folio} " +
                    "en el que aparece inscrito el Bautismo de:", Alineacion.Izquierda),
                Parrafo.De(FormatoConstancia.NombreEnMayusculas(d.Nombres, d.Apellidos), negrita: true, espacioAntes: 4),
                Parrafo.Fila("Bautizado el día:", bautizado),
                Parrafo.Fila("hijo (a) de:", FormatoConstancia.UnirLista([d.Padre, d.Madre])),
                Parrafo.Fila("Sus padrinos fueron:", FormatoConstancia.UnirLista(d.Padrinos)),
                Parrafo.Fila("Firma de ministro:", TextoLibro.Limpiar(d.Ministro) ?? ""),
                Parrafo.Fila("Al margen se lee:", NotasOSinAnotaciones(d.NotasMarginales)),
            ],
            Cierre: Parrafo.De($"{c.CiudadAbreviada}, {FormatoConstancia.FechaLarga(c.FechaEmision)}",
                Alineacion.Izquierda, espacioAntes: 40),
            Firmante: Parrafo.De(c.FirmanteNombre, Alineacion.Derecha, negrita: true, espacioAntes: 60),
            TituloFirmante: FormatoConstancia.Titulo(c.FirmanteTitulo));
    }

    /// <summary>Avisos para mostrar en pantalla antes de imprimir. El PDF nunca los imprime.</summary>
    public static IReadOnlyList<string> Advertencias(DatosBautismo d)
    {
        var avisos = new List<string>();

        if (TextoLibro.Limpiar(d.Nombres) is null || TextoLibro.Limpiar(d.Apellidos) is null)
            avisos.Add("Falta el nombre o el apellido de la persona.");
        if (!d.FechaBautismo.EsCompleta)
            avisos.Add("La fecha del bautismo está incompleta.");
        if (!d.FechaNacimiento.EsCompleta)
            avisos.Add("La fecha de nacimiento falta o está incompleta.");
        if (TextoLibro.Limpiar(d.Padre) is null && TextoLibro.Limpiar(d.Madre) is null)
            avisos.Add("Faltan los padres.");
        if (!d.Padrinos.Any(p => TextoLibro.Limpiar(p) is not null))
            avisos.Add("Faltan los padrinos.");
        if (TextoLibro.Limpiar(d.Ministro) is null)
            avisos.Add("Falta el ministro que bautizó.");
        if (TextoLibro.Limpiar(d.Libro) is null)
            avisos.Add("Falta el número de libro.");
        if (TextoLibro.Limpiar(d.Folio) is null)
            avisos.Add("Falta el folio.");
        if (d.Revisar)
            avisos.Add("El registro está marcado para revisar; confirme antes de imprimir.");

        return avisos;
    }

    private static string NotasOSinAnotaciones(string? notas) =>
        string.IsNullOrWhiteSpace(notas) ? SinAnotaciones : notas.Trim();
}
