using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Web.Pages;

/// <summary>Inicio: búsqueda de una persona en todos los libros registrados.</summary>
[Authorize(Policy = Permisos.RegistrosBuscar)]
public class IndexModel(
    BusquedaGlobal busqueda, IEnumerable<IModuloSacramental> modulos, IAuthorizationService autorizacion) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Texto { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sacramento { get; set; }
    [BindProperty(SupportsGet = true)] public string? Libro { get; set; }
    [BindProperty(SupportsGet = true)] public string? Folio { get; set; }
    [BindProperty(SupportsGet = true)] public short? AnioDesde { get; set; }
    [BindProperty(SupportsGet = true)] public short? AnioHasta { get; set; }

    public ResultadoGlobal Resultado { get; private set; } = ResultadoGlobal.Vacio(false);
    public bool HayTexto => !string.IsNullOrWhiteSpace(Texto);
    public bool PuedeImprimir { get; private set; }
    public IReadOnlyList<IModuloSacramental> Modulos { get; private set; } = [];

    public bool HayFiltros =>
        !string.IsNullOrWhiteSpace(Sacramento) || !string.IsNullOrWhiteSpace(Libro) || !string.IsNullOrWhiteSpace(Folio)
        || AnioDesde is not null || AnioHasta is not null;

    /// <summary>Consulta para el enlace "abrir el libro completo" (conserva texto y filtros que entiende el listado).</summary>
    public string ConsultaLibro(string? texto) =>
        "?texto=" + Uri.EscapeDataString(texto ?? "")
        + (string.IsNullOrWhiteSpace(Libro) ? "" : "&libro=" + Uri.EscapeDataString(Libro))
        + (string.IsNullOrWhiteSpace(Folio) ? "" : "&folio=" + Uri.EscapeDataString(Folio))
        + (AnioDesde is null ? "" : "&desde=" + AnioDesde)
        + (AnioHasta is null ? "" : "&hasta=" + AnioHasta);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Modulos = [.. modulos.OrderBy(m => m.Orden)];
        // La página de impresión decide si es la 1.ª impresión o reimpresión; basta con poder hacer alguna.
        PuedeImprimir = (await autorizacion.AuthorizeAsync(User, Permisos.ConstanciasImprimir)).Succeeded
            || (await autorizacion.AuthorizeAsync(User, Permisos.ConstanciasReimprimir)).Succeeded;

        if (HayTexto)
            Resultado = await busqueda.BuscarAsync(
                new FiltroGlobal(Texto, Sacramento, Libro, Folio, AnioDesde, AnioHasta), ct: ct);

        // htmx pide solo los resultados; el botón "Atrás" (restauración de historial) necesita la página completa.
        var esHtmx = Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request");
        return esHtmx ? Partial("_ResultadosGlobales", this) : Page();
    }
}
