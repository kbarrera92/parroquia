using System.Globalization;
using System.Text;

namespace Parroquia.Dominio.Comun;

/// <summary>
/// Texto de búsqueda: minúsculas, sin tildes, sin puntuación y sin partículas (de, del, la, las, los, y).
/// Se aplica igual al dato guardado (<c>BusquedaNormalizada</c>) y a la consulta del usuario.
/// </summary>
public static class NormalizadorBusqueda
{
    private static readonly HashSet<string> Particulas = ["de", "del", "la", "las", "los", "y"];

    public static string Normalizar(params string?[] partes)
    {
        var sb = new StringBuilder();
        foreach (var token in Tokens(partes))
        {
            if (Particulas.Contains(token)) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(token);
        }
        return sb.ToString();
    }

    private static IEnumerable<string> Tokens(string?[] partes)
    {
        var actual = new StringBuilder();
        foreach (var parte in partes)
        {
            if (string.IsNullOrEmpty(parte)) continue;

            foreach (var c in parte.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

                if (char.IsLetterOrDigit(c))
                {
                    actual.Append(char.ToLowerInvariant(c));
                }
                else if (actual.Length > 0)
                {
                    yield return actual.ToString();
                    actual.Clear();
                }
            }

            if (actual.Length > 0)
            {
                yield return actual.ToString();
                actual.Clear();
            }
        }
    }
}
