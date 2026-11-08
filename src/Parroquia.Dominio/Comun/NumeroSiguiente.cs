namespace Parroquia.Dominio.Comun;

/// <summary>Sugerencia de Folio/Partida del siguiente registro al capturar en serie ("Guardar y nuevo").</summary>
public static class NumeroSiguiente
{
    /// <summary>
    /// Suma uno si el texto es un número puro (conserva ceros a la izquierda: "009" → "010").
    /// Si trae letras o "bis" lo devuelve igual: el capturista decide.
    /// </summary>
    public static string De(string? texto)
    {
        var t = TextoLibro.Limpiar(texto);
        if (t is null) return "";
        if (!t.All(char.IsAsciiDigit) || !long.TryParse(t, out var n) || n == long.MaxValue) return t;

        var siguiente = (n + 1).ToString();
        return t.Length > 1 && t[0] == '0' ? siguiente.PadLeft(t.Length, '0') : siguiente;
    }
}
