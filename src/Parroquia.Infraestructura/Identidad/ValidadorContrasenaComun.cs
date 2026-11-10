using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>
/// Lista de contraseñas comunes (offline, recurso incrustado) y palabras de contexto, como pide NIST SP 800-63B.
/// La longitud mínima la aplica Identity (<c>RequiredLength</c>); aquí no hay reglas de composición.
/// </summary>
public sealed class ValidadorContrasenaComun : IPasswordValidator<Usuario>
{
    public const string CodigoComun = "ContrasenaComun";
    public const string CodigoContieneUsuario = "ContrasenaContieneUsuario";
    public const string CodigoContieneContexto = "ContrasenaContieneContexto";
    public const string CodigoMuyLarga = "ContrasenaMuyLarga";

    private const string Recurso = "contrasenas-comunes.txt";
    private const int LongitudMinimaBase = 6;

    private static readonly string[] PalabrasContexto = ["bartolome", "mazatenango", "parroquia", "catedral", "suchitepequez"];
    private static readonly Lazy<HashSet<string>> Comunes = new(CargarComunes);

    public Task<IdentityResult> ValidateAsync(UserManager<Usuario> manager, Usuario user, string? password)
    {
        var errores = Evaluar(password ?? "", user.UserName);
        return Task.FromResult(errores.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errores]));
    }

    public static IReadOnlyList<IdentityError> Evaluar(string password, string? usuario)
    {
        var errores = new List<IdentityError>();

        if (password.Length > PoliticaContrasena.LongitudMaxima)
        {
            errores.Add(new IdentityError { Code = CodigoMuyLarga, Description = $"La contraseña no puede pasar de {PoliticaContrasena.LongitudMaxima} caracteres." });
            return errores;
        }

        var minuscula = password.ToLowerInvariant();
        var sinTildes = QuitarTildes(minuscula);

        if (EsComun(minuscula) || EsComun(sinTildes))
            errores.Add(new IdentityError { Code = CodigoComun, Description = "Esa contraseña es muy común o fácil de adivinar. Use una frase más larga y personal." });

        if (!string.IsNullOrWhiteSpace(usuario) && usuario.Trim().Length >= 3
            && sinTildes.Contains(QuitarTildes(usuario.Trim().ToLowerInvariant()), StringComparison.Ordinal))
            errores.Add(new IdentityError { Code = CodigoContieneUsuario, Description = "La contraseña no puede contener el nombre de usuario." });

        if (PalabrasContexto.Any(p => sinTildes.Contains(p, StringComparison.Ordinal)))
            errores.Add(new IdentityError { Code = CodigoContieneContexto, Description = "La contraseña no puede contener palabras de la parroquia (catedral, Mazatenango, San Bartolomé…)." });

        return errores;
    }

    private static bool EsComun(string minuscula)
    {
        if (Comunes.Value.Contains(minuscula)) return true;

        // "palabra2026!" se trata como "palabra": se quitan dígitos y símbolos del final.
        var fin = minuscula.Length;
        while (fin > 0 && !char.IsLetter(minuscula[fin - 1])) fin--;
        return fin >= LongitudMinimaBase && fin < minuscula.Length && Comunes.Value.Contains(minuscula[..fin]);
    }

    private static string QuitarTildes(string texto)
    {
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static HashSet<string> CargarComunes()
    {
        var ensamblado = typeof(ValidadorContrasenaComun).Assembly;
        var nombre = ensamblado.GetManifestResourceNames().Single(n => n.EndsWith(Recurso, StringComparison.Ordinal));
        using var lector = new StreamReader(ensamblado.GetManifestResourceStream(nombre)!, Encoding.UTF8);

        var conjunto = new HashSet<string>(StringComparer.Ordinal);
        while (lector.ReadLine() is { } linea)
        {
            linea = linea.Trim();
            if (linea.Length > 0) conjunto.Add(linea.ToLowerInvariant());
        }
        return conjunto;
    }
}
