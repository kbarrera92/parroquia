using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Catalogos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Catalogos;

/// <summary>Valores iniciales de catálogos (docs/03). Solo siembra si la tabla está vacía; después manda el Administrador.</summary>
public static class SembradorCatalogos
{
    public static readonly string[] TratamientosPorDefecto = ["don", "la Srita."];

    // Ministros: sin siembra. La lista de sacerdotes la define la parroquia (docs/06).

    public static async Task InicializarAsync(ParroquiaDbContext db, CancellationToken ct = default)
    {
        if (await db.Tratamientos.AnyAsync(ct)) return;

        db.Tratamientos.AddRange(TratamientosPorDefecto.Select(t => new Tratamiento { Texto = t }));
        await db.SaveChangesAsync(ct);
    }
}
