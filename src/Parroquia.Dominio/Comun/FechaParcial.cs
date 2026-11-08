using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Parroquia.Dominio.Comun;

/// <summary>
/// Fecha de libro que puede estar completa, solo año-mes, solo año o vacía.
/// Nunca usar <see cref="DateTime"/> para fechas de libro (CLAUDE.md regla 6).
/// </summary>
public sealed record FechaParcial : IComparable<FechaParcial>
{
    public const short AnioMinimo = 1500;

    private static readonly string[] Meses =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre",
    ];

    public static readonly FechaParcial Vacia = new();

    public short? Anio { get; init; }
    public short? Mes { get; init; }
    public short? Dia { get; init; }

    /// <summary>Constructor sin validar: para materialización de EF y <see cref="Vacia"/>. Usar <see cref="Crear"/>.</summary>
    public FechaParcial() { }

    public bool EsVacia => Anio is null && Mes is null && Dia is null;
    public bool EsCompleta => Anio is not null && Mes is not null && Dia is not null;

    /// <summary>año*10000 + mes*100 + día, con ceros donde falta. Vacía = 0.</summary>
    public int ClaveOrden => (Anio ?? 0) * 10000 + (Mes ?? 0) * 100 + (Dia ?? 0);

    public static FechaParcial Crear(short? anio, short? mes, short? dia, DateOnly hoy) =>
        TryCrear(anio, mes, dia, hoy, out var fecha, out var error)
            ? fecha
            : throw new ArgumentException(error);

    public static bool TryCrear(short? anio, short? mes, short? dia, DateOnly hoy,
        [NotNullWhen(true)] out FechaParcial? fecha, [NotNullWhen(false)] out string? error)
    {
        fecha = null;
        error = Validar(anio, mes, dia, hoy);
        if (error is not null) return false;

        fecha = anio is null ? Vacia : new FechaParcial { Anio = anio, Mes = mes, Dia = dia };
        return true;
    }

    /// <summary>Acepta "13/8/1978", "13-08-1978", "13.8.1978", "8/1978", "1978" o vacío.</summary>
    public static bool TryParsear(string? texto, DateOnly hoy,
        [NotNullWhen(true)] out FechaParcial? fecha, [NotNullWhen(false)] out string? error)
    {
        fecha = null;
        error = null;

        if (string.IsNullOrWhiteSpace(texto))
        {
            fecha = Vacia;
            return true;
        }

        var partes = texto.Trim().Split(['/', '-', '.'], StringSplitOptions.TrimEntries);
        var numeros = new short[partes.Length];
        for (var i = 0; i < partes.Length; i++)
        {
            if (!short.TryParse(partes[i], NumberStyles.None, CultureInfo.InvariantCulture, out numeros[i]))
            {
                error = $"La fecha \"{texto.Trim()}\" no es válida. Use día/mes/año, mes/año o solo el año.";
                return false;
            }
        }

        return numeros.Length switch
        {
            1 => TryCrear(numeros[0], null, null, hoy, out fecha, out error),
            2 => TryCrear(numeros[1], numeros[0], null, hoy, out fecha, out error),
            3 => TryCrear(numeros[2], numeros[1], numeros[0], hoy, out fecha, out error),
            _ => Falla(texto, out error),
        };

        static bool Falla(string texto, out string error)
        {
            error = $"La fecha \"{texto.Trim()}\" no es válida. Use día/mes/año, mes/año o solo el año.";
            return false;
        }
    }

    /// <summary>"13 de agosto de 1978" / "agosto de 1978" / "1978" / "".</summary>
    public string ATexto()
    {
        if (Anio is null) return "";
        if (Mes is null) return Anio.Value.ToString(CultureInfo.InvariantCulture);

        var mes = Meses[Mes.Value - 1];
        return Dia is null
            ? $"{mes} de {Anio}"
            : $"{Dia} de {mes} de {Anio}";
    }

    public int CompareTo(FechaParcial? otra) => ClaveOrden.CompareTo(otra?.ClaveOrden ?? 0);

    public override string ToString() => ATexto();

    private static string? Validar(short? anio, short? mes, short? dia, DateOnly hoy)
    {
        if (anio is null)
            return mes is not null || dia is not null ? "Para indicar mes o día debe indicar el año." : null;

        if (anio < AnioMinimo || anio > hoy.Year)
            return $"El año debe estar entre {AnioMinimo} y {hoy.Year}.";

        if (mes is null)
            return dia is not null ? "Para indicar el día debe indicar el mes." : null;

        if (mes < 1 || mes > 12)
            return "El mes debe estar entre 1 y 12.";

        if (anio == hoy.Year && mes > hoy.Month)
            return "La fecha no puede ser futura.";

        if (dia is null) return null;

        if (dia < 1 || dia > DateTime.DaysInMonth(anio.Value, mes.Value))
            return $"El día no es válido para {Meses[mes.Value - 1]} de {anio}.";

        if (new DateOnly(anio.Value, mes.Value, dia.Value) > hoy)
            return "La fecha no puede ser futura.";

        return null;
    }
}
