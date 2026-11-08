using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Web.Pages.Matrimonio;

[Authorize(Policy = Permisos.RegistrosBuscar)]
public class IndexModel(ServicioMatrimonio servicio, IAuthorizationService autorizacion) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Texto { get; set; }
    [BindProperty(SupportsGet = true)] public string? Libro { get; set; }
    [BindProperty(SupportsGet = true)] public string? Folio { get; set; }
    [BindProperty(SupportsGet = true)] public string? Desde { get; set; }
    [BindProperty(SupportsGet = true)] public string? Hasta { get; set; }
    [BindProperty(SupportsGet = true)] public bool Pendientes { get; set; }
    [BindProperty(SupportsGet = true)] public bool Anulados { get; set; }
    [BindProperty(SupportsGet = true)] public string? Pagina { get; set; }

    public PaginaResultado<FilaMatrimonio> Resultado { get; private set; } = new([], 0, 1, Paginacion.TamanoPorDefecto);
    public bool PuedeCrear { get; private set; }
    public bool PuedeVerAnulados { get; private set; }
    public bool HayFiltros => !string.IsNullOrWhiteSpace(Texto) || !string.IsNullOrWhiteSpace(Libro)
        || !string.IsNullOrWhiteSpace(Folio) || !string.IsNullOrWhiteSpace(Desde)
        || !string.IsNullOrWhiteSpace(Hasta) || Pendientes || Anulados;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        PuedeCrear = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosCrear)).Succeeded;
        PuedeVerAnulados = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosRevertirAnulacion)).Succeeded;

        var filtro = new FiltroMatrimonio(
            Texto, Libro, Folio, Anio(Desde), Anio(Hasta), Pendientes, Anulados && PuedeVerAnulados);
        var pagina = int.TryParse(Pagina, NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 1;
        Resultado = await servicio.BuscarAsync(filtro, pagina, Paginacion.TamanoPorDefecto, ct);

        // htmx pide solo la tabla; el botón "Atrás" del navegador (restauración de historial) necesita la página completa.
        var esHtmx = Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request");
        return esHtmx ? Partial("_Resultados", this) : Page();
    }

    private static short? Anio(string? texto) =>
        short.TryParse(texto?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : null;
}
