using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Historial;
using Parroquia.Web.Presentacion;

namespace Parroquia.Web.Pages.Admin;

[Authorize(Policy = Permisos.AuditoriaVer)]
public class HistorialModel(ServicioHistorial servicio) : PageModel
{
    private const string FormatoFecha = "yyyy-MM-dd";

    [BindProperty(SupportsGet = true)] public string? Pestana { get; set; }
    [BindProperty(SupportsGet = true)] public string? Usuario { get; set; }
    [BindProperty(SupportsGet = true)] public string? Entidad { get; set; }
    [BindProperty(SupportsGet = true)] public string? Desde { get; set; }
    [BindProperty(SupportsGet = true)] public string? Hasta { get; set; }
    [BindProperty(SupportsGet = true)] public string? Pagina { get; set; }

    public bool EsBitacora => string.Equals(Pestana, "bitacora", StringComparison.OrdinalIgnoreCase);
    public string PestanaActual => EsBitacora ? "bitacora" : "auditoria";
    public IReadOnlyList<FilaAuditoria> FilasAuditoria { get; private set; } = [];
    public IReadOnlyList<FilaBitacora> FilasBitacora { get; private set; } = [];
    public int Total { get; private set; }
    public int PaginaActual { get; private set; } = 1;
    public int TotalPaginas { get; private set; } = 1;
    public string? Aviso { get; private set; }

    public async Task OnGetAsync()
    {
        var filtro = new FiltroHistorial(
            Usuario, Entidad, ParsearFecha(Desde, "inicial"), ParsearFecha(Hasta, "final"));
        var pagina = int.TryParse(Pagina, NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 1;

        if (EsBitacora)
        {
            var r = await servicio.BitacoraAsync(filtro, pagina);
            FilasBitacora = [.. r.Items.Select(HistorialPresentacion.Fila)];
            Resumir(r);
        }
        else
        {
            var r = await servicio.AuditoriaAsync(filtro, pagina);
            FilasAuditoria = [.. r.Items.Select(HistorialPresentacion.Fila)];
            Resumir(r);
        }
    }

    private void Resumir<T>(PaginaResultado<T> r)
    {
        Total = r.Total;
        PaginaActual = r.PaginaActual;
        TotalPaginas = r.TotalPaginas;
    }

    private DateOnly? ParsearFecha(string? texto, string etiqueta)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        if (DateOnly.TryParseExact(texto.Trim(), FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
            return f;

        Aviso = $"Se ignoró la fecha {etiqueta} porque no es válida.";
        return null;
    }
}
