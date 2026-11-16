using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Operacion;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Web.Pages.Admin;

[Authorize(Policy = Permisos.Configuracion)]
public class EstadoModel(IEstadoSistema estado) : PageModel
{
    public EstadoSistema Estado { get; private set; } = null!;

    public async Task OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        Estado = await estado.ObtenerAsync(HttpContext.RequestAborted);
    }

    public static string Alerta(ElementoEstado e) => e.Nivel == NivelEstado.Correcto ? "alert-success" : "alert-warning";

    public static string Rol(ElementoEstado e) => e.Nivel == NivelEstado.Correcto ? "status" : "alert";

    public string Encendido
    {
        get
        {
            var t = DateTimeOffset.UtcNow - Estado.Inicio;
            return t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h" : $"{t.Hours} h {t.Minutes} min";
        }
    }

    public string InicioLocal => HoraGuatemala.Local(Estado.Inicio).ToString("d 'de' MMMM 'de' yyyy, HH:mm", CultureInfo.GetCultureInfo("es-GT"));

    public string TamanoBase
    {
        get
        {
            var mb = Estado.BytesBaseDatos / 1024d / 1024d;
            return mb >= 1024
                ? string.Create(CultureInfo.GetCultureInfo("es-GT"), $"{mb / 1024:0.##} GB")
                : string.Create(CultureInfo.GetCultureInfo("es-GT"), $"{mb:0.#} MB");
        }
    }
}
