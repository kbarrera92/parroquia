namespace Parroquia.Aplicacion.Comun;

public sealed record PaginaResultado<T>(IReadOnlyList<T> Items, int Total, int PaginaActual, int TamanoPagina)
{
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(Total / (double)TamanoPagina));
}
