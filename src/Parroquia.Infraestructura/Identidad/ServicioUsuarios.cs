using Microsoft.AspNetCore.Identity;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>Administración de cuentas. Nada de lo que se registra en la bitácora incluye contraseñas.</summary>
public sealed class ServicioUsuarios(UserManager<Usuario> usuarios, IBitacoraAcceso bitacora)
{
    /// <summary>El propio usuario cambia su contraseña (pide la actual). Cierra sus otras sesiones y quita el cambio obligatorio.</summary>
    public async Task<ResultadoOperacion> CambiarPropiaAsync(Usuario usuario, string actual, string nueva, CancellationToken ct = default)
    {
        if (actual == nueva)
            return ResultadoOperacion.Falla("La nueva contraseña debe ser distinta de la actual.");

        var resultado = await usuarios.ChangePasswordAsync(usuario, actual, nueva);
        if (!resultado.Succeeded)
            return ResultadoOperacion.Falla(string.Join(" ", resultado.Errors.Select(e => e.Description)));

        if (usuario.DebeCambiarPassword)
        {
            usuario.DebeCambiarPassword = false;
            await usuarios.UpdateAsync(usuario);
        }

        await bitacora.RegistrarAsync(EventoAcceso.PasswordCambiada, usuario.UserName, usuario.Id, ct: ct);
        return ResultadoOperacion.Exito;
    }
}
