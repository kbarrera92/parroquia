using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Sacramentos;

/// <summary>
/// Bautismo (docs/02 §3.2). El ministro que bautizó y las notas marginales vienen de <see cref="RegistroSacramental"/>.
/// Los padrinos son una lista de texto (0..n) en el orden del libro; se guardan como <c>text[]</c> y se auditan unidos con "; ".
/// </summary>
public class Bautismo : RegistroSacramental
{
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public FechaParcial FechaBautismo { get; set; } = FechaParcial.Vacia;
    public FechaParcial FechaNacimiento { get; set; } = FechaParcial.Vacia;

    /// <summary>Opcional; no se imprime en la constancia.</summary>
    public string? LugarNacimiento { get; set; }

    public string? Padre { get; set; }
    public string? Madre { get; set; }

    /// <summary>Asignar una lista nueva al cambiarla (así EF y la auditoría detectan el cambio).</summary>
    public List<string> Padrinos { get; set; } = [];

    protected override string TextoBusqueda() => NormalizadorBusqueda.Normalizar(Nombres, Apellidos);
}
