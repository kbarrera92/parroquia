using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>Conserva los claims de rol/permiso de Identity y añade el aviso de cambio obligatorio de contraseña.</summary>
public sealed class FabricaClaimsUsuario(
    UserManager<Usuario> usuarios, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> opciones)
    : UserClaimsPrincipalFactory<Usuario, IdentityRole>(usuarios, roles, opciones)
{
    public const string TipoCambioObligatorio = "debe_cambiar_password";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Usuario user)
    {
        var identidad = await base.GenerateClaimsAsync(user);
        if (user.DebeCambiarPassword)
            identidad.AddClaim(new Claim(TipoCambioObligatorio, "1"));
        return identidad;
    }
}
