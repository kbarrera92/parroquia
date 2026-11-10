using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Numeracion;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Infraestructura.Numeracion;

/// <summary>Número entregado a un asiento nuevo.</summary>
public sealed record NumeroAsignado(long LibroDigitalId, string Libro, string Folio, string Partida);

/// <summary>Lo que se mostraría al próximo asiento (sin reservarlo).</summary>
public sealed record ProximoNumero(string Libro, string Folio, string Partida, int Restantes);

public sealed record VistaLibroDigital(
    long Id, string Numero, int FolioActual, int UltimaPartida, int Entregados, int Restantes,
    int PartidasPorFolio, int FoliosPorLibro, ModoPartida ModoPartida, DateTimeOffset AbiertoEn);

public sealed record DatosAperturaLibro(
    string Clave, string? Numero, int FolioInicial, int PartidaInicial,
    int PartidasPorFolio, int FoliosPorLibro, ModoPartida ModoPartida, int PartidasYaEnFolio = 0);

/// <summary>
/// Numera los asientos nuevos (los que nacen en el sistema). Compartido por todos los módulos sacramentales
/// sin acoplarlos: se identifica por la clave del módulo. Los asientos transcritos de libros físicos no pasan por aquí.
/// </summary>
public sealed class ServicioNumeracion(ParroquiaDbContext db, TimeProvider reloj)
{
    public const string SinLibroAbierto =
        "No hay un libro digital abierto para este sacramento. Pida al Administrador que abra uno (Administración → Libros digitales).";

    /// <summary>
    /// Entrega el siguiente libro/folio/partida y los escribe en el registro. <b>Debe llamarse dentro de la misma transacción
    /// que guarda el registro</b>: el libro queda bloqueado (FOR UPDATE) hasta el commit, así dos capturistas nunca reciben el mismo número.
    /// Devuelve null si no hay libro abierto. No guarda: lo hace quien llama junto con el registro.
    /// </summary>
    public async Task<NumeroAsignado?> AsignarAsync(string clave, RegistroSacramental registro, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("La numeración exige una transacción abierta junto con el guardado del registro.");

        // Si este contexto ya leyó el libro (asientos anteriores), EF devolvería la instancia rastreada con
        // contadores viejos en vez de los que otro capturista acaba de guardar. Se suelta para releerla bajo el bloqueo.
        foreach (var rastreado in db.ChangeTracker.Entries<LibroDigital>().Where(e => e.State == EntityState.Unchanged).ToList())
            rastreado.State = EntityState.Detached;

        var libro = await db.LibrosDigitales
            // xmin (token de concurrencia) es columna de sistema: "*" no la incluye.
            .FromSqlInterpolated($"SELECT *, xmin FROM libro_digital WHERE tipo_sacramento = {clave} AND abierto FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (libro is null) return null;

        var (folio, partida) = libro.Asignar();
        registro.Libro = libro.Numero;
        registro.Folio = folio.ToString();
        registro.Partida = partida.ToString();
        registro.LibroDigitalId = libro.Id;

        if (libro.EstaLleno) await CerrarYAbrirSiguienteAsync(libro, ct);

        return new NumeroAsignado(libro.Id, registro.Libro, registro.Folio, registro.Partida);
    }

    /// <summary>Al llenarse se cierra y, si el número es correlativo y está libre, abre el siguiente con la misma configuración.</summary>
    private async Task CerrarYAbrirSiguienteAsync(LibroDigital lleno, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        lleno.Cerrar(ahora);

        var numero = NumeroSiguiente.De(lleno.Numero);
        // Un número con letras o "bis" no se puede incrementar solo: queda cerrado y el Administrador abre el siguiente.
        if (numero == lleno.Numero) return;
        if (await db.LibrosDigitales.AnyAsync(l => l.TipoSacramento == lleno.TipoSacramento && l.Numero == numero, ct)) return;

        db.LibrosDigitales.Add(lleno.AbrirSiguiente(numero, ahora));
    }

    /// <summary>Libro abierto del sacramento (null si no hay), solo lectura.</summary>
    public async Task<VistaLibroDigital?> LibroAbiertoAsync(string clave, CancellationToken ct = default)
    {
        var l = await db.LibrosDigitales.AsNoTracking().FirstOrDefaultAsync(x => x.TipoSacramento == clave && x.Abierto, ct);
        return l is null ? null : new VistaLibroDigital(
            l.Id, l.Numero, l.FolioActual, l.UltimaPartida, l.Entregados, l.Restantes,
            l.PartidasPorFolio, l.FoliosPorLibro, l.ModoPartida, l.AbiertoEn);
    }

    /// <summary>Número que recibiría el próximo asiento. Orientativo: otro capturista puede tomarlo antes.</summary>
    public async Task<ProximoNumero?> ProximoAsync(string clave, CancellationToken ct = default)
    {
        var l = await db.LibrosDigitales.AsNoTracking().FirstOrDefaultAsync(x => x.TipoSacramento == clave && x.Abierto, ct);
        if (l?.Siguiente() is not var (folio, partida)) return null;
        return new ProximoNumero(l.Numero, folio.ToString(), partida.ToString(), l.Restantes);
    }

    /// <summary>true si <paramref name="libro"/> es el número de un libro digital del sacramento (abierto o cerrado).</summary>
    public Task<bool> EsLibroDigitalAsync(string clave, string libro, CancellationToken ct = default)
    {
        var numero = TextoLibro.Limpiar(libro);
        return numero is null
            ? Task.FromResult(false)
            : db.LibrosDigitales.AsNoTracking().AnyAsync(l => l.TipoSacramento == clave && l.Numero == numero, ct);
    }

    /// <summary>Abre el libro digital de un sacramento (solo Administrador; lo exige la página).</summary>
    public async Task<ResultadoOperacion> AbrirAsync(DatosAperturaLibro d, CancellationToken ct = default)
    {
        if (await db.LibrosDigitales.AnyAsync(l => l.TipoSacramento == d.Clave && l.Abierto, ct))
            return ResultadoOperacion.Falla("Ya hay un libro digital abierto para este sacramento. Ciérrelo antes de abrir otro.");

        var numero = TextoLibro.Limpiar(d.Numero);
        if (numero is null) return ResultadoOperacion.Falla("Escriba el número de libro.");
        if (numero.Length > ValidadorRegistro.MaxClave)
            return ResultadoOperacion.Falla($"El número de libro admite hasta {ValidadorRegistro.MaxClave} caracteres.");
        if (await db.LibrosDigitales.AnyAsync(l => l.TipoSacramento == d.Clave && l.Numero == numero, ct))
            return ResultadoOperacion.Falla($"El libro {numero} ya fue un libro digital de este sacramento.");

        // Un número de libro no puede ser a la vez de papel (transcrito) y digital: los folios se mezclarían.
        if (await HayRegistrosTranscritosAsync(d.Clave, numero, ct))
            return ResultadoOperacion.Falla($"Ya hay registros transcritos del libro {numero} de este sacramento. Use otro número de libro.");

        LibroDigital libro;
        try
        {
            libro = LibroDigital.Abrir(d.Clave, numero, d.FolioInicial, d.PartidaInicial,
                d.PartidasPorFolio, d.FoliosPorLibro, d.ModoPartida, reloj.GetUtcNow(), d.PartidasYaEnFolio);
        }
        catch (ArgumentException ex)
        {
            return ResultadoOperacion.Falla(ex.Message);
        }

        db.LibrosDigitales.Add(libro);
        try
        {
            await db.SaveChangesAsync(ct);
            return ResultadoOperacion.Exito;
        }
        catch (DbUpdateException)
        {
            // Dos administradores abriendo a la vez: el índice único parcial deja pasar solo a uno.
            return ResultadoOperacion.Falla("Otro usuario abrió un libro digital al mismo tiempo. Revise e intente de nuevo.");
        }
    }

    /// <summary>Registros (también anulados) transcritos de un libro físico con ese número. Sacramento desconocido: false.</summary>
    private Task<bool> HayRegistrosTranscritosAsync(string clave, string numero, CancellationToken ct)
    {
        var orden = ClaveOrdenNatural.De(numero);
        return clave switch
        {
            ModuloComunion.ClaveModulo => db.Comuniones.IgnoreQueryFilters().AnyAsync(r => r.LibroOrden == orden && r.LibroDigitalId == null, ct),
            ModuloBautismo.ClaveModulo => db.Bautismos.IgnoreQueryFilters().AnyAsync(r => r.LibroOrden == orden && r.LibroDigitalId == null, ct),
            ModuloConfirmacion.ClaveModulo => db.Confirmaciones.IgnoreQueryFilters().AnyAsync(r => r.LibroOrden == orden && r.LibroDigitalId == null, ct),
            ModuloMatrimonio.ClaveModulo => db.Matrimonios.IgnoreQueryFilters().AnyAsync(r => r.LibroOrden == orden && r.LibroDigitalId == null, ct),
            _ => Task.FromResult(false),
        };
    }

    /// <summary>Cierra el libro abierto. Los asientos ya numerados conservan su número.</summary>
    public async Task<ResultadoOperacion> CerrarAsync(string clave, CancellationToken ct = default)
    {
        var libro = await db.LibrosDigitales.FirstOrDefaultAsync(l => l.TipoSacramento == clave && l.Abierto, ct);
        if (libro is null) return ResultadoOperacion.Falla("No hay un libro digital abierto.");

        libro.Cerrar(reloj.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
            return ResultadoOperacion.Exito;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultadoOperacion.Falla("Otro usuario modificó el libro. Revise e intente de nuevo.");
        }
    }
}
