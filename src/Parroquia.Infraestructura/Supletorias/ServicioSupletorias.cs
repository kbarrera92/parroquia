using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Dominio.Supletorias;
using Parroquia.Infraestructura.Numeracion;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Infraestructura.Supletorias;

/// <summary>
/// Lo que digita el capturista de la partida supletoria. <paramref name="NumeraSistema"/>: el sistema asigna libro/folio/partida
/// del Libro de Supletorias (se ignoran los digitados); si no, se transcriben de un libro supletorio de papel.
/// </summary>
public sealed record DatosSupletoriaFormulario(
    bool NumeraSistema, string? Libro, string? Folio, string? Partida,
    string? Fecha, string? Testigo, string? ParentescoTestigo);

/// <summary>Datos ya validados y limpios; los produce <see cref="ServicioSupletorias.Validar"/>.</summary>
public sealed record SupletoriaValidada(
    bool NumeraSistema, string? Libro, string? Folio, string? Partida,
    FechaParcial Fecha, string Testigo, string? ParentescoTestigo);

public sealed record VistaSupletoria(
    long Id, string Libro, string Folio, string Partida, string Fecha, string FechaCaptura,
    string Testigo, string? ParentescoTestigo, bool AsientoDelSistema);

public sealed record FiltroSupletorias(
    string? Sacramento = null, string? Libro = null, string? Folio = null,
    short? AnioDesde = null, short? AnioHasta = null, bool IncluirAnulados = false);

public sealed record FilaSupletoria(
    long Id, string Clave, long RegistroId, string Libro, string Folio, string Partida,
    string Fecha, string Testigo, string? ParentescoTestigo, bool Anulado);

public sealed record ResultadoSupletoria(bool Ok, ErrorCampo? Error = null)
{
    public static readonly ResultadoSupletoria Exito = new(true);
    public static ResultadoSupletoria Falla(string campo, string mensaje) => new(false, new ErrorCampo(campo, mensaje));
}

/// <summary>
/// Libro de Supletorias: uno solo, compartido por los 4 sacramentos. Cada <c>ServicioX</c> lo crea con su mismo
/// <c>DbContext</c> (como <see cref="ServicioNumeracion"/>) para asentar la supletoria en la misma transacción que el registro.
/// </summary>
public sealed class ServicioSupletorias(ParroquiaDbContext db, TimeProvider reloj)
{
    public const string NombreEntidad = nameof(Supletoria);

    private readonly ServicioNumeracion numeracion = new(db, reloj);

    /// <summary>Valida los datos propios de la supletoria. Los campos se reportan como <c>Supletoria.X</c>.</summary>
    /// <param name="testigoOpcional">Registro marcado "Revisar" con motivo: admite el testigo vacío.</param>
    public SupletoriaValidada? Validar(
        DatosSupletoriaFormulario d, ICollection<ErrorCampo> errores, DateOnly hoy, bool testigoOpcional)
    {
        var antes = errores.Count;
        string? libro = null, folio = null, partida = null;
        if (!d.NumeraSistema)
        {
            libro = ValidadorRegistro.Texto(errores, Campo(nameof(d.Libro)), d.Libro, "el libro supletorio", ValidadorRegistro.MaxClave, true);
            folio = ValidadorRegistro.Texto(errores, Campo(nameof(d.Folio)), d.Folio, "el folio supletorio", ValidadorRegistro.MaxClave, true);
            partida = ValidadorRegistro.Texto(errores, Campo(nameof(d.Partida)), d.Partida, "la partida supletoria", ValidadorRegistro.MaxClave, true);
        }

        var testigo = ValidadorRegistro.Texto(errores, Campo(nameof(d.Testigo)), d.Testigo, "el nombre del testigo",
            ConfiguracionSupletoria.MaxTestigo, !testigoOpcional);
        var parentesco = ValidadorRegistro.Texto(errores, Campo(nameof(d.ParentescoTestigo)), d.ParentescoTestigo,
            "el parentesco", ConfiguracionSupletoria.MaxParentesco, false);

        if (!FechaParcial.TryParsear(d.Fecha, hoy, out var fecha, out var errorFecha))
            errores.Add(new ErrorCampo(Campo(nameof(d.Fecha)), errorFecha));
        else if (fecha.Anio is null)
            errores.Add(new ErrorCampo(Campo(nameof(d.Fecha)), "Indique al menos el año de la fecha del asiento."));

        return errores.Count > antes
            ? null
            : new SupletoriaValidada(d.NumeraSistema, libro, folio, partida, fecha!, testigo ?? "", parentesco);
    }

    private static string Campo(string nombre) => $"Supletoria.{nombre}";

    /// <summary>
    /// Asienta la partida supletoria del <paramref name="registro"/> (ya agregado al contexto, con su Id). <b>Exige una transacción
    /// abierta</b> y no guarda: lo hace quien llama junto con el registro. Numera con el sistema o verifica el número transcrito.
    /// Si <paramref name="sinOriginal"/>, el registro toma el número supletorio como su libro/folio/partida ("Supletorio N").
    /// </summary>
    public async Task<ResultadoSupletoria> AsentarAsync(
        string clave, RegistroSacramental registro, SupletoriaValidada v, bool sinOriginal, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La supletoria exige una transacción abierta junto con el guardado del registro.");

        var supletoria = new Supletoria
        {
            TipoSacramento = clave,
            RegistroId = registro.Id,
            FechaSupletoria = v.Fecha,
            Testigo = v.Testigo,
            ParentescoTestigo = v.ParentescoTestigo,
        };

        if (v.NumeraSistema)
        {
            var numero = await numeracion.AsignarNumeroAsync(Supletoria.ClaveLibroDigital, ct);
            if (numero is null)
                return ResultadoSupletoria.Falla("Supletoria.NumeraSistema",
                    "No hay un libro digital de supletorias abierto. Pida al Administrador que lo abra (Administración → Libros digitales).");
            (supletoria.Libro, supletoria.Folio, supletoria.Partida, supletoria.LibroDigitalId) =
                (numero.Libro, numero.Folio, numero.Partida, numero.LibroDigitalId);
        }
        else
        {
            (supletoria.Libro, supletoria.Folio, supletoria.Partida) = (v.Libro!, v.Folio!, v.Partida!);
            supletoria.ActualizarDerivados();

            if (await numeracion.EsLibroDigitalAsync(Supletoria.ClaveLibroDigital, supletoria.Libro, ct))
                return ResultadoSupletoria.Falla("Supletoria.Libro",
                    $"El libro supletorio {supletoria.Libro} lo numera el sistema (libro digital): no se puede transcribir en él.");

            var (lo, fo, po) = (supletoria.LibroOrden, supletoria.FolioOrden, supletoria.PartidaOrden);
            if (await db.Supletorias.IgnoreQueryFilters().AnyAsync(
                    s => s.LibroOrden == lo && s.FolioOrden == fo && s.PartidaOrden == po, ct))
                return ResultadoSupletoria.Falla("Supletoria.Partida",
                    $"Ya existe la partida supletoria {supletoria.Partida} del folio {supletoria.Folio} del libro {supletoria.Libro}.");
        }

        supletoria.ActualizarDerivados();

        if (sinOriginal)
        {
            registro.Libro = Supletoria.LibroEnRegistro(supletoria.Libro);
            registro.Folio = supletoria.Folio;
            registro.Partida = supletoria.Partida;
            registro.ActualizarDerivados();
        }

        db.Supletorias.Add(supletoria);
        return ResultadoSupletoria.Exito;
    }

    /// <summary>Edita la fecha y el testigo. El número supletorio no se cambia (corregir = anular y asentar de nuevo). No guarda.</summary>
    public async Task<ResultadoSupletoria> ActualizarAsync(
        string clave, long registroId, SupletoriaValidada v, CancellationToken ct = default)
    {
        var s = await db.Supletorias.FirstOrDefaultAsync(x => x.TipoSacramento == clave && x.RegistroId == registroId, ct);
        if (s is null)
            return ResultadoSupletoria.Falla("Supletoria", "Este registro no tiene una partida supletoria.");

        s.FechaSupletoria = v.Fecha;
        s.Testigo = v.Testigo;
        s.ParentescoTestigo = v.ParentescoTestigo;
        return ResultadoSupletoria.Exito;
    }

    /// <summary>
    /// Anular o revertir el registro hace lo mismo con su supletoria (el número supletorio nunca se reutiliza).
    /// No guarda: lo guarda quien llama, en el mismo <c>SaveChanges</c> que el registro.
    /// </summary>
    public async Task SincronizarAnulacionAsync(
        string clave, long registroId, bool anulado, string? motivo, CancellationToken ct = default)
    {
        var s = await db.Supletorias.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TipoSacramento == clave && x.RegistroId == registroId, ct);
        if (s is null || s.Anulado == anulado) return;

        s.Anulado = anulado;
        s.MotivoAnulacion = anulado ? motivo : null;
    }

    public async Task<VistaSupletoria?> ObtenerPorRegistroAsync(
        string clave, long registroId, bool incluirAnuladas = false, CancellationToken ct = default)
    {
        var q = db.Supletorias.AsNoTracking().AsQueryable();
        if (incluirAnuladas) q = q.IgnoreQueryFilters();

        var s = await q.FirstOrDefaultAsync(x => x.TipoSacramento == clave && x.RegistroId == registroId, ct);
        return s is null ? null : new VistaSupletoria(
            s.Id, s.Libro, s.Folio, s.Partida, s.FechaSupletoria.ATexto(), s.FechaSupletoria.ATextoCaptura(),
            s.Testigo, s.ParentescoTestigo, s.EsAsientoDelSistema);
    }

    /// <summary>Listado del Libro de Supletorias en orden natural de libro, folio y partida.</summary>
    public async Task<PaginaResultado<FilaSupletoria>> BuscarAsync(
        FiltroSupletorias f, int pagina, int tamano = Paginacion.TamanoPorDefecto, CancellationToken ct = default)
    {
        var q = db.Supletorias.AsNoTracking().AsQueryable();
        if (f.IncluirAnulados) q = q.IgnoreQueryFilters();
        if (!string.IsNullOrWhiteSpace(f.Sacramento)) q = q.Where(s => s.TipoSacramento == f.Sacramento);

        if (!string.IsNullOrWhiteSpace(f.Libro))
        {
            var clave = ClaveOrdenNatural.De(f.Libro);
            q = q.Where(s => s.LibroOrden == clave);
        }

        if (!string.IsNullOrWhiteSpace(f.Folio))
        {
            var clave = ClaveOrdenNatural.De(f.Folio);
            q = q.Where(s => s.FolioOrden == clave);
        }

        if (f.AnioDesde is { } desde)
        {
            var minimo = desde * 10000;
            q = q.Where(s => EF.Property<int>(s, ConfiguracionSupletoria.ClaveFecha) >= minimo);
        }

        if (f.AnioHasta is { } hasta)
        {
            var maximo = hasta * 10000 + 9999;
            q = q.Where(s => EF.Property<int>(s, ConfiguracionSupletoria.ClaveFecha) <= maximo);
        }

        var pag = await q
            .OrderBy(s => s.LibroOrden).ThenBy(s => s.FolioOrden).ThenBy(s => s.PartidaOrden).ThenBy(s => s.Id)
            .PaginarAsync(pagina, tamano, ct);

        return new PaginaResultado<FilaSupletoria>(
            [.. pag.Items.Select(s => new FilaSupletoria(
                s.Id, s.TipoSacramento, s.RegistroId, s.Libro, s.Folio, s.Partida,
                s.FechaSupletoria.ATexto(), s.Testigo, s.ParentescoTestigo, s.Anulado))],
            pag.Total, pag.PaginaActual, pag.TamanoPagina);
    }
}
