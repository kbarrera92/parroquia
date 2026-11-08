using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Configuracion;
using Parroquia.Infraestructura.Configuracion;

namespace Parroquia.Web.Pages.Admin;

[Authorize(Policy = Permisos.Configuracion)]
public class ParrocosModel(ServicioParroco servicio, TimeProvider reloj) : PageModel
{
    public IReadOnlyList<ParrocoVigente> Historial { get; private set; } = [];
    public ParrocoVigente? Actual => Historial.FirstOrDefault(p => p.EsActual);
    public string HoyIso => HoraGuatemala.Hoy(reloj).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string? Mensaje => TempData["Mensaje"] as string;
    public string? Error => TempData["Error"] as string;

    public async Task OnGetAsync() => Historial = await servicio.HistorialAsync();

    public async Task<IActionResult> OnPostNuevoAsync(string? nombre, string? titulo, string? desde)
    {
        if (!DateOnly.TryParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            return Terminar(ResultadoOperacion.Falla("Indique la fecha desde la que es párroco."), "");

        return Terminar(await servicio.RegistrarNuevoAsync(nombre, titulo, fecha), "Se registró el nuevo párroco.");
    }

    public async Task<IActionResult> OnPostCorregirAsync(string? nombre, string? titulo) =>
        Terminar(await servicio.CorregirActualAsync(nombre, titulo), "Se corrigieron los datos del párroco actual.");

    private IActionResult Terminar(ResultadoOperacion r, string exito)
    {
        if (r.Ok) TempData["Mensaje"] = exito;
        else TempData["Error"] = r.Error;
        return RedirectToPage();
    }
}
