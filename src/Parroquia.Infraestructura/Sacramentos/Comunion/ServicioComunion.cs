using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Lo que digita el capturista. La fecha va como texto ("13/8/1978", "8/1978", "1978") y se interpreta al guardar.</summary>
public sealed record DatosComunionFormulario(
    string? Libro, string? Folio, string? Partida,
    string? Nombres, string? Apellidos, string? Fecha,
    string? Ministro, string? NotasMarginales,
    bool Revisar, string? MotivoRevision);

/// <summary>Registro listo para mostrar o editar (sin exponer la entidad a la capa web).</summary>
public sealed record VistaComunion(
    long Id, string Nombres, string Apellidos, string Fecha, string FechaCaptura,
    string Libro, string Folio, string Partida, string? Ministro, string? NotasMarginales,
    bool Revisar, string? MotivoRevision, bool Anulado, string? MotivoAnulacion, uint Version,
    string? CreadoPor, DateTimeOffset CreadoEn, string? ModificadoPor, DateTimeOffset? ModificadoEn);

public sealed record FiltroComunion(
    string? Texto = null, string? Libro = null, string? Folio = null,
    short? AnioDesde = null, short? AnioHasta = null,
    bool SoloRevisar = false, bool IncluirAnulados = false);

public sealed record FilaComunion(
    long Id, string Nombres, string Apellidos, string Fecha,
    string Libro, string Folio, string Partida, bool Revisar, bool Anulado);

/// <summary>Captura, edición, anulación y búsqueda de Primeras Comuniones. Patrón de los demás módulos.</summary>
public sealed class ServicioComunion(ParroquiaDbContext db, TimeProvider reloj)
{
    public const int MaxDuplicadosMostrados = 5;

    /// <summary>Nombre con el que figura en Auditoria y BitacoraAcceso.</summary>
    public const string NombreEntidad = nameof(Comunion);

    public async Task<ResultadoGuardado> GuardarAsync(
        DatosComunionFormulario d, long? id = null, uint? version = null,
        bool confirmarDuplicado = false, CancellationToken ct = default)
    {
        var errores = new List<ErrorCampo>();
        var libro = ValidadorRegistro.Texto(errores, nameof(d.Libro), d.Libro, "el libro", ValidadorRegistro.MaxClave, true);
        var folio = ValidadorRegistro.Texto(errores, nameof(d.Folio), d.Folio, "el folio", ValidadorRegistro.MaxClave, true);
        var partida = ValidadorRegistro.Texto(errores, nameof(d.Partida), d.Partida, "la partida", ValidadorRegistro.MaxClave, true);
        var nombres = ValidadorRegistro.Texto(errores, nameof(d.Nombres), d.Nombres, "los nombres", ValidadorRegistro.MaxNombre, false);
        var apellidos = ValidadorRegistro.Texto(errores, nameof(d.Apellidos), d.Apellidos, "los apellidos", ValidadorRegistro.MaxNombre, false);
        var ministroTexto = ValidadorRegistro.Texto(errores, nameof(d.Ministro), d.Ministro, "el ministro", ValidadorRegistro.MaxNombre, false);
        var motivo = ValidadorRegistro.Texto(errores, nameof(d.MotivoRevision), d.MotivoRevision, "el motivo", ValidadorRegistro.MaxMotivo, false);
        var notas = ValidadorRegistro.Notas(errores, nameof(d.NotasMarginales), d.NotasMarginales);

        // Nombre completo obligatorio; solo un registro marcado "Revisar" con motivo admite dejarlo vacío (docs/02 §6).
        if (nombres is null || apellidos is null)
        {
            if (!d.Revisar)
            {
                if (nombres is null && !errores.Any(e => e.Campo == nameof(d.Nombres)))
                    errores.Add(new ErrorCampo(nameof(d.Nombres), "Escriba los nombres."));
                if (apellidos is null && !errores.Any(e => e.Campo == nameof(d.Apellidos)))
                    errores.Add(new ErrorCampo(nameof(d.Apellidos), "Escriba los apellidos."));
            }
            else if (motivo is null)
            {
                errores.Add(new ErrorCampo(nameof(d.MotivoRevision),
                    "Falta el nombre: indique en el motivo de revisión por qué no se pudo leer."));
            }
        }

        var hoy = HoraGuatemala.Hoy(reloj);
        if (!FechaParcial.TryParsear(d.Fecha, hoy, out var fecha, out var errorFecha))
            errores.Add(new ErrorCampo(nameof(d.Fecha), errorFecha));
        else if (fecha.Anio is null)
            errores.Add(new ErrorCampo(nameof(d.Fecha), "Indique al menos el año de la comunión."));

        if (errores.Count > 0) return new ResultadoGuardado(EstadoGuardado.Invalido, Errores: errores);

        Comunion registro;
        if (id is null)
        {
            registro = new Comunion();
        }
        else
        {
            var existente = await db.Comuniones.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (existente is null) return new ResultadoGuardado(EstadoGuardado.NoExiste);
            if (existente.Anulado) return new ResultadoGuardado(EstadoGuardado.Anulado, existente.Id);

            // Concurrencia optimista: el UPDATE solo prospera si la fila sigue en la versión que vio el usuario.
            db.Entry(existente).Property(c => c.Version).OriginalValue = version ?? existente.Version;
            registro = existente;
        }

        registro.Libro = libro!;
        registro.Folio = folio!;
        registro.Partida = partida!;
        registro.Nombres = nombres ?? "";
        registro.Apellidos = apellidos ?? "";
        registro.FechaComunion = fecha!;
        registro.NotasMarginales = notas;
        registro.Revisar = d.Revisar;
        registro.MotivoRevision = d.Revisar ? motivo : null;
        (registro.MinistroId, registro.MinistroTexto) =
            await ValidadorRegistro.ResolverMinistroAsync(db, ministroTexto, ct);
        registro.ActualizarDerivados();

        if (!confirmarDuplicado)
        {
            var duplicados = await BuscarDuplicadosAsync(registro, id ?? 0, ct);
            if (duplicados.Count > 0)
            {
                if (id is not null) db.Entry(registro).State = EntityState.Unchanged;
                return new ResultadoGuardado(EstadoGuardado.Duplicados, Duplicados: duplicados);
            }
        }

        if (id is null) db.Comuniones.Add(registro);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ResultadoGuardado(EstadoGuardado.Conflicto, registro.Id);
        }

        return new ResultadoGuardado(EstadoGuardado.Ok, registro.Id);
    }

    /// <summary>
    /// Advertencia (no bloqueo): mismo libro+folio+partida, o misma persona con la misma fecha.
    /// Pueden existir varias partidas por folio y libros reiniciados, por eso solo se avisa.
    /// </summary>
    private async Task<IReadOnlyList<RegistroDuplicado>> BuscarDuplicadosAsync(Comunion r, long idActual, CancellationToken ct)
    {
        var (lo, fo, po, nombre, clave) = (r.LibroOrden, r.FolioOrden, r.PartidaOrden, r.BusquedaNormalizada, r.FechaComunion.ClaveOrden);

        var encontrados = await db.Comuniones.AsNoTracking()
            .Where(c => c.Id != idActual
                && ((c.LibroOrden == lo && c.FolioOrden == fo && c.PartidaOrden == po)
                    || (nombre != "" && c.BusquedaNormalizada == nombre
                        && EF.Property<int>(c, ConfiguracionComunion.ClaveFecha) == clave)))
            .OrderBy(c => c.Id)
            .Take(MaxDuplicadosMostrados)
            .ToListAsync(ct);

        return [.. encontrados.Select(c => new RegistroDuplicado(
            c.Id, $"{c.Nombres} {c.Apellidos}".Trim(), c.Libro, c.Folio, c.Partida, c.FechaComunion.ATexto()))];
    }

    public async Task<VistaComunion?> ObtenerVistaAsync(long id, bool incluirAnulados = false, CancellationToken ct = default)
    {
        var q = db.Comuniones.AsNoTracking().Include(c => c.Ministro).AsQueryable();
        if (incluirAnulados) q = q.IgnoreQueryFilters();

        var c = await q.FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : new VistaComunion(
            c.Id, c.Nombres, c.Apellidos, c.FechaComunion.ATexto(), c.FechaComunion.ATextoCaptura(),
            c.Libro, c.Folio, c.Partida, c.NombreMinistro, c.NotasMarginales,
            c.Revisar, c.MotivoRevision, c.Anulado, c.MotivoAnulacion, c.Version,
            c.CreadoPor, c.CreadoEn, c.ModificadoPor, c.ModificadoEn);
    }

    /// <summary>Datos que imprime la constancia; null si el registro no existe o está anulado.</summary>
    public async Task<DatosComunion?> DatosParaConstanciaAsync(long id, CancellationToken ct = default)
    {
        var c = await db.Comuniones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : new DatosComunion(c.Nombres, c.Apellidos, c.FechaComunion, c.Libro, c.Folio, c.Revisar);
    }

    public async Task<ResultadoOperacion> AnularAsync(long id, string? motivo, CancellationToken ct = default)
    {
        var errores = new List<ErrorCampo>();
        var limpio = ValidadorRegistro.Texto(errores, "Motivo", motivo, "el motivo de la anulación", ValidadorRegistro.MaxMotivo, true);
        if (limpio is null) return ResultadoOperacion.Falla(errores[0].Mensaje);

        var c = await db.Comuniones.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return ResultadoOperacion.Falla("El registro ya no existe.");
        if (c.Anulado) return ResultadoOperacion.Falla("El registro ya está anulado.");

        c.Anulado = true;
        c.MotivoAnulacion = limpio;
        return await GuardarCambioAsync(ct);
    }

    /// <summary>Solo el Administrador (lo exige la página con <c>Registros.RevertirAnulacion</c>).</summary>
    public async Task<ResultadoOperacion> RevertirAnulacionAsync(long id, CancellationToken ct = default)
    {
        var c = await db.Comuniones.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return ResultadoOperacion.Falla("El registro ya no existe.");
        if (!c.Anulado) return ResultadoOperacion.Falla("El registro no está anulado.");

        c.Anulado = false;
        c.MotivoAnulacion = null;
        return await GuardarCambioAsync(ct);
    }

    private async Task<ResultadoOperacion> GuardarCambioAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return ResultadoOperacion.Exito;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultadoOperacion.Falla("Otro usuario modificó el registro. Ábralo de nuevo e intente otra vez.");
        }
    }

    /// <summary>Búsqueda parcial sin tildes ni mayúsculas; ordenada por apellidos, nombres y luego libro/folio/partida.</summary>
    public async Task<PaginaResultado<FilaComunion>> BuscarAsync(
        FiltroComunion f, int pagina, int tamano = Paginacion.TamanoPorDefecto, CancellationToken ct = default)
    {
        var q = db.Comuniones.AsNoTracking().AsQueryable();
        if (f.IncluirAnulados) q = q.IgnoreQueryFilters();

        foreach (var token in NormalizadorBusqueda.Normalizar(f.Texto).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var patron = $"%{token}%"; // los tokens normalizados solo tienen letras y dígitos: no hay comodines que escapar
            q = q.Where(c => EF.Functions.Like(c.BusquedaNormalizada, patron));
        }

        if (!string.IsNullOrWhiteSpace(f.Libro))
        {
            var clave = ClaveOrdenNatural.De(f.Libro);
            q = q.Where(c => c.LibroOrden == clave);
        }

        if (!string.IsNullOrWhiteSpace(f.Folio))
        {
            var clave = ClaveOrdenNatural.De(f.Folio);
            q = q.Where(c => c.FolioOrden == clave);
        }

        if (f.AnioDesde is { } desde)
        {
            var minimo = desde * 10000;
            q = q.Where(c => EF.Property<int>(c, ConfiguracionComunion.ClaveFecha) >= minimo);
        }

        if (f.AnioHasta is { } hasta)
        {
            var maximo = hasta * 10000 + 9999;
            q = q.Where(c => EF.Property<int>(c, ConfiguracionComunion.ClaveFecha) <= maximo);
        }

        if (f.SoloRevisar) q = q.Where(c => c.Revisar);

        var pag = await q
            .OrderBy(c => c.Apellidos).ThenBy(c => c.Nombres)
            .ThenBy(c => c.LibroOrden).ThenBy(c => c.FolioOrden).ThenBy(c => c.PartidaOrden).ThenBy(c => c.Id)
            .PaginarAsync(pagina, tamano, ct);

        return new PaginaResultado<FilaComunion>(
            [.. pag.Items.Select(c => new FilaComunion(
                c.Id, c.Nombres, c.Apellidos, c.FechaComunion.ATexto(), c.Libro, c.Folio, c.Partida, c.Revisar, c.Anulado))],
            pag.Total, pag.PaginaActual, pag.TamanoPagina);
    }
}
