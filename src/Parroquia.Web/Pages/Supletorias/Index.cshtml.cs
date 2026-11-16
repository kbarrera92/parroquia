using Parroquia.Web.Presentacion;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Supletorias;

namespace Parroquia.Web.Pages.Supletorias;

/// <summary>Libro de Supletorias: uno solo, compartido por los 4 sacramentos. Cada fila lleva al registro que se creó.</summary>
[Authorize(Policy = Permisos.RegistrosBuscar)]
public class IndexModel(
    ServicioSupletorias servicio, IEnumerable<IModuloSacramental> modulos, IAuthorizationService autorizacion) : PageModel
{
    /// <summary>Una fila del libro con el registro al que apunta (nombre de la persona y enlace).</summary>
    public sealed record Fila(FilaSupletoria Supletoria, string Sacramento, string Persona, string? RutaDetalle);

    [BindProperty(SupportsGet = true)] public string? Sacramento { get; set; }
    [BindProperty(SupportsGet = true)] public string? Libro { get; set; }
    [BindProperty(SupportsGet = true)] public string? Folio { get; set; }
    [BindProperty(SupportsGet = true)] public string? Desde { get; set; }
    [BindProperty(SupportsGet = true)] public string? Hasta { get; set; }
    [BindProperty(SupportsGet = true)] public bool Anuladas { get; set; }
    [BindProperty(SupportsGet = true)] public string? Pagina { get; set; }

    public PaginaResultado<Fila> Resultado { get; private set; } = new([], 0, 1, Paginacion.TamanoPorDefecto);
    public IReadOnlyList<IModuloSacramental> Sacramentos { get; private set; } = [];
    public bool PuedeCrear { get; private set; }
    public bool PuedeVerAnuladas { get; private set; }
    public bool HayFiltros => !string.IsNullOrWhiteSpace(Sacramento) || !string.IsNullOrWhiteSpace(Libro)
        || !string.IsNullOrWhiteSpace(Folio) || !string.IsNullOrWhiteSpace(Desde)
        || !string.IsNullOrWhiteSpace(Hasta) || Anuladas;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Sacramentos = [.. modulos.OrderBy(m => m.Orden)];
        PuedeCrear = (await PermisosAlta.DeAsync(autorizacion, User)).PuedeCrear;
        PuedeVerAnuladas = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosRevertirAnulacion)).Succeeded;

        var filtro = new FiltroSupletorias(
            Sacramento, Libro, Folio, Anio(Desde), Anio(Hasta), Anuladas && PuedeVerAnuladas);
        var pagina = int.TryParse(Pagina, NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 1;
        var datos = await servicio.BuscarAsync(filtro, pagina, Paginacion.TamanoPorDefecto, ct);

        Resultado = new PaginaResultado<Fila>(
            [.. await ArmarFilasAsync(datos.Items, ct)], datos.Total, datos.PaginaActual, datos.TamanoPagina);

        // htmx pide solo la tabla; el botón "Atrás" del navegador (restauración de historial) necesita la página completa.
        var esHtmx = Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request");
        return esHtmx ? Partial("_Resultados", this) : Page();
    }

    /// <summary>Resuelve el nombre de la persona de cada registro con el módulo de su sacramento (una consulta por sacramento).</summary>
    private async Task<IReadOnlyList<Fila>> ArmarFilasAsync(IReadOnlyList<FilaSupletoria> items, CancellationToken ct)
    {
        var resumenes = new Dictionary<(string Clave, long Id), IReadOnlyList<ResultadoBusquedaRegistro>>();
        foreach (var grupo in items.GroupBy(i => i.Clave))
        {
            var modulo = modulos.FirstOrDefault(m => m.Clave == grupo.Key);
            if (modulo is null) continue;

            var filas = await modulo.ResumenesAsync([.. grupo.Select(i => i.RegistroId)], ct);
            foreach (var porRegistro in filas.GroupBy(f => f.Id)) resumenes[(grupo.Key, porRegistro.Key)] = [.. porRegistro];
        }

        return [.. items.Select(i =>
        {
            var modulo = modulos.FirstOrDefault(m => m.Clave == i.Clave);
            resumenes.TryGetValue((i.Clave, i.RegistroId), out var personas);
            // Matrimonio aporta una fila por contrayente: se muestran ambos.
            var persona = personas is null
                ? "(registro no disponible)"
                : string.Join(" / ", personas.Select(f => $"{f.Apellidos}, {f.Nombres}".Trim(' ', ',')));
            return new Fila(i, modulo?.Nombre ?? i.Clave, persona, personas?.FirstOrDefault()?.RutaDetalle);
        })];
    }

    private static short? Anio(string? texto) =>
        short.TryParse(texto?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : null;
}
