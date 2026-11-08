using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Historial;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Web.Pages.Admin;

public record CambioVista(string Campo, string Anterior, string Nuevo);

public record FilaAuditoria(string Fecha, string Usuario, string Entidad, long EntidadId, string Accion,
    IReadOnlyList<CambioVista> Cambios);

public record FilaBitacora(string Fecha, string Usuario, string Evento, string? Entidad, long? EntidadId,
    string? Detalle, string? Ip);

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
            FilasBitacora = [.. r.Items.Select(b => new FilaBitacora(
                Formato(b.Fecha), b.UsuarioNombre, EtiquetaEvento(b.Evento), b.Entidad, b.EntidadId, b.Detalle, b.Ip))];
            Resumir(r);
        }
        else
        {
            var r = await servicio.AuditoriaAsync(filtro, pagina);
            FilasAuditoria = [.. r.Items.Select(a => new FilaAuditoria(
                Formato(a.Fecha), a.UsuarioNombre, a.Entidad, a.EntidadId, EtiquetaAccion(a.Accion), LeerCambios(a.CambiosJson)))];
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

    private static string Formato(DateTimeOffset f) =>
        HoraGuatemala.Local(f).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string EtiquetaAccion(AccionAuditoria a) => a switch
    {
        AccionAuditoria.Alta => "Alta",
        AccionAuditoria.Modificacion => "Modificación",
        AccionAuditoria.Anulacion => "Anulación",
        AccionAuditoria.RevertirAnulacion => "Reversión de anulación",
        _ => a.ToString(),
    };

    private static string EtiquetaEvento(EventoAcceso e) => e switch
    {
        EventoAcceso.InicioSesion => "Inicio de sesión",
        EventoAcceso.InicioSesionFallido => "Inicio de sesión fallido",
        EventoAcceso.CierreSesion => "Cierre de sesión",
        EventoAcceso.CuentaBloqueada => "Cuenta bloqueada",
        EventoAcceso.VerRegistro => "Ver registro",
        EventoAcceso.ImprimirConstancia => "Imprimir constancia",
        _ => e.ToString(),
    };

    private static IReadOnlyList<CambioVista> LeerCambios(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];

            return [.. doc.RootElement.EnumerateObject().Select(c => new CambioVista(
                c.Name, Texto(c.Value, "anterior"), Texto(c.Value, "nuevo")))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Texto(JsonElement cambio, string propiedad)
    {
        if (cambio.ValueKind != JsonValueKind.Object || !cambio.TryGetProperty(propiedad, out var v)) return "—";
        return v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "—",
            JsonValueKind.True => "Sí",
            JsonValueKind.False => "No",
            JsonValueKind.String => v.GetString() is { Length: > 0 } s ? s : "—",
            _ => v.GetRawText(),
        };
    }
}
