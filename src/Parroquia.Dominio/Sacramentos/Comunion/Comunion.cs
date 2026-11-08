using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Sacramentos;

/// <summary>
/// Primera Comunión. Campos mínimos (lo que imprime la constancia); padres y parroquia de bautismo
/// se agregan, si el libro los trae, después del análisis de libros reales (M0).
/// </summary>
public class Comunion : RegistroSacramental
{
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public FechaParcial FechaComunion { get; set; } = FechaParcial.Vacia;

    protected override string TextoBusqueda() => NormalizadorBusqueda.Normalizar(Nombres, Apellidos);
}
