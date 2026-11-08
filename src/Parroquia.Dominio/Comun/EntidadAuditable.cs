namespace Parroquia.Dominio.Comun;

/// <summary>Entidad con datos de auditoría de fila. Los rellena el interceptor de SaveChanges; nunca a mano.</summary>
public interface IAuditable
{
    long Id { get; }
    string? CreadoPor { get; set; }
    DateTimeOffset CreadoEn { get; set; }
    string? ModificadoPor { get; set; }
    DateTimeOffset? ModificadoEn { get; set; }
}

/// <summary>Registro que se anula con motivo en lugar de borrarse.</summary>
public interface IAnulable
{
    bool Anulado { get; set; }
    string? MotivoAnulacion { get; set; }
}

public abstract class EntidadAuditable : IAuditable
{
    public long Id { get; protected set; }
    public string? CreadoPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public string? ModificadoPor { get; set; }
    public DateTimeOffset? ModificadoEn { get; set; }

    /// <summary>Token de concurrencia optimista (xmin de PostgreSQL).</summary>
    public uint Version { get; private set; }
}
