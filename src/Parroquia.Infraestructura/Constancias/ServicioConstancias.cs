using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Configuracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Constancias;

public enum EstadoConstancia
{
    Ok,

    /// <summary>El sacramento o el registro no existen (o el registro está anulado).</summary>
    NoExiste,

    /// <summary>Falta algo para imprimir o la petición no es válida; <c>Error</c> lo explica en español.</summary>
    Invalida,
}

/// <param name="FechaEmision">Null = hoy. Distinta de hoy solo si <paramref name="PuedeCambiarFecha"/> (Administrador).</param>
/// <param name="ConfirmaAdvertencias">El usuario vio los avisos (dato faltante, registro en "Revisar") y quiere imprimir igual.</param>
public sealed record SolicitudEmision(
    string Clave, long RegistroId, DateOnly? FechaEmision = null,
    bool PuedeCambiarFecha = false, bool ConfirmaAdvertencias = false);

public sealed record VistaPreviaConstancia(
    string NombreSacramento, DocumentoConstancia Documento, IReadOnlyList<string> Advertencias,
    DateOnly FechaEmision, int EmisionesPrevias, bool TieneLogo);

public sealed record ResultadoPreparacion(EstadoConstancia Estado, string? Error = null, VistaPreviaConstancia? Vista = null);

public sealed record ResultadoEmision(
    EstadoConstancia Estado, string? Error = null, byte[]? Pdf = null, string? NombreArchivo = null);

/// <summary>
/// Arma, imprime y registra constancias de cualquier sacramento registrado. El texto lo decide la plantilla del
/// módulo; aquí solo se juntan membrete, párroco vigente y fecha, se genera el PDF y se deja la bitácora.
/// </summary>
public sealed class ServicioConstancias(
    ParroquiaDbContext db,
    ServicioConfiguracionParroquia configuracion,
    ServicioParroco parroco,
    IEnumerable<IModuloSacramental> modulos,
    GeneradorPdfConstancia pdf,
    IUsuarioActual usuario,
    IBitacoraAcceso bitacora,
    TimeProvider reloj)
{
    private sealed record Armada(
        IModuloSacramental Modulo, ConstanciaPreparada Constancia, DateOnly Fecha, byte[]? Logo, string Firmante);

    public async Task<ResultadoPreparacion> PrepararAsync(
        string clave, long registroId, DateOnly? fecha, bool puedeCambiarFecha, CancellationToken ct = default)
    {
        var (armada, estado, error) = await ArmarAsync(clave, registroId, fecha, puedeCambiarFecha, ct);
        if (armada is null) return new ResultadoPreparacion(estado, error);

        var previas = await ContarEmitidasAsync(clave, registroId, ct);
        return new ResultadoPreparacion(EstadoConstancia.Ok, null, new VistaPreviaConstancia(
            armada.Modulo.Nombre, armada.Constancia.Documento, armada.Constancia.Advertencias,
            armada.Fecha, previas, armada.Logo is { Length: > 0 }));
    }

    public async Task<ResultadoEmision> EmitirAsync(SolicitudEmision s, CancellationToken ct = default)
    {
        var (armada, estado, error) = await ArmarAsync(s.Clave, s.RegistroId, s.FechaEmision, s.PuedeCambiarFecha, ct);
        if (armada is null) return new ResultadoEmision(estado, error);

        var advertencias = armada.Constancia.Advertencias;
        if (advertencias.Count > 0 && !s.ConfirmaAdvertencias)
            return new ResultadoEmision(EstadoConstancia.Invalida, "La constancia tiene avisos: confírmelos para imprimir.");

        var bytes = pdf.Generar(armada.Constancia.Documento, armada.Logo);
        var hoy = HoraGuatemala.Hoy(reloj);
        var ajustada = armada.Fecha != hoy;

        db.ConstanciasEmitidas.Add(new ConstanciaEmitida
        {
            TipoSacramento = armada.Modulo.Clave,
            RegistroId = s.RegistroId,
            EmitidaEn = reloj.GetUtcNow(),
            EmitidaPor = usuario.Nombre,
            UsuarioId = usuario.Id,
            FechaImpresa = armada.Fecha,
            FechaAjustadaPor = ajustada ? usuario.Nombre : null,
            Firmante = armada.Firmante,
            InstantaneaJson = JsonSerializer.Serialize(armada.Constancia.Documento),
            HashPdf = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ConAdvertencias = advertencias.Count > 0,
        });

        // La bitácora de acceso guarda con el mismo DbContext: constancia y bitácora se confirman juntas o ninguna.
        await bitacora.RegistrarAsync(
            EventoAcceso.ImprimirConstancia, entidad: armada.Modulo.Entidad, entidadId: s.RegistroId,
            detalle: $"Constancia de {armada.Modulo.Nombre} con fecha {armada.Fecha:dd/MM/yyyy}" + (ajustada ? " (fecha ajustada)" : ""),
            ct: ct);

        return new ResultadoEmision(EstadoConstancia.Ok, null, bytes, $"constancia-{armada.Modulo.Clave}-{s.RegistroId}.pdf");
    }

    public Task<int> ContarEmitidasAsync(string clave, long registroId, CancellationToken ct = default) =>
        db.ConstanciasEmitidas.CountAsync(c => c.TipoSacramento == clave && c.RegistroId == registroId, ct);

    /// <summary>Constancias ya impresas de un registro, la más reciente primero.</summary>
    public Task<List<ConstanciaEmitida>> ListarEmitidasAsync(string clave, long registroId, CancellationToken ct = default) =>
        db.ConstanciasEmitidas.AsNoTracking()
            .Where(c => c.TipoSacramento == clave && c.RegistroId == registroId)
            .OrderByDescending(c => c.EmitidaEn).ThenByDescending(c => c.Id)
            .ToListAsync(ct);

    /// <summary>Logo del membrete para la vista previa; null si no hay.</summary>
    public async Task<(byte[] Contenido, string Tipo)?> LogoAsync(CancellationToken ct = default)
    {
        var c = await configuracion.ObtenerAsync(ct);
        return c?.Logo is { Length: > 0 } logo ? (logo, c.LogoTipoContenido ?? "image/png") : null;
    }

    private async Task<(Armada? Armada, EstadoConstancia Estado, string? Error)> ArmarAsync(
        string clave, long registroId, DateOnly? solicitada, bool puedeCambiarFecha, CancellationToken ct)
    {
        var modulo = modulos.FirstOrDefault(m => string.Equals(m.Clave, clave, StringComparison.Ordinal));
        if (modulo is null) return (null, EstadoConstancia.NoExiste, null);

        var hoy = HoraGuatemala.Hoy(reloj);
        var fecha = solicitada ?? hoy;
        if (fecha != hoy && !puedeCambiarFecha)
            return (null, EstadoConstancia.Invalida, "Solo el Administrador puede cambiar la fecha de emisión.");
        if (fecha > hoy)
            return (null, EstadoConstancia.Invalida, "La fecha de emisión no puede ser futura.");
        if (fecha.Year < 1900)
            return (null, EstadoConstancia.Invalida, "La fecha de emisión no es válida.");

        var conf = await configuracion.ObtenerAsync(ct);
        if (conf is null)
            return (null, EstadoConstancia.Invalida, "Falta la configuración de la parroquia (Administración → Configuración).");

        var firmante = await parroco.ObtenerActualAsync(ct);
        if (firmante is null)
            return (null, EstadoConstancia.Invalida, "No hay un párroco vigente: regístrelo en Administración → Párroco antes de imprimir.");

        var contexto = new ContextoConstancia(
            conf.Direccion, conf.Municipio, conf.Telefono, conf.CiudadAbreviada, conf.TextoLugarEmision,
            firmante.Nombre, firmante.Titulo, fecha, conf.Logo);

        var preparada = await modulo.PrepararConstanciaAsync(registroId, contexto, ct);
        return preparada is null
            ? (null, EstadoConstancia.NoExiste, null)
            : (new Armada(modulo, preparada, fecha, conf.Logo, firmante.Nombre), EstadoConstancia.Ok, null);
    }
}
