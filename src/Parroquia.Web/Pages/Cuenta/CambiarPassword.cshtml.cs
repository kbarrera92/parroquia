using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Pages.Cuenta;

public class CambiarPasswordModel(
    UserManager<Usuario> usuarios, SignInManager<Usuario> signIn, ServicioUsuarios servicio) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Escriba su contraseña actual."), DataType(DataType.Password)]
    public string PasswordActual { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Escriba la nueva contraseña."), DataType(DataType.Password)]
    public string PasswordNueva { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Repita la nueva contraseña."), DataType(DataType.Password)]
    [Compare(nameof(PasswordNueva), ErrorMessage = "Las contraseñas nuevas no coinciden.")]
    public string PasswordConfirmar { get; set; } = "";

    public bool Obligatorio => User.HasClaim(FabricaClaimsUsuario.TipoCambioObligatorio, "1");
    public int Minimo => PoliticaContrasena.LongitudMinima;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var usuario = await usuarios.GetUserAsync(User);
        if (usuario is null) return RedirectToPage("/Cuenta/Login");

        var r = await servicio.CambiarPropiaAsync(usuario, PasswordActual, PasswordNueva);
        if (!r.Ok)
        {
            ModelState.AddModelError("", r.Error!);
            return Page();
        }

        // Nueva cookie con el sello de seguridad actualizado y sin el aviso de cambio obligatorio.
        await signIn.RefreshSignInAsync(usuario);
        TempData["Mensaje"] = "Contraseña cambiada.";
        return LocalRedirect("/");
    }
}
