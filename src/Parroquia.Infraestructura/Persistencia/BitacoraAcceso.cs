using Microsoft.AspNetCore.Http;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Infraestructura.Persistencia;

public class BitacoraAcceso
{
    public long Id { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public string? UsuarioId { get; set; }
    public string UsuarioNombre { get; set; } = "";
    public EventoAcceso Evento { get; set; }
    public string? Entidad { get; set; }
    public long? EntidadId { get; set; }
    public string? Detalle { get; set; }

    /// <summary>IP del equipo que hizo la petición.</summary>
    public string? Ip { get; set; }
}

public sealed class ServicioBitacoraAcceso(
    ParroquiaDbContext db, IUsuarioActual actual, IHttpContextAccessor http, TimeProvider reloj) : IBitacoraAcceso
{
    public async Task RegistrarAsync(EventoAcceso evento, string? usuarioNombre = null, string? usuarioId = null,
        string? entidad = null, long? entidadId = null, string? detalle = null, CancellationToken ct = default)
    {
        db.BitacoraAccesos.Add(new BitacoraAcceso
        {
            Fecha = reloj.GetUtcNow(),
            UsuarioId = usuarioId ?? (usuarioNombre is null ? actual.Id : null),
            UsuarioNombre = usuarioNombre ?? actual.Nombre,
            Evento = evento,
            Entidad = entidad,
            EntidadId = entidadId,
            Detalle = detalle,
            Ip = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync(ct);
    }
}
