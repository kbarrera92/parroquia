namespace Parroquia.Aplicacion.Seguridad;

/// <summary>Usuario que ejecuta la operación en curso (para auditoría y bitácora).</summary>
public interface IUsuarioActual
{
    /// <summary>Id de Identity, o null si no hay sesión (arranque, siembra).</summary>
    string? Id { get; }

    /// <summary>Nombre de usuario, o "sistema" si no hay sesión.</summary>
    string Nombre { get; }
}
