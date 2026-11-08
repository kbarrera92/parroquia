using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Constancias;
using Parroquia.Web.Presentacion;

namespace Parroquia.Web.Pages.Constancias;

/// <summary>Vista previa e impresión de la constancia de cualquier sacramento registrado (<c>/Constancias/{clave}/{id}</c>).</summary>
[Authorize(Policy = Permisos.RegistrosVer)]
public class EmitirModel(
    ServicioConstancias servicio,
    IEnumerable<IModuloSacramental> modulos,
    IAuthorizationService autorizacion,
    TimeProvider reloj) : PageModel
{
    private const string FormatoFecha = "yyyy-MM-dd";

    public string Clave { get; private set; } = "";
    public long Id { get; private set; }
    public VistaPreviaConstancia Vista { get; private set; } = null!;
    public VistaConstanciaModel? Hoja { get; private set; }
    public string? Error { get; private set; }
    public bool PuedeCambiarFecha { get; private set; }
    public string RutaDetalle { get; private set; } = "/";
    public string FechaIso => Vista.FechaEmision.ToString(FormatoFecha, CultureInfo.InvariantCulture);
    public string HoyIso => HoraGuatemala.Hoy(reloj).ToString(FormatoFecha, CultureInfo.InvariantCulture);

    public Task<IActionResult> OnGetAsync(string clave, long id, string? fecha, CancellationToken ct) =>
        MostrarAsync(clave, id, fecha, ct);

    public async Task<IActionResult> OnPostAsync(string clave, long id, string? fecha, bool confirmar, CancellationToken ct)
    {
        var acceso = await AutorizarAsync(clave, id, ct);
        if (acceso is not null) return acceso;

        var solicitada = ParsearFecha(fecha, out var errorFecha);
        if (errorFecha is not null) return await MostrarAsync(clave, id, null, ct, errorFecha);

        var r = await servicio.EmitirAsync(
            new SolicitudEmision(clave, id, solicitada, PuedeCambiarFecha, confirmar), ct);

        switch (r.Estado)
        {
            case EstadoConstancia.Ok:
                // inline: el navegador lo abre en la pestaña nueva para imprimir o descargar.
                Response.Headers.ContentDisposition = $"inline; filename=\"{r.NombreArchivo}\"";
                Response.Headers.CacheControl = "no-store";
                return File(r.Pdf!, "application/pdf");

            case EstadoConstancia.NoExiste:
                return NotFound();

            default:
                return await MostrarAsync(clave, id, fecha, ct, r.Error);
        }
    }

    /// <summary>Imagen del logo para la vista previa (solo quien puede imprimir este registro).</summary>
    public async Task<IActionResult> OnGetLogoAsync(string clave, long id, CancellationToken ct)
    {
        var acceso = await AutorizarAsync(clave, id, ct);
        if (acceso is not null) return acceso;

        return await servicio.LogoAsync(ct) is { } logo ? File(logo.Contenido, logo.Tipo) : NotFound();
    }

    private async Task<IActionResult> MostrarAsync(
        string clave, long id, string? fecha, CancellationToken ct, string? errorPrevio = null)
    {
        var acceso = await AutorizarAsync(clave, id, ct);
        if (acceso is not null) return acceso;

        var solicitada = ParsearFecha(fecha, out var errorFecha);
        var r = await servicio.PrepararAsync(clave, id, errorFecha is null ? solicitada : null, PuedeCambiarFecha, ct);
        if (r.Estado == EstadoConstancia.NoExiste) return NotFound();

        // Si la fecha pedida no sirve, se muestra la vista con la fecha de hoy y el motivo.
        if (r.Estado == EstadoConstancia.Invalida && (solicitada is not null || errorFecha is not null))
        {
            errorPrevio ??= errorFecha ?? r.Error;
            r = await servicio.PrepararAsync(clave, id, null, PuedeCambiarFecha, ct);
        }

        if (r.Estado != EstadoConstancia.Ok)
        {
            Error = r.Error;
            RutaDetalle = Ruta(clave, id);
            return Page(); // sin Vista: la página muestra solo el motivo por el que no se puede imprimir
        }

        Error = errorPrevio;
        Vista = r.Vista!;
        Hoja = new VistaConstanciaModel(
            Vista.Documento, Vista.TieneLogo ? Url.Page("/Constancias/Emitir", "Logo", new { clave, id }) : null);
        return Page();
    }

    /// <summary>Null si puede continuar; si no, el resultado a devolver (404, o acceso denegado).</summary>
    private async Task<IActionResult?> AutorizarAsync(string clave, long id, CancellationToken ct)
    {
        Clave = clave;
        Id = id;
        RutaDetalle = Ruta(clave, id);
        if (modulos.All(m => m.Clave != clave)) return NotFound();

        // La primera impresión pide Constancias.Imprimir; las siguientes, Constancias.Reimprimir.
        var yaEmitida = await servicio.ContarEmitidasAsync(clave, id, ct) > 0;
        var permiso = yaEmitida ? Permisos.ConstanciasReimprimir : Permisos.ConstanciasImprimir;
        if (!(await autorizacion.AuthorizeAsync(User, permiso)).Succeeded) return Forbid();

        PuedeCambiarFecha = (await autorizacion.AuthorizeAsync(User, Permisos.ConstanciasCambiarFecha)).Succeeded;
        return null;
    }

    private string Ruta(string clave, long id) =>
        modulos.FirstOrDefault(m => m.Clave == clave)?.RutaDetalle(id) ?? "/";

    private static DateOnly? ParsearFecha(string? texto, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(texto)) return null;
        if (DateOnly.TryParseExact(texto.Trim(), FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
            return f;

        error = "La fecha de emisión no es válida.";
        return null;
    }
}
