using Microsoft.AspNetCore.Identity;
using Parroquia.Aplicacion.Comun;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>
/// Con <c>RequireConfirmedAccount</c> Identity pregunta aquí si el usuario puede iniciar sesión:
/// un usuario desactivado o con la cuenta vencida no entra aunque la contraseña sea correcta.
/// </summary>
public sealed class ConfirmacionUsuarioActivo(TimeProvider reloj) : IUserConfirmation<Usuario>
{
    public Task<bool> IsConfirmedAsync(UserManager<Usuario> manager, Usuario user) =>
        Task.FromResult(user.PuedeIngresar(HoraGuatemala.Hoy(reloj)));
}
