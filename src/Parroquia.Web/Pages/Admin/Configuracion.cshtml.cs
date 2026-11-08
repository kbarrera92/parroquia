using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Configuracion;

namespace Parroquia.Web.Pages.Admin;

public class FormularioConfiguracion
{
    [Display(Name = "Nombre de la parroquia")] public string? Nombre { get; set; }
    [Display(Name = "Dirección")] public string? Direccion { get; set; }
    [Display(Name = "Municipio y departamento")] public string? Municipio { get; set; }
    [Display(Name = "Teléfono")] public string? Telefono { get; set; }
    [Display(Name = "Lugar de emisión (cierre de la constancia)")] public string? TextoLugarEmision { get; set; }
    [Display(Name = "Ciudad abreviada")] public string? CiudadAbreviada { get; set; }
}

[Authorize(Policy = Permisos.Configuracion)]
public class ConfiguracionModel(ServicioConfiguracionParroquia servicio) : PageModel
{
    [BindProperty] public FormularioConfiguracion Datos { get; set; } = new();
    [BindProperty] public IFormFile? ArchivoLogo { get; set; }
    [BindProperty] public bool QuitarLogo { get; set; }

    public bool TieneLogo { get; private set; }
    public string? Mensaje => TempData["Mensaje"] as string;

    public async Task OnGetAsync() => await CargarAsync();

    public async Task<IActionResult> OnGetLogoAsync()
    {
        var conf = await servicio.ObtenerAsync();
        if (conf?.Logo is null || conf.LogoTipoContenido is null) return NotFound();

        Response.Headers.CacheControl = "private, no-cache";
        return File(conf.Logo, conf.LogoTipoContenido);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ArchivoLogo? logo = null;
        if (ArchivoLogo is { Length: > 0 })
        {
            if (ArchivoLogo.Length > ServicioConfiguracionParroquia.LogoMaximoBytes)
                return await ConErrorAsync("El logo no puede pesar más de 1 MB.");

            using var ms = new MemoryStream();
            await ArchivoLogo.CopyToAsync(ms);
            logo = new ArchivoLogo(ms.ToArray(), ArchivoLogo.ContentType);
        }

        var resultado = await servicio.GuardarAsync(
            new DatosConfiguracion(Datos.Nombre, Datos.Direccion, Datos.Municipio, Datos.Telefono,
                Datos.TextoLugarEmision, Datos.CiudadAbreviada),
            logo, QuitarLogo);

        if (!resultado.Ok) return await ConErrorAsync(resultado.Error!);

        TempData["Mensaje"] = "Se guardó la configuración.";
        return RedirectToPage();
    }

    private async Task<IActionResult> ConErrorAsync(string error)
    {
        ModelState.AddModelError("", error);
        TieneLogo = (await servicio.ObtenerAsync())?.Logo is not null;
        return Page();
    }

    private async Task CargarAsync()
    {
        var conf = await servicio.ObtenerAsync();
        if (conf is null) return;

        Datos = new FormularioConfiguracion
        {
            Nombre = conf.Nombre,
            Direccion = conf.Direccion,
            Municipio = conf.Municipio,
            Telefono = conf.Telefono,
            TextoLugarEmision = conf.TextoLugarEmision,
            CiudadAbreviada = conf.CiudadAbreviada,
        };
        TieneLogo = conf.Logo is not null;
    }
}
