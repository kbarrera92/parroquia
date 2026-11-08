using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Comun;
using Parroquia.Infraestructura.Catalogos;

namespace Parroquia.Web.Pages.Admin;

public record ItemCatalogo(long Id, string Valor, bool Activo);

/// <summary>Lo que necesita el parcial compartido <c>_Catalogo</c>.</summary>
public interface ICatalogoPagina
{
    string Titulo { get; }
    string Descripcion { get; }
    string EtiquetaNuevo { get; }
    IReadOnlyList<ItemCatalogo> Items { get; }
    string? Mensaje { get; }
    string? Error { get; }
}

/// <summary>Base de las páginas de catálogos administrables. Cada catálogo concreto añade su política de acceso.</summary>
public abstract class CatalogoPagina<T>(ServicioCatalogo<T> servicio) : PageModel, ICatalogoPagina
    where T : EntidadAuditable, ICatalogoItem, new()
{
    public abstract string Titulo { get; }
    public abstract string Descripcion { get; }
    public abstract string EtiquetaNuevo { get; }

    public IReadOnlyList<ItemCatalogo> Items { get; private set; } = [];
    public string? Mensaje => TempData["Mensaje"] as string;
    public string? Error => TempData["Error"] as string;

    public async Task OnGetAsync() => Items = await CargarAsync();

    public async Task<IActionResult> OnPostCrearAsync(string? valor) =>
        Terminar(await servicio.CrearAsync(valor), "Se agregó el elemento.");

    public async Task<IActionResult> OnPostEditarAsync(long id, string? valor) =>
        Terminar(await servicio.EditarAsync(id, valor), "Se guardó el cambio.");

    public async Task<IActionResult> OnPostActivoAsync(long id, bool activo) =>
        Terminar(await servicio.CambiarActivoAsync(id, activo), activo ? "Elemento activado." : "Elemento desactivado.");

    private IActionResult Terminar(ResultadoCatalogo r, string exito)
    {
        if (r.Ok) TempData["Mensaje"] = exito;
        else TempData["Error"] = r.Error;
        return RedirectToPage();
    }

    private async Task<IReadOnlyList<ItemCatalogo>> CargarAsync() =>
        [.. (await servicio.ListarAsync()).Select(x => new ItemCatalogo(x.Id, x.Valor, x.Activo))];
}
