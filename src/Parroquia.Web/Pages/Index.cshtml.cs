using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Web.Pages;

/// <summary>Inicio: búsqueda de una persona en todos los libros registrados.</summary>
[Authorize(Policy = Permisos.RegistrosBuscar)]
public class IndexModel(BusquedaGlobal busqueda) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Texto { get; set; }

    public ResultadoGlobal Resultado { get; private set; } = ResultadoGlobal.Vacio(false);
    public bool HayTexto => !string.IsNullOrWhiteSpace(Texto);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (HayTexto) Resultado = await busqueda.BuscarAsync(Texto, ct: ct);

        // htmx pide solo los resultados; el botón "Atrás" (restauración de historial) necesita la página completa.
        var esHtmx = Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request");
        return esHtmx ? Partial("_ResultadosGlobales", this) : Page();
    }
}
