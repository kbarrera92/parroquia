using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Pages.Cuenta;

[AllowAnonymous]
[EnableRateLimiting("login")]
public class LoginModel(SignInManager<Usuario> signIn, UserManager<Usuario> usuarios, IBitacoraAcceso bitacora) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Escriba su usuario.")]
    public string Usuario { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Escriba su contraseña."), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var usuario = await usuarios.FindByNameAsync(Usuario);

        // Un usuario desactivado devuelve NotAllowed (ConfirmacionUsuarioActivo) sin revelarlo en pantalla.
        var resultado = await signIn.PasswordSignInAsync(Usuario, Password, isPersistent: false, lockoutOnFailure: true);

        if (resultado.Succeeded)
        {
            await bitacora.RegistrarAsync(EventoAcceso.InicioSesion, usuario?.UserName ?? Usuario, usuario?.Id);
            if (usuario is { DebeCambiarPassword: true })
                return RedirectToPage("/Cuenta/CambiarPassword");
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        }

        await bitacora.RegistrarAsync(
            resultado.IsLockedOut ? EventoAcceso.CuentaBloqueada : EventoAcceso.InicioSesionFallido,
            Usuario, usuario?.Id,
            detalle: usuario is null ? "Usuario inexistente"
                : resultado.IsNotAllowed ? "Usuario desactivado"
                : resultado.IsLockedOut ? "Cuenta bloqueada" : "Contraseña incorrecta");

        ModelState.AddModelError("", resultado.IsLockedOut
            ? "Cuenta bloqueada por intentos fallidos. Espere 15 minutos o avise al Administrador."
            : "Usuario o contraseña incorrectos.");
        return Page();
    }
}
