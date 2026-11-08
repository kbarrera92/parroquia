using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Pages.Cuenta;

public class LogoutModel(SignInManager<Usuario> signIn) : PageModel
{
    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync();
        return RedirectToPage("/Cuenta/Login");
    }
}
