namespace Parroquia.Aplicacion.Operacion;

public enum NivelEstado { Correcto, Aviso }

/// <summary>Un renglón de la página de estado: cómo está (verde/amarillo) y qué decir.</summary>
public sealed record ElementoEstado(NivelEstado Nivel, string Resumen, string? Detalle = null);

public sealed record ConteoLibro(string Libro, int Total);

/// <summary>Lo que ve el Administrador en <c>Administración → Estado del sistema</c> (docs/04 ADR-10).</summary>
public sealed record EstadoSistema(
    string Version,
    string Entorno,
    DateTimeOffset Inicio,
    string VersionPostgres,
    long BytesBaseDatos,
    string UltimaMigracion,
    IReadOnlyList<ConteoLibro> Registros,
    ElementoEstado Respaldo,
    ElementoEstado Restauracion,
    ElementoEstado Disco,
    ElementoEstado Certificado);

public interface IEstadoSistema
{
    Task<EstadoSistema> ObtenerAsync(CancellationToken ct = default);
}

/// <summary>Umbrales de las alertas (docs/04 ADR-09 y ADR-08).</summary>
public static class UmbralesEstado
{
    public static readonly TimeSpan RespaldoMaximo = TimeSpan.FromHours(36);
    public static readonly TimeSpan RestauracionMaxima = TimeSpan.FromDays(100); // la prueba es trimestral
    public const int DiasAvisoCertificado = 30;
    public const long BytesLibresMinimos = 10L * 1024 * 1024 * 1024;
    public const double FraccionLibreMinima = 0.10;
}
