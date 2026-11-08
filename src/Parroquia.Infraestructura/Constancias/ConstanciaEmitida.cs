namespace Parroquia.Infraestructura.Constancias;

/// <summary>
/// Bitácora de cada constancia impresa. Solo se agrega (como <c>BitacoraAcceso</c>): no se edita ni se borra.
/// Guarda lo que salió en el papel para poder demostrarlo aunque el registro o el párroco cambien después.
/// </summary>
public class ConstanciaEmitida
{
    public long Id { get; set; }

    /// <summary><c>IModuloSacramental.Clave</c> ("comunion").</summary>
    public string TipoSacramento { get; set; } = "";
    public long RegistroId { get; set; }

    public DateTimeOffset EmitidaEn { get; set; }
    public string EmitidaPor { get; set; } = "";
    public string? UsuarioId { get; set; }

    /// <summary>Fecha impresa en la constancia: hoy, salvo ajuste del Administrador.</summary>
    public DateOnly FechaImpresa { get; set; }
    public string? FechaAjustadaPor { get; set; }

    /// <summary>Párroco vigente al emitir, tal como se imprimió.</summary>
    public string Firmante { get; set; } = "";

    /// <summary>El <c>DocumentoConstancia</c> tal como se imprimió (sin logo), en JSON.</summary>
    public string InstantaneaJson { get; set; } = "{}";

    /// <summary>SHA-256 (hex) del PDF entregado.</summary>
    public string HashPdf { get; set; } = "";

    /// <summary>Se imprimió confirmando avisos (dato faltante o registro marcado "Revisar").</summary>
    public bool ConAdvertencias { get; set; }

    /// <summary>Reservado para la Fase 2 (solicitudes y cobros). Sin uso en la Fase 1.</summary>
    public long? SolicitudId { get; set; }
}
