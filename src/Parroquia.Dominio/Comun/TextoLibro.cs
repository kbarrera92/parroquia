using System.Text.RegularExpressions;

namespace Parroquia.Dominio.Comun;

/// <summary>Limpieza al guardar texto del libro: recorta y colapsa espacios; no cambia mayúsculas ni tildes.</summary>
public static partial class TextoLibro
{
    public static string? Limpiar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        return EspaciosMultiples().Replace(texto.Trim(), " ");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspaciosMultiples();
}
