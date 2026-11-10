using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Numeracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Un contrayente tal como lo digita el capturista. La edad va como texto y se valida al guardar.</summary>
public sealed record DatosContrayenteFormulario(
    string? Tratamiento, string? Nombres, string? Apellidos, string? Edad,
    string? OriginarioDe, string? FeligresDe, string? Padre, string? Madre);

/// <summary>Lo que digita el capturista. La fecha va como texto ("21/4/2007", "4/2007", "2007") y se interpreta al guardar.</summary>
public sealed record DatosMatrimonioFormulario(
    string? Libro, string? Folio, string? Partida,
    DatosContrayenteFormulario Esposo, DatosContrayenteFormulario Esposa,
    string? FechaMatrimonio, string? LugarCelebracion, IReadOnlyList<string?>? Testigos,
    string? Ministro, string? NotasMarginales,
    bool Revisar, string? MotivoRevision, bool AsientoNuevo = false);

public sealed record VistaContrayente(
    string? Tratamiento, string Nombres, string Apellidos, short? Edad,
    string? OriginarioDe, string? FeligresDe, string? Padre, string? Madre);

/// <summary>Registro listo para mostrar o editar (sin exponer la entidad a la capa web).</summary>
public sealed record VistaMatrimonio(
    long Id, VistaContrayente Esposo, VistaContrayente Esposa,
    string FechaMatrimonio, string FechaMatrimonioCaptura, string LugarCelebracion, IReadOnlyList<string> Testigos,
    string Libro, string Folio, string Partida, string? Ministro, string? NotasMarginales,
    bool Revisar, string? MotivoRevision, bool Anulado, string? MotivoAnulacion, uint Version,
    string? CreadoPor, DateTimeOffset CreadoEn, string? ModificadoPor, DateTimeOffset? ModificadoEn,
    bool AsientoDelSistema = false);

public sealed record FiltroMatrimonio(
    string? Texto = null, string? Libro = null, string? Folio = null,
    short? AnioDesde = null, short? AnioHasta = null,
    bool SoloRevisar = false, bool IncluirAnulados = false);

/// <summary>Un registro del listado. <c>CoincideEsposo/Esposa</c> dicen por quién apareció en una búsqueda por texto.</summary>
public sealed record FilaMatrimonio(
    long Id, string EsposoNombres, string EsposoApellidos, string EsposaNombres, string EsposaApellidos, string Fecha,
    string Libro, string Folio, string Partida, bool Revisar, bool Anulado, bool CoincideEsposo, bool CoincideEsposa);

/// <summary>Captura, edición, anulación y búsqueda de Matrimonios. Calcado de <see cref="ServicioBautismo"/>.</summary>
public sealed class ServicioMatrimonio(ParroquiaDbContext db, TimeProvider reloj)
{
    public const int MaxDuplicadosMostrados = 5;
    public const int MaxTestigos = 10;
    public const short EdadMinima = 12;
    public const short EdadMaxima = 120;

    /// <summary>Nombre con el que figura en Auditoria y BitacoraAcceso.</summary>
    public const string NombreEntidad = nameof(Matrimonio);

    /// <summary>Clave del módulo (<see cref="ModuloMatrimonio"/>); también identifica su libro digital.</summary>
    public const string Clave = ModuloMatrimonio.ClaveModulo;

    private readonly ServicioNumeracion numeracion = new(db, reloj);

    public async Task<ResultadoGuardado> GuardarAsync(
        DatosMatrimonioFormulario d, long? id = null, uint? version = null,
        bool confirmarDuplicado = false, CancellationToken ct = default)
    {
        var errores = new List<ErrorCampo>();
        // Asiento nuevo: libro/folio/partida los pone el sistema al guardar, no se validan los digitados.
        var asientoNuevo = d.AsientoNuevo && id is null;
        var libro = asientoNuevo ? "" : ValidadorRegistro.Texto(errores, nameof(d.Libro), d.Libro, "el libro", ValidadorRegistro.MaxClave, true);
        var folio = asientoNuevo ? "" : ValidadorRegistro.Texto(errores, nameof(d.Folio), d.Folio, "el folio", ValidadorRegistro.MaxClave, true);
        var partida = asientoNuevo ? "" : ValidadorRegistro.Texto(errores, nameof(d.Partida), d.Partida, "la partida", ValidadorRegistro.MaxClave, true);
        var lugar = ValidadorRegistro.Texto(errores, nameof(d.LugarCelebracion), d.LugarCelebracion, "el lugar de celebración", ValidadorRegistro.MaxNombre, false);
        var ministroTexto = ValidadorRegistro.Texto(errores, nameof(d.Ministro), d.Ministro, "el ministro", ValidadorRegistro.MaxNombre, false);
        var motivo = ValidadorRegistro.Texto(errores, nameof(d.MotivoRevision), d.MotivoRevision, "el motivo", ValidadorRegistro.MaxMotivo, false);
        var notas = ValidadorRegistro.Notas(errores, nameof(d.NotasMarginales), d.NotasMarginales);
        var testigos = LimpiarTestigos(errores, d.Testigos);

        var esposo = ValidarContrayente(errores, "Esposo", "del esposo", d.Esposo, d.Revisar, motivo);
        var esposa = ValidarContrayente(errores, "Esposa", "de la esposa", d.Esposa, d.Revisar, motivo);

        var hoy = HoraGuatemala.Hoy(reloj);
        if (!FechaParcial.TryParsear(d.FechaMatrimonio, hoy, out var fechaMatrimonio, out var errorFecha))
            errores.Add(new ErrorCampo(nameof(d.FechaMatrimonio), errorFecha));
        else if (fechaMatrimonio.Anio is null)
            errores.Add(new ErrorCampo(nameof(d.FechaMatrimonio), "Indique al menos el año del matrimonio."));

        if (errores.Count > 0) return new ResultadoGuardado(EstadoGuardado.Invalido, Errores: errores);

        Matrimonio registro;
        if (id is null)
        {
            registro = new Matrimonio();
        }
        else
        {
            var existente = await db.Matrimonios.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id, ct);
            if (existente is null) return new ResultadoGuardado(EstadoGuardado.NoExiste);
            if (existente.Anulado) return new ResultadoGuardado(EstadoGuardado.Anulado, existente.Id);

            // Concurrencia optimista: el UPDATE solo prospera si la fila sigue en la versión que vio el usuario.
            // Un asiento del sistema conserva su numeración: corregir = anular y asentar de nuevo (el número queda usado).
            if (existente.EsAsientoDelSistema
                && (libro != existente.Libro || folio != existente.Folio || partida != existente.Partida))
                return new ResultadoGuardado(EstadoGuardado.Invalido, Errores:
                    [new ErrorCampo(nameof(d.Libro), "El libro, folio y partida de un asiento del sistema no se pueden cambiar.")]);

            db.Entry(existente).Property(m => m.Version).OriginalValue = version ?? existente.Version;
            registro = existente;
        }

        if (!asientoNuevo && !registro.EsAsientoDelSistema && await numeracion.EsLibroDigitalAsync(Clave, libro!, ct))
            return new ResultadoGuardado(EstadoGuardado.Invalido, Errores:
                [new ErrorCampo(nameof(d.Libro), $"El libro {libro} lo numera el sistema (libro digital): no se puede transcribir en él.")]);

        if (!asientoNuevo)
        {
            registro.Libro = libro!;
            registro.Folio = folio!;
            registro.Partida = partida!;
        }
        registro.Esposo = esposo;
        registro.Esposa = esposa;
        registro.FechaMatrimonio = fechaMatrimonio!;
        registro.LugarCelebracion = lugar ?? Matrimonio.LugarPorDefecto;
        registro.Testigos = testigos; // lista nueva siempre: así se detecta y audita el cambio
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

        try
        {
            if (asientoNuevo)
            {
                // Numerar y guardar en la misma transacción: el libro queda bloqueado hasta el commit.
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                if (await numeracion.AsignarAsync(Clave, registro, ct) is null)
                    return new ResultadoGuardado(EstadoGuardado.Invalido, Errores:
                        [new ErrorCampo(nameof(d.AsientoNuevo), ServicioNumeracion.SinLibroAbierto)]);

                registro.ActualizarDerivados();
                db.Matrimonios.Add(registro);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            else
            {
                if (id is null) db.Matrimonios.Add(registro);
                await db.SaveChangesAsync(ct);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ResultadoGuardado(EstadoGuardado.Conflicto, registro.Id);
        }

        return new ResultadoGuardado(EstadoGuardado.Ok, registro.Id);
    }

    /// <summary>
    /// Valida un contrayente y arma el complex type. Nombre y apellidos obligatorios; solo un registro "Revisar" con motivo
    /// admite dejarlos vacíos (docs/02 §6). Los campos de error llevan el prefijo "Esposo." / "Esposa.".
    /// </summary>
    private static Contrayente ValidarContrayente(
        List<ErrorCampo> errores, string prefijo, string quien, DatosContrayenteFormulario c, bool revisar, string? motivo)
    {
        string Campo(string nombre) => $"{prefijo}.{nombre}";

        var tratamiento = ValidadorRegistro.Texto(errores, Campo(nameof(c.Tratamiento)), c.Tratamiento, $"el tratamiento {quien}", ValidadorRegistro.MaxNombre, false);
        var nombres = ValidadorRegistro.Texto(errores, Campo(nameof(c.Nombres)), c.Nombres, $"los nombres {quien}", ValidadorRegistro.MaxNombre, false);
        var apellidos = ValidadorRegistro.Texto(errores, Campo(nameof(c.Apellidos)), c.Apellidos, $"los apellidos {quien}", ValidadorRegistro.MaxNombre, false);
        var origen = ValidadorRegistro.Texto(errores, Campo(nameof(c.OriginarioDe)), c.OriginarioDe, $"el lugar de origen {quien}", ValidadorRegistro.MaxNombre, false);
        var feligres = ValidadorRegistro.Texto(errores, Campo(nameof(c.FeligresDe)), c.FeligresDe, $"la feligresía {quien}", ValidadorRegistro.MaxNombre, false);
        var padre = ValidadorRegistro.Texto(errores, Campo(nameof(c.Padre)), c.Padre, $"el padre {quien}", ValidadorRegistro.MaxNombre, false);
        var madre = ValidadorRegistro.Texto(errores, Campo(nameof(c.Madre)), c.Madre, $"la madre {quien}", ValidadorRegistro.MaxNombre, false);

        if (nombres is null || apellidos is null)
        {
            if (!revisar)
            {
                if (nombres is null && !errores.Any(e => e.Campo == Campo(nameof(c.Nombres))))
                    errores.Add(new ErrorCampo(Campo(nameof(c.Nombres)), $"Escriba los nombres {quien}."));
                if (apellidos is null && !errores.Any(e => e.Campo == Campo(nameof(c.Apellidos))))
                    errores.Add(new ErrorCampo(Campo(nameof(c.Apellidos)), $"Escriba los apellidos {quien}."));
            }
            else if (motivo is null && !errores.Any(e => e.Campo == nameof(DatosMatrimonioFormulario.MotivoRevision)))
            {
                errores.Add(new ErrorCampo(nameof(DatosMatrimonioFormulario.MotivoRevision),
                    "Falta un nombre: indique en el motivo de revisión por qué no se pudo leer."));
            }
        }

        short? edad = null;
        if (TextoLibro.Limpiar(c.Edad) is { } textoEdad)
        {
            if (short.TryParse(textoEdad, NumberStyles.None, CultureInfo.InvariantCulture, out var e)
                && e is >= EdadMinima and <= EdadMaxima)
                edad = e;
            else
                errores.Add(new ErrorCampo(Campo(nameof(c.Edad)),
                    $"La edad {quien} debe ser un número entre {EdadMinima} y {EdadMaxima}."));
        }

        return new Contrayente
        {
            Tratamiento = tratamiento, Nombres = nombres ?? "", Apellidos = apellidos ?? "", Edad = edad,
            OriginarioDe = origen, FeligresDe = feligres, Padre = padre, Madre = madre,
        };
    }

    /// <summary>Limpia espacios, descarta vacíos y valida cantidad y largo. Conserva el orden del libro.</summary>
    private static List<string> LimpiarTestigos(List<ErrorCampo> errores, IReadOnlyList<string?>? entrada)
    {
        var lista = new List<string>();
        foreach (var texto in entrada ?? [])
        {
            if (TextoLibro.Limpiar(texto) is not { } t) continue;
            if (t.Length > ValidadorRegistro.MaxNombre)
            {
                errores.Add(new ErrorCampo("Testigos", $"Cada testigo admite máximo {ValidadorRegistro.MaxNombre} caracteres."));
                return [];
            }
            lista.Add(t);
        }

        if (lista.Count > MaxTestigos)
        {
            errores.Add(new ErrorCampo("Testigos", $"Se admiten máximo {MaxTestigos} testigos."));
            return [];
        }
        return lista;
    }

    /// <summary>
    /// Advertencia (no bloqueo): mismo libro+folio+partida, o los mismos contrayentes con la misma fecha de matrimonio.
    /// Pueden existir varias partidas por folio y libros reiniciados, por eso solo se avisa.
    /// </summary>
    private async Task<IReadOnlyList<RegistroDuplicado>> BuscarDuplicadosAsync(Matrimonio r, long idActual, CancellationToken ct)
    {
        var (lo, fo, po, esposo, esposa, clave) =
            (r.LibroOrden, r.FolioOrden, r.PartidaOrden, r.BusquedaEsposo, r.BusquedaEsposa, r.FechaMatrimonio.ClaveOrden);

        var encontrados = await db.Matrimonios.AsNoTracking()
            .Where(m => m.Id != idActual
                && ((m.LibroOrden == lo && m.FolioOrden == fo && m.PartidaOrden == po)
                    || (esposo != "" && esposa != "" && m.BusquedaEsposo == esposo && m.BusquedaEsposa == esposa
                        && EF.Property<int>(m, ConfiguracionMatrimonio.ClaveFecha) == clave)))
            .OrderBy(m => m.Id)
            .Take(MaxDuplicadosMostrados)
            .ToListAsync(ct);

        return [.. encontrados.Select(m => new RegistroDuplicado(
            m.Id, $"{NombreCompleto(m.Esposo)} y {NombreCompleto(m.Esposa)}".Trim(),
            m.Libro, m.Folio, m.Partida, m.FechaMatrimonio.ATexto()))];
    }

    public async Task<VistaMatrimonio?> ObtenerVistaAsync(long id, bool incluirAnulados = false, CancellationToken ct = default)
    {
        var q = db.Matrimonios.AsNoTracking().Include(m => m.Ministro).AsQueryable();
        if (incluirAnulados) q = q.IgnoreQueryFilters();

        var m = await q.FirstOrDefaultAsync(x => x.Id == id, ct);
        return m is null ? null : new VistaMatrimonio(
            m.Id, VistaDe(m.Esposo), VistaDe(m.Esposa),
            m.FechaMatrimonio.ATexto(), m.FechaMatrimonio.ATextoCaptura(), m.LugarCelebracion, [.. m.Testigos],
            m.Libro, m.Folio, m.Partida, m.NombreMinistro, m.NotasMarginales,
            m.Revisar, m.MotivoRevision, m.Anulado, m.MotivoAnulacion, m.Version,
            m.CreadoPor, m.CreadoEn, m.ModificadoPor, m.ModificadoEn, m.EsAsientoDelSistema);
    }

    /// <summary>Datos que imprime la constancia; null si el registro no existe o está anulado.</summary>
    public async Task<DatosMatrimonio?> DatosParaConstanciaAsync(long id, CancellationToken ct = default)
    {
        var m = await db.Matrimonios.AsNoTracking().Include(x => x.Ministro).FirstOrDefaultAsync(x => x.Id == id, ct);
        return m is null ? null : new DatosMatrimonio(
            DatosDe(m.Esposo), DatosDe(m.Esposa), m.FechaMatrimonio, m.LugarCelebracion, [.. m.Testigos],
            m.NombreMinistro, m.NotasMarginales, m.Libro, m.Folio, m.Partida, m.Revisar);
    }

    public async Task<ResultadoOperacion> AnularAsync(long id, string? motivo, CancellationToken ct = default)
    {
        var errores = new List<ErrorCampo>();
        var limpio = ValidadorRegistro.Texto(errores, "Motivo", motivo, "el motivo de la anulación", ValidadorRegistro.MaxMotivo, true);
        if (limpio is null) return ResultadoOperacion.Falla(errores[0].Mensaje);

        var m = await db.Matrimonios.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return ResultadoOperacion.Falla("El registro ya no existe.");
        if (m.Anulado) return ResultadoOperacion.Falla("El registro ya está anulado.");

        m.Anulado = true;
        m.MotivoAnulacion = limpio;
        return await GuardarCambioAsync(ct);
    }

    /// <summary>Solo el Administrador (lo exige la página con <c>Registros.RevertirAnulacion</c>).</summary>
    public async Task<ResultadoOperacion> RevertirAnulacionAsync(long id, CancellationToken ct = default)
    {
        var m = await db.Matrimonios.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return ResultadoOperacion.Falla("El registro ya no existe.");
        if (!m.Anulado) return ResultadoOperacion.Falla("El registro no está anulado.");

        m.Anulado = false;
        m.MotivoAnulacion = null;
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

    /// <summary>
    /// Búsqueda parcial sin tildes ni mayúsculas. Con texto, el registro aparece si TODAS las palabras están en el esposo
    /// o TODAS en la esposa (no se mezclan). Orden: apellidos y nombres del esposo y luego libro/folio/partida.
    /// </summary>
    public async Task<PaginaResultado<FilaMatrimonio>> BuscarAsync(
        FiltroMatrimonio f, int pagina, int tamano = Paginacion.TamanoPorDefecto, CancellationToken ct = default)
    {
        var tokens = Tokens(f.Texto);
        var q = Filtrar(f, tokens);

        var pag = await q
            .OrderBy(m => m.Esposo.Apellidos).ThenBy(m => m.Esposo.Nombres)
            .ThenBy(m => m.LibroOrden).ThenBy(m => m.FolioOrden).ThenBy(m => m.PartidaOrden).ThenBy(m => m.Id)
            .PaginarAsync(pagina, tamano, ct);

        return new PaginaResultado<FilaMatrimonio>(
            [.. pag.Items.Select(m => Fila(m, tokens))], pag.Total, pag.PaginaActual, pag.TamanoPagina);
    }

    /// <summary>
    /// Para la búsqueda global: una fila por contrayente que coincide (un registro puede aportar dos). El total cuenta
    /// filas, así que "hay más" es verdadero siempre que queden registros sin mostrar.
    /// </summary>
    public async Task<(IReadOnlyList<FilaMatrimonio> Filas, int Total)> BuscarPersonasAsync(
        FiltroMatrimonio f, int maximo, CancellationToken ct = default)
    {
        var tokens = Tokens(f.Texto);
        var q = Filtrar(f, tokens);

        var registros = await q
            .OrderBy(m => m.Esposo.Apellidos).ThenBy(m => m.Esposo.Nombres)
            .ThenBy(m => m.LibroOrden).ThenBy(m => m.FolioOrden).ThenBy(m => m.PartidaOrden).ThenBy(m => m.Id)
            .Take(maximo)
            .ToListAsync(ct);

        var total = tokens.Length == 0
            ? await q.CountAsync(ct)
            : await q.Where(Coincide(esposo: true, tokens)).CountAsync(ct)
              + await q.Where(Coincide(esposo: false, tokens)).CountAsync(ct);

        return ([.. registros.Select(m => Fila(m, tokens))], total);
    }

    /// <summary>Filas de los ids pedidos (anulados excluidos por el filtro global); ambos contrayentes marcados como coincidentes.</summary>
    public async Task<IReadOnlyList<FilaMatrimonio>> ResumenesAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        var lista = ids.ToArray();
        var registros = await db.Matrimonios.AsNoTracking().Where(m => lista.Contains(m.Id)).ToListAsync(ct);
        return [.. registros.Select(m => Fila(m, []) with { CoincideEsposo = true, CoincideEsposa = true })];
    }

    private IQueryable<Matrimonio> Filtrar(FiltroMatrimonio f, string[] tokens)
    {
        var q = db.Matrimonios.AsNoTracking().AsQueryable();
        if (f.IncluirAnulados) q = q.IgnoreQueryFilters();

        if (tokens.Length > 0)
            q = q.Where(CoincideAlguno(tokens));

        if (!string.IsNullOrWhiteSpace(f.Libro))
        {
            var clave = ClaveOrdenNatural.De(f.Libro);
            q = q.Where(m => m.LibroOrden == clave);
        }

        if (!string.IsNullOrWhiteSpace(f.Folio))
        {
            var clave = ClaveOrdenNatural.De(f.Folio);
            q = q.Where(m => m.FolioOrden == clave);
        }

        if (f.AnioDesde is { } desde)
        {
            var minimo = desde * 10000;
            q = q.Where(m => EF.Property<int>(m, ConfiguracionMatrimonio.ClaveFecha) >= minimo);
        }

        if (f.AnioHasta is { } hasta)
        {
            var maximo = hasta * 10000 + 9999;
            q = q.Where(m => EF.Property<int>(m, ConfiguracionMatrimonio.ClaveFecha) <= maximo);
        }

        if (f.SoloRevisar) q = q.Where(m => m.Revisar);
        return q;
    }

    private static string[] Tokens(string? texto) =>
        NormalizadorBusqueda.Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Todas las palabras dentro de la búsqueda del esposo (o de la esposa). Los patrones viajan como parámetros.</summary>
    private static Expression<Func<Matrimonio, bool>> Coincide(bool esposo, string[] tokens)
    {
        var m = Expression.Parameter(typeof(Matrimonio), "m");
        return Expression.Lambda<Func<Matrimonio, bool>>(Cuerpo(m, esposo, tokens), m);
    }

    /// <summary>Todas las palabras en el esposo O todas en la esposa.</summary>
    private static Expression<Func<Matrimonio, bool>> CoincideAlguno(string[] tokens)
    {
        var m = Expression.Parameter(typeof(Matrimonio), "m");
        return Expression.Lambda<Func<Matrimonio, bool>>(
            Expression.OrElse(Cuerpo(m, true, tokens), Cuerpo(m, false, tokens)), m);
    }

    private static Expression Cuerpo(ParameterExpression m, bool esposo, string[] tokens)
    {
        var campo = Expression.Property(m, esposo ? nameof(Matrimonio.BusquedaEsposo) : nameof(Matrimonio.BusquedaEsposa));
        var like = typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!;

        Expression? cuerpo = null;
        foreach (var token in tokens)
        {
            // Los tokens normalizados solo tienen letras y dígitos: no hay comodines que escapar.
            var patron = Expression.Property(Expression.Constant(new Patron($"%{token}%")), nameof(Patron.Valor));
            Expression llamada = Expression.Call(like, Expression.Constant(EF.Functions), campo, patron);
            cuerpo = cuerpo is null ? llamada : Expression.AndAlso(cuerpo, llamada);
        }
        return cuerpo ?? Expression.Constant(true);
    }

    private sealed record Patron(string Valor);

    private static FilaMatrimonio Fila(Matrimonio m, string[] tokens) => new(
        m.Id, m.Esposo.Nombres, m.Esposo.Apellidos, m.Esposa.Nombres, m.Esposa.Apellidos, m.FechaMatrimonio.ATexto(),
        m.Libro, m.Folio, m.Partida, m.Revisar, m.Anulado,
        tokens.Length > 0 && tokens.All(t => m.BusquedaEsposo.Contains(t, StringComparison.Ordinal)),
        tokens.Length > 0 && tokens.All(t => m.BusquedaEsposa.Contains(t, StringComparison.Ordinal)));

    private static string NombreCompleto(Contrayente c) => $"{c.Nombres} {c.Apellidos}".Trim();

    private static VistaContrayente VistaDe(Contrayente c) =>
        new(c.Tratamiento, c.Nombres, c.Apellidos, c.Edad, c.OriginarioDe, c.FeligresDe, c.Padre, c.Madre);

    private static DatosContrayente DatosDe(Contrayente c) =>
        new(c.Tratamiento, c.Nombres, c.Apellidos, c.Edad, c.OriginarioDe, c.FeligresDe, c.Padre, c.Madre);
}
