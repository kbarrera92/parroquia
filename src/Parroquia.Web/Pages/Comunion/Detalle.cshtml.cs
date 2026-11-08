using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Constancias;
using Parroquia.Infraestructura.Historial;
using Parroquia.Infraestructura.Sacramentos;
using Parroquia.Web.Presentacion;

namespace Parroquia.Web.Pages.Comunion;

[Authorize(Policy = Permisos.RegistrosVer)]
public class DetalleModel(
    ServicioComunion servicio,
    ServicioHistorial historial,
    ServicioConstancias constancias,
    IBitacoraAcceso bitacora,
    IAuthorizationService autorizacion) : PageModel
{
    public VistaComunion Registro { get; private set; } = null!;
    public IReadOnlyList<FilaAuditoria> Movimientos { get; private set; } = [];
    public bool PuedeEditar { get; private set; }
    public bool PuedeAnular { get; private set; }
    public bool PuedeRevertir { get; private set; }
    public bool PuedeImprimir { get; private set; }
    public bool YaImpresa => Emitidas.Count > 0;
    public IReadOnlyList<ConstanciaEmitida> Emitidas { get; private set; } = [];
    public string Clave => ModuloComunion.ClaveModulo;

    public string? Mensaje => TempData["Mensaje"] as string;
    public string? Error => TempData["Error"] as string;

    public string NombreCompleto
    {
        get
        {
            var n = $"{Registro.Nombres} {Registro.Apellidos}".Trim();
            return n.Length == 0 ? "(sin nombre)" : n;
        }
    }

    public static string FormatoEmision(DateTimeOffset f) => HistorialPresentacion.Formato(f);

    public string Creado => $"{Registro.CreadoPor} · {HistorialPresentacion.Formato(Registro.CreadoEn)}";

    public string? Modificado => Registro.ModificadoEn is { } m && m != Registro.CreadoEn
        ? $"{Registro.ModificadoPor} · {HistorialPresentacion.Formato(m)}"
        : null;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken ct)
    {
        await CargarPermisosAsync();
        var v = await servicio.ObtenerVistaAsync(id, incluirAnulados: PuedeRevertir, ct: ct);
        if (v is null) return NotFound();
        Registro = v;

        Emitidas = await constancias.ListarEmitidasAsync(Clave, id, ct);
        // Primera impresión: Constancias.Imprimir; las siguientes: Constancias.Reimprimir.
        PuedeImprimir = !v.Anulado && (await autorizacion.AuthorizeAsync(
            User, YaImpresa ? Permisos.ConstanciasReimprimir : Permisos.ConstanciasImprimir)).Succeeded;

        Movimientos = [.. (await historial.DeRegistroAsync(ServicioComunion.NombreEntidad, id, ct)).Select(HistorialPresentacion.Fila)];
        await bitacora.RegistrarAsync(EventoAcceso.VerRegistro, entidad: ServicioComunion.NombreEntidad, entidadId: id, ct: ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAnularAsync(long id, string? motivo, CancellationToken ct)
    {
        await CargarPermisosAsync();
        if (!PuedeAnular) return Forbid();

        var r = await servicio.AnularAsync(id, motivo, ct);
        return Terminar(id, r, "El registro quedó anulado. Ya no aparece en búsquedas ni listados.");
    }

    public async Task<IActionResult> OnPostRevertirAsync(long id, CancellationToken ct)
    {
        await CargarPermisosAsync();
        if (!PuedeRevertir) return Forbid();

        var r = await servicio.RevertirAnulacionAsync(id, ct);
        return Terminar(id, r, "Se revirtió la anulación: el registro vuelve a estar activo.");
    }

    private IActionResult Terminar(long id, ResultadoOperacion r, string exito)
    {
        if (r.Ok) TempData["Mensaje"] = exito;
        else TempData["Error"] = r.Error;
        return RedirectToPage(new { id });
    }

    private async Task CargarPermisosAsync()
    {
        PuedeEditar = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosEditar)).Succeeded;
        PuedeAnular = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosAnular)).Succeeded;
        PuedeRevertir = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosRevertirAnulacion)).Succeeded;
    }
}
