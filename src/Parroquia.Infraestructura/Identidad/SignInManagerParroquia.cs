using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>
/// Además del sello de seguridad, la sesión abierta exige que la cuenta siga pudiendo entrar
/// (<see cref="ConfirmacionUsuarioActivo"/>: activa y no vencida). Así una cuenta temporal que llega a su fecha de
/// vencimiento pierde la sesión en la siguiente revalidación (cada minuto) sin que el Administrador haga nada.
/// </summary>
public class SignInManagerParroquia(
    UserManager<Usuario> userManager, IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<Usuario> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<Usuario>> logger, IAuthenticationSchemeProvider schemes, IUserConfirmation<Usuario> confirmation)
    : SignInManager<Usuario>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<Usuario?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var usuario = await base.ValidateSecurityStampAsync(principal);
        return usuario is not null && await CanSignInAsync(usuario) ? usuario : null;
    }
}
