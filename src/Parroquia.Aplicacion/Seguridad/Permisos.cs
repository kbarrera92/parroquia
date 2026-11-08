namespace Parroquia.Aplicacion.Seguridad;

/// <summary>Permisos según la matriz de docs/04-arquitectura.md (ADR-05). Cada permiso es una política de autorización.</summary>
public static class Permisos
{
    /// <summary>Tipo de claim con el que se guardan los permisos en los roles.</summary>
    public const string TipoClaim = "permiso";

    public const string RegistrosVer = "Registros.Ver";
    public const string RegistrosBuscar = "Registros.Buscar";
    public const string RegistrosCrear = "Registros.Crear";
    public const string RegistrosEditar = "Registros.Editar";
    public const string RegistrosAnular = "Registros.Anular";
    public const string RegistrosRevertirAnulacion = "Registros.RevertirAnulacion";
    public const string ConstanciasImprimir = "Constancias.Imprimir";
    public const string ConstanciasReimprimir = "Constancias.Reimprimir";
    public const string ConstanciasCambiarFecha = "Constancias.CambiarFecha";
    public const string CatalogosGestionar = "Catalogos.Gestionar";
    public const string Configuracion = "Configuracion";
    public const string UsuariosGestionar = "Usuarios.Gestionar";
    public const string AuditoriaVer = "Auditoria.Ver";

    public static readonly IReadOnlyList<string> Todos =
    [
        RegistrosVer, RegistrosBuscar, RegistrosCrear, RegistrosEditar, RegistrosAnular,
        RegistrosRevertirAnulacion, ConstanciasImprimir, ConstanciasReimprimir,
        ConstanciasCambiarFecha, CatalogosGestionar, Configuracion, UsuariosGestionar, AuditoriaVer,
    ];
}
