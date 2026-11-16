using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Vinculos;
using Parroquia.Infraestructura.Vinculos;

namespace Parroquia.Web.Pages.Agregar;

/// <summary>
/// Fragmento htmx del botón «+» de la búsqueda: los libros donde la persona aún no tiene registro
/// (<c>/Agregar/{clave}/{id}/{persona?}</c>). Cada opción abre el alta de ese libro con la persona precargada.
/// </summary>
[Authorize(Policy = Permisos.RegistrosCrear)]
public class IndexModel(ServicioVinculos vinculos) : PageModel
{
    public IReadOnlyList<OpcionAlta> Opciones { get; private set; } = [];
    private ExtremoVinculo origen;

    public async Task<IActionResult> OnGetAsync(string clave, long id, string? persona, CancellationToken ct)
    {
        origen = new ExtremoVinculo(clave, id, persona ?? "");
        var faltan = await vinculos.SacramentosFaltantesAsync(origen, ct);
        if (faltan is null) return NotFound();

        Opciones = faltan;
        return Page();
    }

    /// <summary>
    /// Alta del libro elegido con el origen en la consulta (lo lee <c>OrigenAlta</c> en la página <c>Editar</c>).
    /// Si el libro tiene varias personas (Matrimonio), el propio formulario pregunta cuál es.
    /// </summary>
    public string Enlace(OpcionAlta o) =>
        Url.Content("~" + o.RutaNuevo)
        + "?Origen.Clave=" + Uri.EscapeDataString(origen.Tipo)
        + "&Origen.Id=" + origen.Id
        + (origen.Persona.Length == 0 ? "" : "&Origen.Persona=" + Uri.EscapeDataString(origen.Persona));
}
