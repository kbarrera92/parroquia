using Parroquia.Dominio.Comun;

namespace Parroquia.Aplicacion.Constancias;

public enum Alineacion { Izquierda, Centro, Derecha }

/// <summary>Trozo de texto con estilo. Es lo único que el PDF y la vista previa necesitan para maquetar.</summary>
public sealed record Segmento(string Texto, bool Negrita = false, bool Subrayado = false);

public sealed record Parrafo(
    IReadOnlyList<Segmento> Segmentos,
    Alineacion Alineacion = Alineacion.Centro,
    int EspacioAntes = 0,
    bool LineaInferior = false)
{
    public static Parrafo De(string texto, Alineacion alineacion = Alineacion.Centro, bool negrita = false,
        bool subrayado = false, int espacioAntes = 0, bool lineaInferior = false) =>
        new([new Segmento(texto, negrita, subrayado)], alineacion, espacioAntes, lineaInferior);

    public string ATexto() => string.Concat(Segmentos.Select(s => s.Texto));
}

/// <summary>
/// Modelo de documento de una constancia: lo produce una función pura por sacramento y lo consumen
/// el PDF, la vista previa HTML, la prueba golden y la instantánea de <c>ConstanciaEmitida</c>.
/// </summary>
public sealed record DocumentoConstancia(
    IReadOnlyList<string> Membrete,
    string Titulo,
    IReadOnlyList<Parrafo> Cuerpo,
    Parrafo Cierre,
    Parrafo Firmante,
    string TituloFirmante)
{
    /// <summary>Una línea por párrafo; sin logo ni estilos. Es el texto que verifica la prueba golden.</summary>
    public string ATextoPlano() => string.Join("\n",
        Membrete.Concat([Titulo])
            .Concat(Cuerpo.Select(p => p.ATexto()))
            .Concat([Cierre.ATexto(), Firmante.ATexto(), TituloFirmante]));
}

/// <summary>Datos de emisión que no pertenecen al registro: membrete, firmante y fecha. Se arma fuera de la plantilla.</summary>
public sealed record ContextoConstancia(
    string Direccion,
    string Municipio,
    string Telefono,
    string CiudadAbreviada,
    string TextoLugarEmision,
    string FirmanteNombre,
    string FirmanteTitulo,
    DateOnly FechaEmision,
    byte[]? Logo);

/// <summary>Piezas comunes a todas las plantillas.</summary>
public static class FormatoConstancia
{
    public static IReadOnlyList<string> Membrete(ContextoConstancia c) =>
        [$"{c.Direccion} -:- Teléfono: {c.Telefono}", c.Municipio];

    /// <summary>"3 de noviembre de 2026".</summary>
    public static string FechaLarga(DateOnly fecha) =>
        new FechaParcial { Anio = (short)fecha.Year, Mes = (short)fecha.Month, Dia = (short)fecha.Day }.ATexto();

    /// <summary>Nombre y apellidos en mayúsculas, sin tocar tildes ni letras del libro.</summary>
    public static string NombreEnMayusculas(string? nombres, string? apellidos) =>
        string.Join(' ', new[] { nombres, apellidos }
            .Select(TextoLibro.Limpiar)
            .Where(t => t is not null))
            .ToUpperInvariant();

    public static string Titulo(string firmanteTitulo) => $"{firmanteTitulo.TrimEnd('.')}.";
}
