using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Pages.Cuenta;

public class LogoutModel(SignInManager<Usuario> signIn, IBitacoraAcceso bitacora) : PageModel
{
    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await bitacora.RegistrarAsync(EventoAcceso.CierreSesion);
        await signIn.SignOutAsync();
        return RedirectToPage("/Cuenta/Login");
    }
}
