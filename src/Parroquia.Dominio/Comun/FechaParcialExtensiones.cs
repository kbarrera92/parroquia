using System.Globalization;

namespace Parroquia.Dominio.Comun;

public static class FechaParcialExtensiones
{
    /// <summary>
    /// Texto para el campo de captura, el mismo que acepta <see cref="FechaParcial.TryParsear"/>:
    /// "02/10/2004", "10/2004", "2004" o vacío.
    /// </summary>
    public static string ATextoCaptura(this FechaParcial fecha)
    {
        if (fecha.Anio is null) return "";
        var anio = fecha.Anio.Value.ToString(CultureInfo.InvariantCulture);
        if (fecha.Mes is null) return anio;

        var mes = fecha.Mes.Value.ToString("00", CultureInfo.InvariantCulture);
        return fecha.Dia is null
            ? $"{mes}/{anio}"
            : $"{fecha.Dia.Value.ToString("00", CultureInfo.InvariantCulture)}/{mes}/{anio}";
    }
}
