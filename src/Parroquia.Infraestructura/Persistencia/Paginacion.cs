using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;

namespace Parroquia.Infraestructura.Persistencia;

public static class Paginacion
{
    public const int TamanoPorDefecto = 25;
    public const int TamanoMaximo = 200;

    /// <summary>Cuenta y devuelve una página; una página fuera de rango se ajusta a la última. La consulta debe venir ordenada.</summary>
    public static async Task<PaginaResultado<T>> PaginarAsync<T>(
        this IQueryable<T> q, int pagina, int tamano = TamanoPorDefecto, CancellationToken ct = default)
    {
        tamano = Math.Clamp(tamano, 1, TamanoMaximo);
        var total = await q.CountAsync(ct);
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)tamano));
        var actual = Math.Clamp(pagina, 1, totalPaginas);

        var items = await q.Skip((actual - 1) * tamano).Take(tamano).ToListAsync(ct);
        return new PaginaResultado<T>(items, total, actual, tamano);
    }
}
