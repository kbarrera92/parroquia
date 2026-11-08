using System.Globalization;
using Parroquia.Aplicacion.Constancias;
using Parroquia.Dominio.Comun;

// Namespace plano por capa (la carpeta separa los módulos): un namespace ".Matrimonio" chocaría con la entidad Matrimonio.
namespace Parroquia.Aplicacion.Sacramentos;

/// <summary>Un contrayente tal como lo imprime la constancia. El tratamiento ("don", "la Srita.") ya viene como texto.</summary>
public sealed record DatosContrayente(
    string? Tratamiento, string? Nombres, string? Apellidos, short? Edad,
    string? OriginarioDe, string? FeligresDe, string? Padre, string? Madre);

/// <summary>Datos del registro que usa la constancia de Matrimonio.</summary>
public sealed record DatosMatrimonio(
    DatosContrayente Esposo, DatosContrayente Esposa, FechaParcial FechaMatrimonio, string? LugarCelebracion,
    IReadOnlyList<string> Testigos, string? Ministro, string? NotasMarginales,
    string? Libro, string? Folio, string? Partida, bool Revisar);

/// <summary>
/// Texto de la constancia de Matrimonio (docs/03 §2.4). Funciones puras: sin base de datos ni reloj.
/// Cambiar la redacción exige actualizar docs/03 y la prueba golden.
/// </summary>
public static class PlantillaMatrimonio
{
    public const string Titulo = "CERTIFICACIÓN DE MATRIMONIO";
    public const string SinAnotaciones = "Sin anotaciones";
    public const string LugarPorDefecto = "Iglesia de Mazatenango, Suchitepéquez";

    public static DocumentoConstancia Render(DatosMatrimonio d, ContextoConstancia c)
    {
        var libro = TextoLibro.Limpiar(d.Libro) ?? "";
        var folio = TextoLibro.Limpiar(d.Folio) ?? "";
        var partida = TextoLibro.Limpiar(d.Partida) ?? "";
        var lugar = TextoLibro.Limpiar(d.LugarCelebracion) ?? LugarPorDefecto;

        var introduccion = new Parrafo(
            [
                Plano("El infrascrito párroco, por medio de la presente hace constar que, teniendo a la vista el libro de Matrimonio #: "),
                Valor(libro), Plano(" en el folio #: "), Valor(folio),
                Plano(", de esta parroquia de \"San Bartolomé\", Mazatenango, Suchitepéquez, se encuentra la partida No. "),
                Valor(partida), Plano(" que literalmente dice:"),
            ],
            Alineacion.Izquierda);

        List<Segmento> partidaLiteral =
        [
            Plano($"<<En la {lugar} en fecha: "), Valor(d.FechaMatrimonio.ATexto()),
            Plano(" previo los trámites de Derecho confesé y comulgué y sin ningún impedimento autoricé el matrimonio que " +
                  "IN FACIE ECCLESIAE, en mi presencia y ante los testigos: "),
            Valor(FormatoConstancia.UnirLista(d.Testigos)),
        ];
        partidaLiteral.AddRange(Contrayente(d.Esposo, esposo: true));
        partidaLiteral.AddRange(Contrayente(d.Esposa, esposo: false));

        var cierre = new Parrafo(
            [
                Plano("Para los usos que a los interesados convenga, extiendo, firmo y sello la presente, en el " +
                      "despacho de la Parroquia \"San Bartolomé\", Mazatenango Such, a los: "),
                Valor(c.FechaEmision.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
            ],
            Alineacion.Izquierda, EspacioAntes: 24);

        return new DocumentoConstancia(
            Membrete: FormatoConstancia.Membrete(c),
            Titulo: Titulo,
            Cuerpo:
            [
                introduccion,
                new Parrafo(partidaLiteral, Alineacion.Izquierda),
                Parrafo.Fila("Firma:", TextoLibro.Limpiar(d.Ministro) ?? ""),
                new Parrafo(
                    [Plano("Al margen se lee: \""), Valor(NotasOSinAnotaciones(d.NotasMarginales)), Plano("\">>")],
                    Alineacion.Izquierda),
                cierre,
            ],
            // "Ref:" sigue en blanco hasta resolver qué es (docs/06, pendiente).
            Cierre: Parrafo.De("Ref:", Alineacion.Izquierda, espacioAntes: 16),
            Firmante: Parrafo.De(c.FirmanteNombre, Alineacion.Derecha, negrita: true, espacioAntes: 40),
            TituloFirmante: FormatoConstancia.Titulo(c.FirmanteTitulo),
            LineaFirma: true);
    }

    /// <summary>
    /// "contrajo don: NOMBRE de 29 años, originario de: X, feligrés de: Y, hijo legítimo de: A y B" para el esposo;
    /// " con la Srita. NOMBRE de 22 años, originaria de: X, feligrés de Y, hija legítima de: A y B" para la esposa
    /// (la redacción del papel no lleva ":" tras "feligrés de" en la esposa).
    /// </summary>
    private static IEnumerable<Segmento> Contrayente(DatosContrayente c, bool esposo)
    {
        yield return Plano(esposo ? " contrajo " : " con ");

        if (TextoLibro.Limpiar(c.Tratamiento) is { } tratamiento)
        {
            yield return Valor(tratamiento);
            yield return Plano(esposo ? ": " : " ");
        }

        yield return new Segmento(NombreCompleto(c), Negrita: true, Subrayado: true);

        if (c.Edad is { } edad)
        {
            yield return Plano(" de ");
            yield return Valor(edad.ToString(CultureInfo.InvariantCulture));
            yield return Plano(" años");
        }

        yield return Plano(esposo ? ", originario de: " : ", originaria de: ");
        yield return Valor(TextoLibro.Limpiar(c.OriginarioDe) ?? "");
        yield return Plano(esposo ? ", feligrés de: " : ", feligrés de ");
        yield return Valor(TextoLibro.Limpiar(c.FeligresDe) ?? "");
        yield return Plano(esposo ? ", hijo legítimo de: " : ", hija legítima de: ");
        yield return Valor(FormatoConstancia.UnirLista([c.Padre, c.Madre]));
    }

    /// <summary>Avisos para mostrar en pantalla antes de imprimir. El PDF nunca los imprime.</summary>
    public static IReadOnlyList<string> Advertencias(DatosMatrimonio d)
    {
        var avisos = new List<string>();

        AvisosContrayente(avisos, d.Esposo, "del esposo");
        AvisosContrayente(avisos, d.Esposa, "de la esposa");

        if (!d.FechaMatrimonio.EsCompleta)
            avisos.Add("La fecha del matrimonio está incompleta.");
        if (!d.Testigos.Any(t => TextoLibro.Limpiar(t) is not null))
            avisos.Add("Faltan los testigos.");
        if (TextoLibro.Limpiar(d.Ministro) is null)
            avisos.Add("Falta el ministro que firma.");
        if (TextoLibro.Limpiar(d.Libro) is null)
            avisos.Add("Falta el número de libro.");
        if (TextoLibro.Limpiar(d.Folio) is null)
            avisos.Add("Falta el folio.");
        if (TextoLibro.Limpiar(d.Partida) is null)
            avisos.Add("Falta la partida.");
        if (d.Revisar)
            avisos.Add("El registro está marcado para revisar; confirme antes de imprimir.");

        return avisos;
    }

    private static void AvisosContrayente(List<string> avisos, DatosContrayente c, string quien)
    {
        if (TextoLibro.Limpiar(c.Nombres) is null || TextoLibro.Limpiar(c.Apellidos) is null)
            avisos.Add($"Falta el nombre o el apellido {quien}.");
        if (c.Edad is null)
            avisos.Add($"Falta la edad {quien}.");
        if (TextoLibro.Limpiar(c.OriginarioDe) is null)
            avisos.Add($"Falta el lugar de origen {quien}.");
        if (TextoLibro.Limpiar(c.FeligresDe) is null)
            avisos.Add($"Falta la feligresía {quien}.");
        if (TextoLibro.Limpiar(c.Padre) is null && TextoLibro.Limpiar(c.Madre) is null)
            avisos.Add($"Faltan los padres {quien}.");
    }

    /// <summary>Nombres y apellidos tal cual el libro (sin cambiar mayúsculas ni tildes).</summary>
    private static string NombreCompleto(DatosContrayente c) =>
        string.Join(' ', new[] { c.Nombres, c.Apellidos }.Select(TextoLibro.Limpiar).OfType<string>());

    private static Segmento Plano(string texto) => new(texto);
    private static Segmento Valor(string texto) => new(texto, Subrayado: true);

    private static string NotasOSinAnotaciones(string? notas) =>
        string.IsNullOrWhiteSpace(notas) ? SinAnotaciones : notas.Trim();
}
