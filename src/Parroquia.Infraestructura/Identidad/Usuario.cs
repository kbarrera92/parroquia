using Microsoft.AspNetCore.Identity;

namespace Parroquia.Infraestructura.Identidad;

public class Usuario : IdentityUser
{
    public string NombreCompleto { get; set; } = "";

    /// <summary>Usuario desactivado no puede iniciar sesión (no se borra: queda en la bitácora).</summary>
    public bool Activo { get; set; } = true;
}
