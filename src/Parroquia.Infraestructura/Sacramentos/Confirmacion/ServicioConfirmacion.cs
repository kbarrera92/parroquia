using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Lo que digita el capturista. La fecha va como texto ("17/10/2026", "10/2026", "2026") y se interpreta al guardar.</summary>
public sealed record DatosConfirmacionFormulario(
    string? Libro, string? Folio, string? Partida,
    string? Nombres, string? Apellidos, string? FechaConfirmacion, IReadOnlyList<string?>? Padrinos,
    string? Ministro, string? NotasMarginales,
    bool Revisar, string? MotivoRevision);

/// <summary>Registro listo para mostrar o editar (sin exponer la entidad a la capa web).</summary>
public sealed record VistaConfirmacion(
    long Id, string Nombres, string Apellidos,
    string FechaConfirmacion, string FechaConfirmacionCaptura, IReadOnlyList<string> Padrinos,
    string Libro, string Folio, string Partida, string? Ministro, string? NotasMarginales,
    bool Revisar, string? MotivoRevision, bool Anulado, string? MotivoAnulacion, uint Version,
    string? CreadoPor, DateTimeOffset CreadoEn, string? ModificadoPor, DateTimeOffset? ModificadoEn);

public sealed record FiltroConfirmacion(
    string? Texto = null, string? Libro = null, string? Folio = null,
    short? AnioDesde = null, short? AnioHasta = null,
    bool SoloRevisar = false, bool IncluirAnulados = false);

public sealed record FilaConfirmacion(
    long Id, string Nombres, string Apellidos, string Fecha,
    string Libro, string Folio, string Partida, bool Revisar, bool Anulado);

/// <summary>Captura, edición, anulación y búsqueda de Confirmaciones. Calcado de <see cref="ServicioBautismo"/>.</summary>
public sealed class ServicioConfirmacion(ParroquiaDbContext db, TimeProvider reloj)
{
    public const int MaxDuplicadosMostrados = 5;
    public const int MaxPadrinos = 10;

    /// <summary>Nombre con el que figura en Auditoria y BitacoraAcceso.</summary>
    public const string NombreEntidad = nameof(Confirmacion);

    public async Task<ResultadoGuardado> GuardarAsync(
        DatosConfirmacionFormulario d, long? id = null, uint? version = null,
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
        var padrinos = LimpiarPadrinos(errores, d.Padrinos);

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
        if (!FechaParcial.TryParsear(d.FechaConfirmacion, hoy, out var fechaConfirmacion, out var errorFecha))
            errores.Add(new ErrorCampo(nameof(d.FechaConfirmacion), errorFecha));
        else if (fechaConfirmacion.Anio is null)
            errores.Add(new ErrorCampo(nameof(d.FechaConfirmacion), "Indique al menos el año de la confirmación."));

        if (errores.Count > 0) return new ResultadoGuardado(EstadoGuardado.Invalido, Errores: errores);

        Confirmacion registro;
        if (id is null)
        {
            registro = new Confirmacion();
        }
        else
        {
            var existente = await db.Confirmaciones.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
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
        registro.FechaConfirmacion = fechaConfirmacion!;
        registro.Padrinos = padrinos; // lista nueva siempre: así se detecta y audita el cambio
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

        if (id is null) db.Confirmaciones.Add(registro);

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

    /// <summary>Limpia espacios, descarta vacíos y valida cantidad y largo. Conserva el orden del libro.</summary>
    private static List<string> LimpiarPadrinos(List<ErrorCampo> errores, IReadOnlyList<string?>? entrada)
    {
        var lista = new List<string>();
        foreach (var texto in entrada ?? [])
        {
            if (TextoLibro.Limpiar(texto) is not { } p) continue;
            if (p.Length > ValidadorRegistro.MaxNombre)
            {
                errores.Add(new ErrorCampo("Padrinos", $"Cada padrino admite máximo {ValidadorRegistro.MaxNombre} caracteres."));
                return [];
            }
            lista.Add(p);
        }

        if (lista.Count > MaxPadrinos)
        {
            errores.Add(new ErrorCampo("Padrinos", $"Se admiten máximo {MaxPadrinos} padrinos."));
            return [];
        }
        return lista;
    }

    /// <summary>
    /// Advertencia (no bloqueo): mismo libro+folio+partida, o misma persona con la misma fecha de confirmación.
    /// Pueden existir varias partidas por folio y libros reiniciados, por eso solo se avisa.
    /// </summary>
    private async Task<IReadOnlyList<RegistroDuplicado>> BuscarDuplicadosAsync(Confirmacion r, long idActual, CancellationToken ct)
    {
        var (lo, fo, po, nombre, clave) = (r.LibroOrden, r.FolioOrden, r.PartidaOrden, r.BusquedaNormalizada, r.FechaConfirmacion.ClaveOrden);

        var encontrados = await db.Confirmaciones.AsNoTracking()
            .Where(c => c.Id != idActual
                && ((c.LibroOrden == lo && c.FolioOrden == fo && c.PartidaOrden == po)
                    || (nombre != "" && c.BusquedaNormalizada == nombre
                        && EF.Property<int>(c, ConfiguracionConfirmacion.ClaveFecha) == clave)))
            .OrderBy(c => c.Id)
            .Take(MaxDuplicadosMostrados)
            .ToListAsync(ct);

        return [.. encontrados.Select(c => new RegistroDuplicado(
            c.Id, $"{c.Nombres} {c.Apellidos}".Trim(), c.Libro, c.Folio, c.Partida, c.FechaConfirmacion.ATexto()))];
    }

    public async Task<VistaConfirmacion?> ObtenerVistaAsync(long id, bool incluirAnulados = false, CancellationToken ct = default)
    {
        var q = db.Confirmaciones.AsNoTracking().Include(c => c.Ministro).AsQueryable();
        if (incluirAnulados) q = q.IgnoreQueryFilters();

        var c = await q.FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : new VistaConfirmacion(
            c.Id, c.Nombres, c.Apellidos,
            c.FechaConfirmacion.ATexto(), c.FechaConfirmacion.ATextoCaptura(), [.. c.Padrinos],
            c.Libro, c.Folio, c.Partida, c.NombreMinistro, c.NotasMarginales,
            c.Revisar, c.MotivoRevision, c.Anulado, c.MotivoAnulacion, c.Version,
            c.CreadoPor, c.CreadoEn, c.ModificadoPor, c.ModificadoEn);
    }

    /// <summary>Datos que imprime la constancia; null si el registro no existe o está anulado.</summary>
    public async Task<DatosConfirmacion?> DatosParaConstanciaAsync(long id, CancellationToken ct = default)
    {
        var c = await db.Confirmaciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : new DatosConfirmacion(
            c.Nombres, c.Apellidos, c.FechaConfirmacion, [.. c.Padrinos], c.Libro, c.Folio, c.Revisar);
    }

    public async Task<ResultadoOperacion> AnularAsync(long id, string? motivo, CancellationToken ct = default)
    {
        var errores = new List<ErrorCampo>();
        var limpio = ValidadorRegistro.Texto(errores, "Motivo", motivo, "el motivo de la anulación", ValidadorRegistro.MaxMotivo, true);
        if (limpio is null) return ResultadoOperacion.Falla(errores[0].Mensaje);

        var c = await db.Confirmaciones.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return ResultadoOperacion.Falla("El registro ya no existe.");
        if (c.Anulado) return ResultadoOperacion.Falla("El registro ya está anulado.");

        c.Anulado = true;
        c.MotivoAnulacion = limpio;
        return await GuardarCambioAsync(ct);
    }

    /// <summary>Solo el Administrador (lo exige la página con <c>Registros.RevertirAnulacion</c>).</summary>
    public async Task<ResultadoOperacion> RevertirAnulacionAsync(long id, CancellationToken ct = default)
    {
        var c = await db.Confirmaciones.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
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

    /// <summary>Filas de los ids pedidos (los anulados no salen por el filtro global). Para mostrar vínculos.</summary>
    public async Task<IReadOnlyList<FilaConfirmacion>> ResumenesAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        var lista = ids.ToArray();
        var registros = await db.Confirmaciones.AsNoTracking().Where(c => lista.Contains(c.Id)).ToListAsync(ct);
        return [.. registros.Select(c => new FilaConfirmacion(
            c.Id, c.Nombres, c.Apellidos, c.FechaConfirmacion.ATexto(), c.Libro, c.Folio, c.Partida, c.Revisar, c.Anulado))];
    }

    /// <summary>Búsqueda parcial sin tildes ni mayúsculas; ordenada por apellidos, nombres y luego libro/folio/partida.</summary>
    public async Task<PaginaResultado<FilaConfirmacion>> BuscarAsync(
        FiltroConfirmacion f, int pagina, int tamano = Paginacion.TamanoPorDefecto, CancellationToken ct = default)
    {
        var q = db.Confirmaciones.AsNoTracking().AsQueryable();
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
            q = q.Where(c => EF.Property<int>(c, ConfiguracionConfirmacion.ClaveFecha) >= minimo);
        }

        if (f.AnioHasta is { } hasta)
        {
            var maximo = hasta * 10000 + 9999;
            q = q.Where(c => EF.Property<int>(c, ConfiguracionConfirmacion.ClaveFecha) <= maximo);
        }

        if (f.SoloRevisar) q = q.Where(c => c.Revisar);

        var pag = await q
            .OrderBy(c => c.Apellidos).ThenBy(c => c.Nombres)
            .ThenBy(c => c.LibroOrden).ThenBy(c => c.FolioOrden).ThenBy(c => c.PartidaOrden).ThenBy(c => c.Id)
            .PaginarAsync(pagina, tamano, ct);

        return new PaginaResultado<FilaConfirmacion>(
            [.. pag.Items.Select(c => new FilaConfirmacion(
                c.Id, c.Nombres, c.Apellidos, c.FechaConfirmacion.ATexto(), c.Libro, c.Folio, c.Partida, c.Revisar, c.Anulado))],
            pag.Total, pag.PaginaActual, pag.TamanoPagina);
    }
}
