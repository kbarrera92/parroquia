namespace Parroquia.Aplicacion.Seguridad;

/// <summary>Nombres de rol. El código de negocio nunca compara roles: usa <see cref="Permisos"/>.</summary>
public static class Roles
{
    public const string Capturista = "Capturista";
    public const string Secretaria = "Secretaria";
    public const string Administrador = "Administrador";

    public static readonly IReadOnlyList<string> Todos = [Capturista, Secretaria, Administrador];
}
