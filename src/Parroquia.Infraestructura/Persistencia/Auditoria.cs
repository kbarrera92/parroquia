namespace Parroquia.Infraestructura.Persistencia;

public enum AccionAuditoria
{
    Alta,
    Modificacion,
    Anulacion,
    RevertirAnulacion,
}

/// <summary>Historial de cambios de datos (campo → anterior/nuevo). Lo escribe solo <see cref="AuditoriaInterceptor"/>.</summary>
public class Auditoria
{
    public long Id { get; set; }
    public string Entidad { get; set; } = "";
    public long EntidadId { get; set; }
    public AccionAuditoria Accion { get; set; }
    public string? UsuarioId { get; set; }
    public string UsuarioNombre { get; set; } = "";
    public DateTimeOffset Fecha { get; set; }

    /// <summary>JSON: { "Campo": { "anterior": ..., "nuevo": ... } }.</summary>
    public string CambiosJson { get; set; } = "{}";
}
