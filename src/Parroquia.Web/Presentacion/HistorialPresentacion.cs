using System.Globalization;
using System.Text.Json;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Web.Presentacion;

public record CambioVista(string Campo, string Anterior, string Nuevo);

public record FilaAuditoria(string Fecha, string Usuario, string Entidad, long EntidadId, string Accion,
    IReadOnlyList<CambioVista> Cambios);

public record FilaBitacora(string Fecha, string Usuario, string Evento, string? Entidad, long? EntidadId,
    string? Detalle, string? Ip);

/// <summary>Cómo se muestran la auditoría y la bitácora (hora de Guatemala, etiquetas en español). Lo usan el Historial de Administración y el detalle de cada registro.</summary>
public static class HistorialPresentacion
{
    public static FilaAuditoria Fila(Auditoria a) => new(
        Formato(a.Fecha), a.UsuarioNombre, a.Entidad, a.EntidadId, EtiquetaAccion(a.Accion), LeerCambios(a.CambiosJson));

    public static FilaBitacora Fila(BitacoraAcceso b) => new(
        Formato(b.Fecha), b.UsuarioNombre, EtiquetaEvento(b.Evento), b.Entidad, b.EntidadId, b.Detalle, b.Ip);

    public static string Formato(DateTimeOffset f) =>
        HoraGuatemala.Local(f).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string EtiquetaAccion(AccionAuditoria a) => a switch
    {
        AccionAuditoria.Alta => "Alta",
        AccionAuditoria.Modificacion => "Modificación",
        AccionAuditoria.Anulacion => "Anulación",
        AccionAuditoria.RevertirAnulacion => "Reversión de anulación",
        _ => a.ToString(),
    };

    public static string EtiquetaEvento(EventoAcceso e) => e switch
    {
        EventoAcceso.InicioSesion => "Inicio de sesión",
        EventoAcceso.InicioSesionFallido => "Inicio de sesión fallido",
        EventoAcceso.CierreSesion => "Cierre de sesión",
        EventoAcceso.CuentaBloqueada => "Cuenta bloqueada",
        EventoAcceso.VerRegistro => "Ver registro",
        EventoAcceso.ImprimirConstancia => "Imprimir constancia",
        _ => e.ToString(),
    };

    public static IReadOnlyList<CambioVista> LeerCambios(string json)
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
