namespace Parroquia.Aplicacion.Seguridad;

/// <summary>Fuente única de qué permisos tiene cada rol (docs/04, ADR-05).</summary>
public static class MatrizPermisos
{
    private static readonly IReadOnlyList<string> Lectura = [Permisos.RegistrosVer, Permisos.RegistrosBuscar];

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> PorRol =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [Roles.Capturista] = new HashSet<string>(Lectura)
            {
                Permisos.RegistrosCrear,
                Permisos.RegistrosEditar,
                Permisos.RegistrosAnular,
            },
            [Roles.Secretaria] = new HashSet<string>(Lectura)
            {
                Permisos.ConstanciasImprimir,
                Permisos.ConstanciasReimprimir,
            },
            [Roles.Administrador] = new HashSet<string>(Permisos.Todos),
        };

    public static IReadOnlySet<string> De(string rol) =>
        PorRol.TryGetValue(rol, out var permisos) ? permisos : new HashSet<string>();
}
