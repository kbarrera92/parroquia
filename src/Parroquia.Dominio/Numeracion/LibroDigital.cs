using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Numeracion;

/// <summary>
/// Libro que numera el sistema para los sacramentos nuevos (los que ya no se escriben en papel).
/// A lo sumo uno abierto por sacramento. Continúa la numeración de los libros físicos y entrega
/// folio y partida correlativos; cuando se llena se cierra y el servicio abre el siguiente.
/// La lógica de numeración es pura (sin base de datos) para probarla aislada.
/// </summary>
public class LibroDigital : EntidadAuditable
{
    /// <summary>Clave del módulo sacramental ("bautismo", "comunion"…).</summary>
    public string TipoSacramento { get; private set; } = "";

    /// <summary>Número del libro como texto, igual que <c>Libro</c> en los registros (ej. "81").</summary>
    public string Numero { get; private set; } = "";

    public int FolioInicial { get; private set; }
    public int PartidaInicial { get; private set; }
    public int PartidasPorFolio { get; private set; }
    public int FoliosPorLibro { get; private set; }

    /// <summary>Cuándo vuelve a 1 la partida: nunca, en cada libro nuevo o en cada folio.</summary>
    public ModoPartida ModoPartida { get; private set; }

    /// <summary>Folio del último asiento entregado (o <see cref="FolioInicial"/> si aún no hay ninguno).</summary>
    public int FolioActual { get; private set; }

    /// <summary>Cuántas partidas lleva ocupadas el <see cref="FolioActual"/>.</summary>
    public int PartidasEnFolio { get; private set; }

    /// <summary>Última partida entregada (<c>PartidaInicial - 1</c> si aún no hay ninguna).</summary>
    public int UltimaPartida { get; private set; }

    /// <summary>Asientos entregados por este libro (incluye los que luego se anulen: su número no se reutiliza).</summary>
    public int Entregados { get; private set; }

    public bool Abierto { get; private set; }
    public DateTimeOffset AbiertoEn { get; private set; }
    public DateTimeOffset? CerradoEn { get; private set; }

    // EF
    private LibroDigital() { }

    /// <param name="partidasYaEnFolio">Partidas que ya tiene ocupadas el folio inicial en el libro físico (0 si se empieza folio nuevo).</param>
    public static LibroDigital Abrir(
        string tipoSacramento, string numero, int folioInicial, int partidaInicial,
        int partidasPorFolio, int foliosPorLibro, ModoPartida modoPartida,
        DateTimeOffset ahora, int partidasYaEnFolio = 0)
    {
        if (string.IsNullOrWhiteSpace(tipoSacramento)) throw new ArgumentException("Falta el sacramento.", nameof(tipoSacramento));
        var num = TextoLibro.Limpiar(numero) ?? throw new ArgumentException("Falta el número de libro.", nameof(numero));
        if (partidasPorFolio < 1) throw new ArgumentOutOfRangeException(nameof(partidasPorFolio), "Debe haber al menos 1 partida por folio.");
        if (foliosPorLibro < 1) throw new ArgumentOutOfRangeException(nameof(foliosPorLibro), "Debe haber al menos 1 folio por libro.");
        if (folioInicial < 1 || folioInicial > foliosPorLibro)
            throw new ArgumentOutOfRangeException(nameof(folioInicial), "El folio inicial debe estar entre 1 y los folios por libro.");
        if (partidaInicial < 1) throw new ArgumentOutOfRangeException(nameof(partidaInicial), "La partida inicial debe ser al menos 1.");
        if (partidasYaEnFolio < 0 || partidasYaEnFolio > partidasPorFolio)
            throw new ArgumentOutOfRangeException(nameof(partidasYaEnFolio), "Las partidas ya ocupadas no pueden pasar de las partidas por folio.");

        return new LibroDigital
        {
            TipoSacramento = tipoSacramento,
            Numero = num,
            FolioInicial = folioInicial,
            PartidaInicial = partidaInicial,
            PartidasPorFolio = partidasPorFolio,
            FoliosPorLibro = foliosPorLibro,
            ModoPartida = modoPartida,
            FolioActual = folioInicial,
            PartidasEnFolio = partidasYaEnFolio,
            UltimaPartida = partidaInicial - 1,
            Abierto = true,
            AbiertoEn = ahora,
        };
    }

    /// <summary>true si ya no cabe otro asiento (último folio con todas sus partidas ocupadas).</summary>
    public bool EstaLleno => FolioActual >= FoliosPorLibro && PartidasEnFolio >= PartidasPorFolio;

    /// <summary>Asientos que aún caben en el libro.</summary>
    public int Restantes => (FoliosPorLibro - FolioActual) * PartidasPorFolio + (PartidasPorFolio - PartidasEnFolio);

    /// <summary>Folio y partida que recibiría el próximo asiento, sin modificar nada; null si el libro está lleno.</summary>
    public (int Folio, int Partida)? Siguiente()
    {
        if (EstaLleno) return null;

        var folio = PartidasEnFolio >= PartidasPorFolio ? FolioActual + 1 : FolioActual;
        var enFolio = folio == FolioActual ? PartidasEnFolio + 1 : 1;
        var partida = ModoPartida == ModoPartida.ReiniciaPorFolio ? enFolio : UltimaPartida + 1;
        return (folio, partida);
    }

    /// <summary>Entrega el siguiente número y avanza el contador. Lanza si el libro está lleno o cerrado.</summary>
    public (int Folio, int Partida) Asignar()
    {
        if (!Abierto) throw new InvalidOperationException("El libro digital está cerrado.");
        var (folio, partida) = Siguiente() ?? throw new InvalidOperationException("El libro digital está lleno.");

        PartidasEnFolio = folio == FolioActual ? PartidasEnFolio + 1 : 1;
        FolioActual = folio;
        UltimaPartida = partida;
        Entregados++;
        return (folio, partida);
    }

    public void Cerrar(DateTimeOffset ahora)
    {
        if (!Abierto) return;
        Abierto = false;
        CerradoEn = ahora;
    }

    /// <summary>Libro siguiente con la misma configuración, desde el folio 1; la partida continúa solo si es correlativa.</summary>
    public LibroDigital AbrirSiguiente(string numeroSiguiente, DateTimeOffset ahora) =>
        Abrir(TipoSacramento, numeroSiguiente, 1,
            ModoPartida == ModoPartida.Correlativa ? UltimaPartida + 1 : 1,
            PartidasPorFolio, FoliosPorLibro, ModoPartida, ahora);
}
