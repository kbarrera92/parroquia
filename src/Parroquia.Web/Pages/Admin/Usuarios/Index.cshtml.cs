using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Pages.Admin.Usuarios;

// NoStore: esta página puede mostrar una contraseña temporal; no debe quedar en la caché del navegador.
[Authorize(Policy = Permisos.UsuariosGestionar)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class IndexModel(ServicioUsuarios servicio, Parroquia.Aplicacion.Seguridad.IUsuarioActual actual) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Rol { get; set; }
    [BindProperty(SupportsGet = true)] public string? Estado { get; set; }

    public IReadOnlyList<FilaUsuario> Filas { get; private set; } = [];
    public string? IdActual => actual.Id;
    public string? Mensaje => TempData["Mensaje"] as string;
    public string? Error => TempData["Error"] as string;

    /// <summary>Contraseña temporal recién generada: se muestra una sola vez (TempData se consume al leerla).</summary>
    public string? Temporal { get; private set; }
    public string? UsuarioTemporal { get; private set; }

    public async Task OnGetAsync()
    {
        Temporal = TempData["Temporal"] as string;
        UsuarioTemporal = TempData["TemporalUsuario"] as string;

        bool? activo = Estado switch { "activos" => true, "inactivos" => false, _ => null };
        Filas = await servicio.ListarAsync(string.IsNullOrWhiteSpace(Rol) ? null : Rol, activo);
    }

    public async Task<IActionResult> OnPostEstadoAsync(string id, bool activo) =>
        Terminar(await servicio.CambiarEstadoAsync(id, activo), activo ? "Usuario activado." : "Usuario desactivado. Sus sesiones abiertas se cerrarán en un minuto.");

    public async Task<IActionResult> OnPostDesbloquearAsync(string id) =>
        Terminar(await servicio.DesbloquearAsync(id), "Cuenta desbloqueada.");

    public async Task<IActionResult> OnPostRestablecerAsync(string id)
    {
        var r = await servicio.RestablecerPasswordAsync(id);
        if (!r.Ok) return Terminar(ResultadoOperacion.Falla(r.Error!), "");

        var fila = await servicio.ObtenerAsync(id);
        TempData["Temporal"] = r.Temporal;
        TempData["TemporalUsuario"] = fila?.Usuario;
        return RedirectToPage();
    }

    private IActionResult Terminar(ResultadoOperacion r, string exito)
    {
        if (r.Ok) TempData["Mensaje"] = exito;
        else TempData["Error"] = r.Error;
        return RedirectToPage(new { Rol, Estado });
    }
}
