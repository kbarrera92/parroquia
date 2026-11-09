namespace Parroquia.Aplicacion.Seguridad;

/// <summary>
/// Política de contraseñas (docs/04, ADR-05), según NIST SP 800-63B y OWASP ASVS:
/// solo longitud y lista de contraseñas comunes; sin reglas de composición ni caducidad periódica.
/// </summary>
public static class PoliticaContrasena
{
    public const int LongitudMinima = 12;
    public const int LongitudMaxima = 128;
}
