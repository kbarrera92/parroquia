using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Sacramentos;

/// <summary>
/// Confirmación (docs/02 §3.2). El ministro (obispo) y las notas marginales vienen de <see cref="RegistroSacramental"/>;
/// no se imprimen en la constancia. Los padrinos son una lista de texto (0..n) en el orden del libro;
/// se guardan como <c>text[]</c> y se auditan unidos con "; ".
/// </summary>
public class Confirmacion : RegistroSacramental
{
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public FechaParcial FechaConfirmacion { get; set; } = FechaParcial.Vacia;

    /// <summary>Asignar una lista nueva al cambiarla (así EF y la auditoría detectan el cambio).</summary>
    public List<string> Padrinos { get; set; } = [];

    protected override string TextoBusqueda() => NormalizadorBusqueda.Normalizar(Nombres, Apellidos);
}
