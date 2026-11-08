using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Comun;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Catalogos;

public readonly record struct ResultadoCatalogo(bool Ok, string? Error = null)
{
    public static ResultadoCatalogo Exito => new(true);
    public static ResultadoCatalogo Falla(string error) => new(false, error);
}

/// <summary>Alta, edición y (des)activación de un catálogo. No hay borrado.</summary>
public class ServicioCatalogo<T>(ParroquiaDbContext db) where T : EntidadAuditable, ICatalogoItem, new()
{
    public const int LongitudMaxima = 150;

    public async Task<List<T>> ListarAsync(CancellationToken ct = default)
    {
        var todos = await db.Set<T>().AsNoTracking().ToListAsync(ct);
        return [.. todos.OrderBy(x => NormalizadorBusqueda.Normalizar(x.Valor), StringComparer.Ordinal)];
    }

    public async Task<ResultadoCatalogo> CrearAsync(string? valor, CancellationToken ct = default)
    {
        var error = await ValidarAsync(valor, idActual: null, ct);
        if (error is not null) return ResultadoCatalogo.Falla(error);

        db.Set<T>().Add(new T { Valor = TextoLibro.Limpiar(valor)!, Activo = true });
        await db.SaveChangesAsync(ct);
        return ResultadoCatalogo.Exito;
    }

    public async Task<ResultadoCatalogo> EditarAsync(long id, string? valor, CancellationToken ct = default)
    {
        var item = await db.Set<T>().FindAsync([id], ct);
        if (item is null) return ResultadoCatalogo.Falla("El elemento ya no existe.");

        var error = await ValidarAsync(valor, id, ct);
        if (error is not null) return ResultadoCatalogo.Falla(error);

        item.Valor = TextoLibro.Limpiar(valor)!;
        await db.SaveChangesAsync(ct);
        return ResultadoCatalogo.Exito;
    }

    public async Task<ResultadoCatalogo> CambiarActivoAsync(long id, bool activo, CancellationToken ct = default)
    {
        var item = await db.Set<T>().FindAsync([id], ct);
        if (item is null) return ResultadoCatalogo.Falla("El elemento ya no existe.");

        item.Activo = activo;
        await db.SaveChangesAsync(ct);
        return ResultadoCatalogo.Exito;
    }

    private async Task<string?> ValidarAsync(string? valor, long? idActual, CancellationToken ct)
    {
        var limpio = TextoLibro.Limpiar(valor);
        if (limpio is null) return "Escriba el nombre.";
        if (limpio.Length > LongitudMaxima) return $"Máximo {LongitudMaxima} caracteres.";

        var clave = NormalizadorBusqueda.Normalizar(limpio);
        if (clave.Length == 0) return "Escriba un nombre con letras.";

        var existentes = await db.Set<T>().AsNoTracking().ToListAsync(ct);
        var repetido = existentes.FirstOrDefault(x =>
            x.Id != idActual && NormalizadorBusqueda.Normalizar(x.Valor) == clave);

        return repetido is null ? null : $"Ya existe \"{repetido.Valor}\".";
    }
}
