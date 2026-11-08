using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Pages.Cuenta;

[AllowAnonymous]
public class LoginModel(SignInManager<Usuario> signIn, UserManager<Usuario> usuarios) : PageModel
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
        if (usuario is { Activo: false })
        {
            ModelState.AddModelError("", "Usuario o contraseña incorrectos.");
            return Page();
        }

        var resultado = await signIn.PasswordSignInAsync(Usuario, Password, isPersistent: false, lockoutOnFailure: true);

        if (resultado.Succeeded)
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");

        ModelState.AddModelError("", resultado.IsLockedOut
            ? "Cuenta bloqueada por intentos fallidos. Espere 15 minutos o avise al Administrador."
            : "Usuario o contraseña incorrectos.");
        return Page();
    }
}
