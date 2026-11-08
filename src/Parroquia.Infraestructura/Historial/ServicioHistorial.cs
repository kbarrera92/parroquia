using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Historial;

/// <param name="Usuario">Contiene (sin distinguir mayúsculas).</param>
/// <param name="Entidad">Coincidencia exacta del nombre de entidad.</param>
/// <param name="Desde">Día inclusive, hora de Guatemala.</param>
/// <param name="Hasta">Día inclusive, hora de Guatemala.</param>
public sealed record FiltroHistorial(string? Usuario = null, string? Entidad = null, DateOnly? Desde = null, DateOnly? Hasta = null);

/// <summary>Consulta (solo lectura) de <see cref="Auditoria"/> y <see cref="BitacoraAcceso"/>, más reciente primero.</summary>
public sealed class ServicioHistorial(ParroquiaDbContext db)
{
    public const int TamanoPorDefecto = Paginacion.TamanoPorDefecto;

    public Task<PaginaResultado<Auditoria>> AuditoriaAsync(
        FiltroHistorial filtro, int pagina, int tamano = TamanoPorDefecto, CancellationToken ct = default)
    {
        var q = db.Auditorias.AsNoTracking().AsQueryable();
        q = Filtrar(q, filtro, a => a.UsuarioNombre, a => a.Entidad, a => a.Fecha);
        return q.OrderByDescending(a => a.Fecha).ThenByDescending(a => a.Id).PaginarAsync(pagina, tamano, ct);
    }

    /// <summary>Todos los cambios de un registro (alta, ediciones, anulaciones), el más reciente primero.</summary>
    public Task<List<Auditoria>> DeRegistroAsync(string entidad, long id, CancellationToken ct = default) =>
        db.Auditorias.AsNoTracking()
            .Where(a => a.Entidad == entidad && a.EntidadId == id)
            .OrderByDescending(a => a.Fecha).ThenByDescending(a => a.Id)
            .ToListAsync(ct);

    public Task<PaginaResultado<BitacoraAcceso>> BitacoraAsync(
        FiltroHistorial filtro, int pagina, int tamano = TamanoPorDefecto, CancellationToken ct = default)
    {
        var q = db.BitacoraAccesos.AsNoTracking().AsQueryable();
        q = Filtrar(q, filtro, b => b.UsuarioNombre, b => b.Entidad, b => b.Fecha);
        return q.OrderByDescending(b => b.Fecha).ThenByDescending(b => b.Id).PaginarAsync(pagina, tamano, ct);
    }

    private static IQueryable<T> Filtrar<T>(
        IQueryable<T> q, FiltroHistorial f,
        System.Linq.Expressions.Expression<Func<T, string>> usuario,
        System.Linq.Expressions.Expression<Func<T, string?>> entidad,
        System.Linq.Expressions.Expression<Func<T, DateTimeOffset>> fecha)
    {
        if (!string.IsNullOrWhiteSpace(f.Usuario))
        {
            var patron = $"%{EscaparLike(f.Usuario.Trim())}%";
            q = q.Where(Combinar(usuario, u => EF.Functions.ILike(u, patron, "\\")));
        }

        if (!string.IsNullOrWhiteSpace(f.Entidad))
        {
            var e = f.Entidad.Trim();
            q = q.Where(Combinar(entidad, x => x == e));
        }

        if (f.Desde is { } desde)
        {
            var inicio = HoraGuatemala.InicioDelDia(desde);
            q = q.Where(Combinar(fecha, x => x >= inicio));
        }

        if (f.Hasta is { } hasta)
        {
            var finExclusivo = HoraGuatemala.InicioDelDia(hasta.AddDays(1));
            q = q.Where(Combinar(fecha, x => x < finExclusivo));
        }

        return q;
    }

    /// <summary>selector(x) → predicado, como una sola expresión traducible a SQL.</summary>
    private static System.Linq.Expressions.Expression<Func<T, bool>> Combinar<T, TProp>(
        System.Linq.Expressions.Expression<Func<T, TProp>> selector,
        System.Linq.Expressions.Expression<Func<TProp, bool>> predicado)
    {
        var p = selector.Parameters[0];
        var cuerpo = new ReemplazarParametro(predicado.Parameters[0], selector.Body).Visit(predicado.Body)!;
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(cuerpo, p);
    }

    private sealed class ReemplazarParametro(
        System.Linq.Expressions.ParameterExpression viejo, System.Linq.Expressions.Expression nuevo)
        : System.Linq.Expressions.ExpressionVisitor
    {
        protected override System.Linq.Expressions.Expression VisitParameter(System.Linq.Expressions.ParameterExpression node) =>
            node == viejo ? nuevo : base.VisitParameter(node);
    }

    private static string EscaparLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
