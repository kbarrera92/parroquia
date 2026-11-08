using System.Globalization;
using System.Text;

namespace Parroquia.Dominio.Comun;

/// <summary>
/// Clave de texto para ordenar Libro/Folio de forma natural ("2" &lt; "2 bis" &lt; "10").
/// Comparar con orden ordinal. Ignora mayúsculas, tildes y separadores.
/// </summary>
public static class ClaveOrdenNatural
{
    private const int AnchoNumero = 10;

    public static string De(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";

        var sb = new StringBuilder();
        var digitos = new StringBuilder();

        void CerrarNumero()
        {
            if (digitos.Length == 0) return;
            var numero = digitos.ToString().TrimStart('0');
            sb.Append(numero.PadLeft(AnchoNumero, '0'));
            digitos.Clear();
        }

        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsAsciiDigit(c))
            {
                digitos.Append(c);
            }
            else if (char.IsLetter(c))
            {
                CerrarNumero();
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                CerrarNumero();
            }
        }
        CerrarNumero();

        return sb.ToString();
    }
}
