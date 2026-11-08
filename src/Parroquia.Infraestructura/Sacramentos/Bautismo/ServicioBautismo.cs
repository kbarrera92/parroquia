using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Lo que digita el capturista. Las fechas van como texto ("13/8/1978", "8/1978", "1978") y se interpretan al guardar.</summary>
public sealed record DatosBautismoFormulario(
    string? Libro, string? Folio, string? Partida,
    string? Nombres, string? Apellidos, string? FechaBautismo, string? FechaNacimiento, string? LugarNacimiento,
    string? Padre, string? Madre, IReadOnlyList<string?>? Padrinos,
    string? Ministro, string? NotasMarginales,
    bool Revisar, string? MotivoRevision);

/// <summary>Registro listo para mostrar o editar (sin exponer la entidad a la capa web).</summary>
public sealed record VistaBautismo(
    long Id, string Nombres, string Apellidos,
    string FechaBautismo, string FechaBautismoCaptura, string FechaNacimiento, string FechaNacimientoCaptura,
    string? LugarNacimiento, string? Padre, string? Madre, IReadOnlyList<string> Padrinos,
    string Libro, string Folio, string Partida, string? Ministro, string? NotasMarginales,
    bool Revisar, string? MotivoRevision, bool Anulado, string? MotivoAnulacion, uint Version,
    string? CreadoPor, DateTimeOffset CreadoEn, string? ModificadoPor, DateTimeOffset? ModificadoEn);

public sealed record FiltroBautismo(
    string? Texto = null, string? Libro = null, string? Folio = null,
    short? AnioDesde = null, short? AnioHasta = null,
    bool SoloRevisar = false, bool IncluirAnulados = false);

public sealed record FilaBautismo(
    long Id, string Nombres, string Apellidos, string Fecha,
    string Libro, string Folio, string Partida, bool Revisar, bool Anulado);

/// <summary>Captura, edición, anulación y búsqueda de Bautismos. Calcado de <see cref="ServicioComunion"/>.</summary>
public sealed class ServicioBautismo(ParroquiaDbContext db, TimeProvider reloj)
{
    public const int MaxDuplicadosMostrados = 5;
    public const int MaxPadrinos = 10;

    /// <summary>Nombre con el que figura en Auditoria y BitacoraAcceso.</summary>
    public const string NombreEntidad = nameof(Bautismo);

    public async Task<ResultadoGuardado> GuardarAsync(
        DatosBautismoFormulario d, long? id = null, uint? version = null,
        bool confirmarDuplicado = false, CancellationToken ct = default)
    {
        var errores = new List<ErrorCampo>();
        var libro = ValidadorRegistro.Texto(errores, nameof(d.Libro), d.Libro, "el libro", ValidadorRegistro.MaxClave, true);
        var folio = ValidadorRegistro.Texto(errores, nameof(d.Folio), d.Folio, "el folio", ValidadorRegistro.MaxClave, true);
        var partida = ValidadorRegistro.Texto(errores, nameof(d.Partida), d.Partida, "la partida", ValidadorRegistro.MaxClave, true);
        var nombres = ValidadorRegistro.Texto(errores, nameof(d.Nombres), d.Nombres, "los nombres", ValidadorRegistro.MaxNombre, false);
        var apellidos = ValidadorRegistro.Texto(errores, nameof(d.Apellidos), d.Apellidos, "los apellidos", ValidadorRegistro.MaxNombre, false);
        var lugar = ValidadorRegistro.Texto(errores, nameof(d.LugarNacimiento), d.LugarNacimiento, "el lugar de nacimiento", ValidadorRegistro.MaxNombre, false);
        var padre = ValidadorRegistro.Texto(errores, nameof(d.Padre), d.Padre, "el nombre del padre", ValidadorRegistro.MaxNombre, false);
        var madre = ValidadorRegistro.Texto(errores, nameof(d.Madre), d.Madre, "el nombre de la madre", ValidadorRegistro.MaxNombre, false);
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
        FechaParcial? fechaBautismo = null, fechaNacimiento = null;

        if (!FechaParcial.TryParsear(d.FechaBautismo, hoy, out fechaBautismo, out var errorBautismo))
            errores.Add(new ErrorCampo(nameof(d.FechaBautismo), errorBautismo));
        else if (fechaBautismo.Anio is null)
            errores.Add(new ErrorCampo(nameof(d.FechaBautismo), "Indique al menos el año del bautismo."));

        // El nacimiento es opcional (vacío es válido).
        if (!FechaParcial.TryParsear(d.FechaNacimiento, hoy, out fechaNacimiento, out var errorNacimiento))
            errores.Add(new ErrorCampo(nameof(d.FechaNacimiento), errorNacimiento));
        else if (fechaBautismo is not null && NacioDespues(fechaNacimiento, fechaBautismo))
            errores.Add(new ErrorCampo(nameof(d.FechaNacimiento), "La fecha de nacimiento es posterior a la del bautismo."));

        if (errores.Count > 0) return new ResultadoGuardado(EstadoGuardado.Invalido, Errores: errores);

        Bautismo registro;
        if (id is null)
        {
            registro = new Bautismo();
        }
        else
        {
            var existente = await db.Bautismos.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (existente is null) return new ResultadoGuardado(EstadoGuardado.NoExiste);
            if (existente.Anulado) return new ResultadoGuardado(EstadoGuardado.Anulado, existente.Id);

            // Concurrencia optimista: el UPDATE solo prospera si la fila sigue en la versión que vio el usuario.
            db.Entry(existente).Property(b => b.Version).OriginalValue = version ?? existente.Version;
            registro = existente;
        }

        registro.Libro = libro!;
        registro.Folio = folio!;
        registro.Partida = partida!;
        registro.Nombres = nombres ?? "";
        registro.Apellidos = apellidos ?? "";
        registro.FechaBautismo = fechaBautismo!;
        registro.FechaNacimiento = fechaNacimiento!;
        registro.LugarNacimiento = lugar;
        registro.Padre = padre;
        registro.Madre = madre;
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

        if (id is null) db.Bautismos.Add(registro);

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
    /// ¿El nacimiento es posterior al bautismo? Se compara solo hasta donde ambas fechas tienen dato:
    /// año, y si ambas traen mes, el mes, y si ambas traen día, el día. Lo incomparable no se rechaza.
    /// </summary>
    private static bool NacioDespues(FechaParcial nacimiento, FechaParcial bautismo)
    {
        if (nacimiento.Anio is null || bautismo.Anio is null) return false;
        if (nacimiento.Anio != bautismo.Anio) return nacimiento.Anio > bautismo.Anio;
        if (nacimiento.Mes is null || bautismo.Mes is null) return false;
        if (nacimiento.Mes != bautismo.Mes) return nacimiento.Mes > bautismo.Mes;
        if (nacimiento.Dia is null || bautismo.Dia is null) return false;
        return nacimiento.Dia > bautismo.Dia;
    }

    /// <summary>
    /// Advertencia (no bloqueo): mismo libro+folio+partida, o misma persona con la misma fecha de bautismo.
    /// Pueden existir varias partidas por folio y libros reiniciados, por eso solo se avisa.
    /// </summary>
    private async Task<IReadOnlyList<RegistroDuplicado>> BuscarDuplicadosAsync(Bautismo r, long idActual, CancellationToken ct)
    {
        var (lo, fo, po, nombre, clave) = (r.LibroOrden, r.FolioOrden, r.PartidaOrden, r.BusquedaNormalizada, r.FechaBautismo.ClaveOrden);

        var encontrados = await db.Bautismos.AsNoTracking()
            .Where(b => b.Id != idActual
                && ((b.LibroOrden == lo && b.FolioOrden == fo && b.PartidaOrden == po)
                    || (nombre != "" && b.BusquedaNormalizada == nombre
                        && EF.Property<int>(b, ConfiguracionBautismo.ClaveFecha) == clave)))
            .OrderBy(b => b.Id)
            .Take(MaxDuplicadosMostrados)
            .ToListAsync(ct);

        return [.. encontrados.Select(b => new RegistroDuplicado(
            b.Id, $"{b.Nombres} {b.Apellidos}".Trim(), b.Libro, b.Folio, b.Partida, b.FechaBautismo.ATexto()))];
    }

    public async Task<VistaBautismo?> ObtenerVistaAsync(long id, bool incluirAnulados = false, CancellationToken ct = default)
    {
        var q = db.Bautismos.AsNoTracking().Include(b => b.Ministro).AsQueryable();
        if (incluirAnulados) q = q.IgnoreQueryFilters();

        var b = await q.FirstOrDefaultAsync(x => x.Id == id, ct);
        return b is null ? null : new VistaBautismo(
            b.Id, b.Nombres, b.Apellidos,
            b.FechaBautismo.ATexto(), b.FechaBautismo.ATextoCaptura(),
            b.FechaNacimiento.ATexto(), b.FechaNacimiento.ATextoCaptura(),
            b.LugarNacimiento, b.Padre, b.Madre, [.. b.Padrinos],
            b.Libro, b.Folio, b.Partida, b.NombreMinistro, b.NotasMarginales,
            b.Revisar, b.MotivoRevision, b.Anulado, b.MotivoAnulacion, b.Version,
            b.CreadoPor, b.CreadoEn, b.ModificadoPor, b.ModificadoEn);
    }

    /// <summary>Datos que imprime la constancia; null si el registro no existe o está anulado.</summary>
    public async Task<DatosBautismo?> DatosParaConstanciaAsync(long id, CancellationToken ct = default)
    {
        var b = await db.Bautismos.AsNoTracking().Include(x => x.Ministro).FirstOrDefaultAsync(x => x.Id == id, ct);
        return b is null ? null : new DatosBautismo(
            b.Nombres, b.Apellidos, b.FechaBautismo, b.FechaNacimiento, b.Padre, b.Madre, [.. b.Padrinos],
            b.NombreMinistro, b.NotasMarginales, b.Libro, b.Folio, b.Revisar);
    }

    public async Task<ResultadoOperacion> AnularAsync(long id, string? motivo, CancellationToken ct = default)
    {
        var errores = new List<ErrorCampo>();
        var limpio = ValidadorRegistro.Texto(errores, "Motivo", motivo, "el motivo de la anulación", ValidadorRegistro.MaxMotivo, true);
        if (limpio is null) return ResultadoOperacion.Falla(errores[0].Mensaje);

        var b = await db.Bautismos.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return ResultadoOperacion.Falla("El registro ya no existe.");
        if (b.Anulado) return ResultadoOperacion.Falla("El registro ya está anulado.");

        b.Anulado = true;
        b.MotivoAnulacion = limpio;
        return await GuardarCambioAsync(ct);
    }

    /// <summary>Solo el Administrador (lo exige la página con <c>Registros.RevertirAnulacion</c>).</summary>
    public async Task<ResultadoOperacion> RevertirAnulacionAsync(long id, CancellationToken ct = default)
    {
        var b = await db.Bautismos.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return ResultadoOperacion.Falla("El registro ya no existe.");
        if (!b.Anulado) return ResultadoOperacion.Falla("El registro no está anulado.");

        b.Anulado = false;
        b.MotivoAnulacion = null;
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
    public async Task<PaginaResultado<FilaBautismo>> BuscarAsync(
        FiltroBautismo f, int pagina, int tamano = Paginacion.TamanoPorDefecto, CancellationToken ct = default)
    {
        var q = db.Bautismos.AsNoTracking().AsQueryable();
        if (f.IncluirAnulados) q = q.IgnoreQueryFilters();

        foreach (var token in NormalizadorBusqueda.Normalizar(f.Texto).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var patron = $"%{token}%"; // los tokens normalizados solo tienen letras y dígitos: no hay comodines que escapar
            q = q.Where(b => EF.Functions.Like(b.BusquedaNormalizada, patron));
        }

        if (!string.IsNullOrWhiteSpace(f.Libro))
        {
            var clave = ClaveOrdenNatural.De(f.Libro);
            q = q.Where(b => b.LibroOrden == clave);
        }

        if (!string.IsNullOrWhiteSpace(f.Folio))
        {
            var clave = ClaveOrdenNatural.De(f.Folio);
            q = q.Where(b => b.FolioOrden == clave);
        }

        if (f.AnioDesde is { } desde)
        {
            var minimo = desde * 10000;
            q = q.Where(b => EF.Property<int>(b, ConfiguracionBautismo.ClaveFecha) >= minimo);
        }

        if (f.AnioHasta is { } hasta)
        {
            var maximo = hasta * 10000 + 9999;
            q = q.Where(b => EF.Property<int>(b, ConfiguracionBautismo.ClaveFecha) <= maximo);
        }

        if (f.SoloRevisar) q = q.Where(b => b.Revisar);

        var pag = await q
            .OrderBy(b => b.Apellidos).ThenBy(b => b.Nombres)
            .ThenBy(b => b.LibroOrden).ThenBy(b => b.FolioOrden).ThenBy(b => b.PartidaOrden).ThenBy(b => b.Id)
            .PaginarAsync(pagina, tamano, ct);

        return new PaginaResultado<FilaBautismo>(
            [.. pag.Items.Select(b => new FilaBautismo(
                b.Id, b.Nombres, b.Apellidos, b.FechaBautismo.ATexto(), b.Libro, b.Folio, b.Partida, b.Revisar, b.Anulado))],
            pag.Total, pag.PaginaActual, pag.TamanoPagina);
    }
}
