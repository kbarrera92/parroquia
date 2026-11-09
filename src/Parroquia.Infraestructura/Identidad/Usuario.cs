using Microsoft.AspNetCore.Identity;

namespace Parroquia.Infraestructura.Identidad;

public class Usuario : IdentityUser
{
    public string NombreCompleto { get; set; } = "";

    /// <summary>Usuario desactivado no puede iniciar sesión (no se borra: queda en la bitácora).</summary>
    public bool Activo { get; set; } = true;

    /// <summary>Contraseña temporal (alta o restablecimiento): el usuario debe cambiarla antes de usar el sistema.</summary>
    public bool DebeCambiarPassword { get; set; }

    public DateTimeOffset CreadoEn { get; set; }
}
