using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Sacramentos;

/// <summary>
/// Uno de los dos contrayentes. Se mapea como complex type (columnas <c>esposo_*</c> / <c>esposa_*</c>), igual que
/// <see cref="FechaParcial"/>: así el interceptor de auditoría lo registra campo por campo ("Esposo.Nombres").
/// Todo se guarda tal cual el libro.
/// </summary>
public class Contrayente
{
    /// <summary>Texto del catálogo de tratamientos tal como se imprime ("don", "la Srita."). Se guarda el texto, no el Id.</summary>
    public string? Tratamiento { get; set; }

    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public short? Edad { get; set; }
    public string? OriginarioDe { get; set; }
    public string? FeligresDe { get; set; }
    public string? Padre { get; set; }
    public string? Madre { get; set; }

    /// <summary>Nombres y apellidos normalizados para buscar (sin tildes ni partículas).</summary>
    public string TextoBusqueda() => NormalizadorBusqueda.Normalizar(Nombres, Apellidos);
}

/// <summary>
/// Matrimonio (docs/02 §3.2). Dos contrayentes, testigos (lista de texto 0..n en el orden del libro, <c>text[]</c>,
/// auditada unida con "; "), lugar de celebración y fecha parcial. Ministro y notas vienen de <see cref="RegistroSacramental"/>.
/// </summary>
public class Matrimonio : RegistroSacramental
{
    public const string LugarPorDefecto = "Iglesia de Mazatenango, Suchitepéquez";

    // Los complex types no pueden ser null: siempre existen (vacíos hasta que se llenan).
    public Contrayente Esposo { get; set; } = new();
    public Contrayente Esposa { get; set; } = new();

    public FechaParcial FechaMatrimonio { get; set; } = FechaParcial.Vacia;
    public string LugarCelebracion { get; set; } = LugarPorDefecto;

    /// <summary>Asignar una lista nueva al cambiarla (así EF y la auditoría detectan el cambio).</summary>
    public List<string> Testigos { get; set; } = [];

    // Derivadas (no se auditan): permiten buscar por cada contrayente por separado.
    public string BusquedaEsposo { get; private set; } = "";
    public string BusquedaEsposa { get; private set; } = "";

    /// <summary>Recalcula las búsquedas por contrayente y devuelve ambas juntas (la columna común del registro).</summary>
    protected override string TextoBusqueda()
    {
        BusquedaEsposo = Esposo.TextoBusqueda();
        BusquedaEsposa = Esposa.TextoBusqueda();
        return $"{BusquedaEsposo} {BusquedaEsposa}".Trim();
    }
}
