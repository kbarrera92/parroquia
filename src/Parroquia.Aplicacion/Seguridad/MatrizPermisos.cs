namespace Parroquia.Aplicacion.Seguridad;

/// <summary>Permisos <b>iniciales</b> de cada rol (docs/04, ADR-05): se siembran al crear el rol; luego el Administrador los edita en Perfiles.</summary>
public static class MatrizPermisos
{
    private static readonly IReadOnlyList<string> Lectura = [Permisos.RegistrosVer, Permisos.RegistrosBuscar];

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> PorRol =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [Roles.Capturista] = new HashSet<string>(Lectura)
            {
                Permisos.RegistrosTranscribir,
                Permisos.RegistrosEditar,
                Permisos.RegistrosAnular,
            },
            [Roles.Secretaria] = new HashSet<string>(Lectura)
            {
                Permisos.RegistrosAsentarNuevo,
                Permisos.RegistrosEditar,
                Permisos.RegistrosAnular,
                Permisos.ConstanciasImprimir,
                Permisos.ConstanciasReimprimir,
            },
            [Roles.Administrador] = new HashSet<string>(Permisos.Todos),
        };

    public static IReadOnlySet<string> De(string rol) =>
        PorRol.TryGetValue(rol, out var permisos) ? permisos : new HashSet<string>();
}
