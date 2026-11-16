namespace Parroquia.Aplicacion.Seguridad;

/// <summary>Permisos según la matriz de docs/04-arquitectura.md (ADR-05). Cada permiso es una política de autorización.</summary>
public static class Permisos
{
    /// <summary>Tipo de claim con el que se guardan los permisos en los roles.</summary>
    public const string TipoClaim = "permiso";

    public const string RegistrosVer = "Registros.Ver";
    public const string RegistrosBuscar = "Registros.Buscar";
    /// <summary>Dar de alta transcribiendo de un libro físico (también la supletoria transcrita de papel).</summary>
    public const string RegistrosTranscribir = "Registros.Transcribir";

    /// <summary>Dar de alta un asiento nuevo que numera el sistema (también la supletoria numerada y el botón «+»).</summary>
    public const string RegistrosAsentarNuevo = "Registros.AsentarNuevo";

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
        RegistrosVer, RegistrosBuscar, RegistrosTranscribir, RegistrosAsentarNuevo, RegistrosEditar, RegistrosAnular,
        RegistrosRevertirAnulacion, ConstanciasImprimir, ConstanciasReimprimir,
        ConstanciasCambiarFecha, CatalogosGestionar, Configuracion, UsuariosGestionar, AuditoriaVer,
    ];

    /// <summary>Solo el Administrador los tiene; no se pueden asignar a otros perfiles (evita darse privilegios).</summary>
    public static readonly IReadOnlyList<string> Reservados = [UsuariosGestionar];

    /// <summary>Texto en español de cada permiso, para la pantalla de perfiles.</summary>
    public static readonly IReadOnlyDictionary<string, string> Descripciones = new Dictionary<string, string>
    {
        [RegistrosVer] = "Ver el detalle de los registros",
        [RegistrosBuscar] = "Buscar y listar registros",
        [RegistrosTranscribir] = "Transcribir registros de un libro físico",
        [RegistrosAsentarNuevo] = "Asentar sacramentos nuevos (numera el sistema)",
        [RegistrosEditar] = "Editar registros",
        [RegistrosAnular] = "Anular registros",
        [RegistrosRevertirAnulacion] = "Revertir anulaciones",
        [ConstanciasImprimir] = "Imprimir constancias",
        [ConstanciasReimprimir] = "Reimprimir constancias",
        [ConstanciasCambiarFecha] = "Cambiar la fecha de emisión de una constancia",
        [CatalogosGestionar] = "Gestionar catálogos (ministros y tratamientos)",
        [Configuracion] = "Configuración de la parroquia, párroco y libros digitales",
        [UsuariosGestionar] = "Gestionar usuarios y perfiles",
        [AuditoriaVer] = "Ver historial y bitácora",
    };
}
