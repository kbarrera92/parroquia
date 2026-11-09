namespace Parroquia.Aplicacion.Seguridad;

public enum EventoAcceso
{
    InicioSesion,
    InicioSesionFallido,
    CierreSesion,
    CuentaBloqueada,
    VerRegistro,
    ImprimirConstancia,
    UsuarioCreado,
    UsuarioModificado,
    UsuarioActivado,
    UsuarioDesactivado,
    PasswordRestablecida,
    PasswordCambiada,
    CuentaDesbloqueada,
}

/// <summary>
/// Bitácora de acceso (sesión, ver detalle, imprimir). Solo se agrega: no hay operaciones de edición ni borrado.
/// Los cambios de datos van en <c>Auditoria</c>, no aquí. No registra contraseñas ni búsquedas.
/// </summary>
public interface IBitacoraAcceso
{
    /// <param name="usuarioNombre">Si es null se usa el usuario con sesión (o "sistema"). Indicarlo en el login, donde aún no hay sesión.</param>
    Task RegistrarAsync(EventoAcceso evento, string? usuarioNombre = null, string? usuarioId = null,
        string? entidad = null, long? entidadId = null, string? detalle = null, CancellationToken ct = default);
}
