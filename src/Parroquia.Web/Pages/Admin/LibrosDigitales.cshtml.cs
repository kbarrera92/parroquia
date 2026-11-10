using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Numeracion;
using Parroquia.Infraestructura.Numeracion;

namespace Parroquia.Web.Pages.Admin;

/// <summary>Libros que numera el sistema para los sacramentos nuevos (los que ya no se escriben en papel).</summary>
[Authorize(Policy = Permisos.Configuracion)]
public class LibrosDigitalesModel(IEnumerable<IModuloSacramental> modulos, ServicioNumeracion numeracion) : PageModel
{
    public sealed record Fila(string Clave, string Nombre, VistaLibroDigital? Libro);

    public IReadOnlyList<Fila> Filas { get; private set; } = [];
    public string? Mensaje => TempData["Mensaje"] as string;
    public string? Error => TempData["Error"] as string;

    public async Task OnGetAsync()
    {
        var filas = new List<Fila>();
        foreach (var m in modulos.OrderBy(m => m.Orden))
            filas.Add(new Fila(m.Clave, m.Nombre, await numeracion.LibroAbiertoAsync(m.Clave)));
        Filas = filas;
    }

    public async Task<IActionResult> OnPostAbrirAsync(
        string? clave, string? numero, int folioInicial, int partidaInicial,
        int partidasPorFolio, int foliosPorLibro, ModoPartida modoPartida, int partidasYaEnFolio)
    {
        if (!modulos.Any(m => m.Clave == clave)) return NotFound();

        var r = await numeracion.AbrirAsync(new DatosAperturaLibro(
            clave!, numero, folioInicial, partidaInicial, partidasPorFolio, foliosPorLibro,
            modoPartida, partidasYaEnFolio));
        return Terminar(r, "Se abrió el libro digital.");
    }

    public async Task<IActionResult> OnPostCerrarAsync(string? clave)
    {
        if (!modulos.Any(m => m.Clave == clave)) return NotFound();
        return Terminar(await numeracion.CerrarAsync(clave!), "Se cerró el libro digital.");
    }

    private IActionResult Terminar(ResultadoOperacion r, string exito)
    {
        if (r.Ok) TempData["Mensaje"] = exito;
        else TempData["Error"] = r.Error;
        return RedirectToPage();
    }
}
