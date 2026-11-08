using Parroquia.Aplicacion.Constancias;
using Parroquia.Dominio.Comun;

namespace Parroquia.Aplicacion.Sacramentos.Comunion;

/// <summary>Datos del registro que usa la constancia de Comunión.</summary>
public sealed record DatosComunion(
    string? Nombres, string? Apellidos, FechaParcial FechaComunion, string? Libro, string? Folio, bool Revisar);

/// <summary>
/// Texto de la constancia de Primera Comunión (docs/03 §2.3). Funciones puras: sin base de datos ni reloj.
/// Cambiar la redacción exige actualizar docs/03 y la prueba golden.
/// </summary>
public static class PlantillaComunion
{
    public const string Titulo = "CERTIFICACIÓN DE COMUNIÓN";

    public static DocumentoConstancia Render(DatosComunion d, ContextoConstancia c)
    {
        var libro = TextoLibro.Limpiar(d.Libro) ?? "";
        var folio = TextoLibro.Limpiar(d.Folio) ?? "";

        return new DocumentoConstancia(
            Membrete: FormatoConstancia.Membrete(c),
            Titulo: Titulo,
            Cuerpo:
            [
                Parrafo.De("Por la presente se hace constar que:"),
                Parrafo.De(FormatoConstancia.NombreEnMayusculas(d.Nombres, d.Apellidos), negrita: true, lineaInferior: true, espacioAntes: 6),
                Parrafo.De("Recibió el Sacramento de la primera comunión en esta parroquia el día:"),
                Parrafo.De(d.FechaComunion.ATexto(), negrita: true, subrayado: true),
                Parrafo.De($"Según consta en el libro de primeras comuniones No. {libro} folio {folio}", negrita: true),
            ],
            Cierre: Parrafo.De(
                $"A solicitud de la parte interesada, se extiende la presente constancia, en la ciudad de {c.CiudadAbreviada}, {FormatoConstancia.FechaLarga(c.FechaEmision)}",
                Alineacion.Izquierda, espacioAntes: 40),
            Firmante: Parrafo.De(c.FirmanteNombre, Alineacion.Derecha, negrita: true, espacioAntes: 60),
            TituloFirmante: FormatoConstancia.Titulo(c.FirmanteTitulo));
    }

    /// <summary>Avisos para mostrar en pantalla antes de imprimir. El PDF nunca los imprime.</summary>
    public static IReadOnlyList<string> Advertencias(DatosComunion d)
    {
        var avisos = new List<string>();

        if (TextoLibro.Limpiar(d.Nombres) is null || TextoLibro.Limpiar(d.Apellidos) is null)
            avisos.Add("Falta el nombre o el apellido de la persona.");
        if (!d.FechaComunion.EsCompleta)
            avisos.Add("La fecha de la comunión está incompleta.");
        if (TextoLibro.Limpiar(d.Libro) is null)
            avisos.Add("Falta el número de libro.");
        if (TextoLibro.Limpiar(d.Folio) is null)
            avisos.Add("Falta el folio.");
        if (d.Revisar)
            avisos.Add("El registro está marcado para revisar; confirme antes de imprimir.");

        return avisos;
    }
}
