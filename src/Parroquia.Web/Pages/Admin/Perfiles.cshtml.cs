using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Pages.Admin;

/// <summary>Perfiles: el Administrador elige qué permisos tiene cada rol (Capturista, Secretaria). El Administrador no se edita.</summary>
[Authorize(Policy = Permisos.UsuariosGestionar)]
public class PerfilesModel(ServicioPerfiles servicio) : PageModel
{
    public IReadOnlyList<PerfilPermisos> Perfiles { get; private set; } = [];
    public string? Mensaje => TempData["Mensaje"] as string;
    public string? Error => TempData["Error"] as string;

    public async Task OnGetAsync(CancellationToken ct) => Perfiles = await servicio.ListarAsync(ct);

    public async Task<IActionResult> OnPostAsync(string rol, List<string>? permisos, CancellationToken ct)
    {
        var r = await servicio.GuardarAsync(rol, permisos, ct);
        if (r.Ok) TempData["Mensaje"] = $"Se guardaron los permisos de {rol}. Quienes tengan ese perfil deberán iniciar sesión de nuevo (sus sesiones se cierran en un minuto).";
        else TempData["Error"] = r.Error;
        return RedirectToPage();
    }
}
