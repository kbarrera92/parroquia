using Microsoft.AspNetCore.Identity;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>
/// Con <c>RequireConfirmedAccount</c> Identity pregunta aquí si el usuario puede iniciar sesión:
/// un usuario desactivado no entra aunque la contraseña sea correcta.
/// </summary>
public sealed class ConfirmacionUsuarioActivo : IUserConfirmation<Usuario>
{
    public Task<bool> IsConfirmedAsync(UserManager<Usuario> manager, Usuario user) => Task.FromResult(user.Activo);
}
