namespace Parroquia.Aplicacion.Comun;

/// <summary>Resultado de una operación de negocio: éxito o un mensaje de error en español para el usuario.</summary>
public readonly record struct ResultadoOperacion(bool Ok, string? Error = null)
{
    public static ResultadoOperacion Exito => new(true);
    public static ResultadoOperacion Falla(string error) => new(false, error);
}

public static class HoraGuatemala
{
    // Guatemala (America/Guatemala) es UTC-6 todo el año, sin horario de verano.
    private static readonly TimeSpan Desfase = TimeSpan.FromHours(-6);

    public static DateOnly Hoy(TimeProvider reloj) =>
        DateOnly.FromDateTime(reloj.GetUtcNow().ToOffset(Desfase).DateTime);

    /// <summary>La fecha-hora tal como se ve en un reloj de Guatemala.</summary>
    public static DateTimeOffset Local(DateTimeOffset fecha) => fecha.ToOffset(Desfase);

    /// <summary>Inicio (00:00, hora de Guatemala) del día indicado, como instante UTC (Npgsql solo escribe offset 0).</summary>
    public static DateTimeOffset InicioDelDia(DateOnly dia) => new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), Desfase).ToUniversalTime();
}
