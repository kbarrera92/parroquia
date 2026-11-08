using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Configuracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Configuracion;

public sealed class ServicioParroco(ParroquiaDbContext db, TimeProvider reloj)
{
    public const int NombreMaximo = 150;
    public const int TituloMaximo = 50;
    public const string TituloPorDefecto = "Párroco";

    public Task<ParrocoVigente?> ObtenerActualAsync(CancellationToken ct = default) =>
        db.Parrocos.FirstOrDefaultAsync(p => p.EsActual, ct);

    /// <summary>Más reciente primero.</summary>
    public Task<List<ParrocoVigente>> HistorialAsync(CancellationToken ct = default) =>
        db.Parrocos.OrderByDescending(p => p.VigenteDesde).ThenByDescending(p => p.Id).ToListAsync(ct);

    /// <summary>Registra un nuevo párroco y cierra al actual el día anterior a <paramref name="desde"/>.</summary>
    public async Task<ResultadoOperacion> RegistrarNuevoAsync(
        string? nombre, string? titulo, DateOnly desde, CancellationToken ct = default)
    {
        var (n, t, error) = Validar(nombre, titulo);
        if (error is not null) return ResultadoOperacion.Falla(error);

        if (desde > HoraGuatemala.Hoy(reloj))
            return ResultadoOperacion.Falla("La fecha de inicio no puede ser futura.");

        var actual = await ObtenerActualAsync(ct);
        if (actual is not null && desde <= actual.VigenteDesde)
            return ResultadoOperacion.Falla(
                $"La fecha de inicio debe ser posterior al inicio del párroco actual ({actual.VigenteDesde:dd/MM/yyyy}).");

        // Dos guardados en una transacción: el índice único parcial (solo un EsActual) no admite
        // que el nuevo se inserte antes de que el anterior deje de ser actual.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (actual is not null)
        {
            actual.EsActual = false;
            actual.VigenteHasta = desde.AddDays(-1);
            await db.SaveChangesAsync(ct);
        }

        db.Parrocos.Add(new ParrocoVigente { Nombre = n!, Titulo = t!, VigenteDesde = desde, EsActual = true });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ResultadoOperacion.Exito;
    }

    /// <summary>Corrige nombre o título del párroco actual (errata), sin cambiar vigencias.</summary>
    public async Task<ResultadoOperacion> CorregirActualAsync(string? nombre, string? titulo, CancellationToken ct = default)
    {
        var actual = await ObtenerActualAsync(ct);
        if (actual is null) return ResultadoOperacion.Falla("No hay un párroco vigente que corregir.");

        var (n, t, error) = Validar(nombre, titulo);
        if (error is not null) return ResultadoOperacion.Falla(error);

        actual.Nombre = n!;
        actual.Titulo = t!;
        await db.SaveChangesAsync(ct);
        return ResultadoOperacion.Exito;
    }

    private static (string? Nombre, string? Titulo, string? Error) Validar(string? nombre, string? titulo)
    {
        var n = TextoLibro.Limpiar(nombre);
        if (n is null) return (null, null, "Escriba el nombre del párroco.");
        if (n.Length > NombreMaximo) return (null, null, $"El nombre admite máximo {NombreMaximo} caracteres.");

        var t = TextoLibro.Limpiar(titulo) ?? TituloPorDefecto;
        if (t.Length > TituloMaximo) return (null, null, $"El título admite máximo {TituloMaximo} caracteres.");

        return (n, t, null);
    }
}
