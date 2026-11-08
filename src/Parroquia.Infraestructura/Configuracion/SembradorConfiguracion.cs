using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Configuracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Configuracion;

/// <summary>
/// Crea la fila de configuración con el membrete de docs/03 si no existe. Después manda el Administrador.
/// El párroco no se siembra: lo registra el Administrador.
/// </summary>
public static class SembradorConfiguracion
{
    public static async Task InicializarAsync(ParroquiaDbContext db, CancellationToken ct = default)
    {
        if (await db.Configuraciones.AnyAsync(ct)) return;

        db.Configuraciones.Add(new ConfiguracionParroquia
        {
            Nombre = "Catedral — Parroquia San Bartolomé",
            Direccion = "6a. Avenida y 7a. Calle zona 1",
            Municipio = "Mazatenango, Suchitepéquez Guatemala C.A.",
            Telefono = "(502) 7872-0051",
            TextoLugarEmision = "Mazatenango, Such.",
            CiudadAbreviada = "Mazatenango",
        });
        await db.SaveChangesAsync(ct);
    }
}
