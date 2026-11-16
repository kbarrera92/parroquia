using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Supletorias;

/// <summary>
/// Partida supletoria: reposición de una partida que no se encontró en el libro o está dañada. Se asienta en el
/// Libro de Supletorias (uno solo, compartido por los 4 sacramentos) y apunta al registro del sacramento que crea a la
/// vez; ese registro es el que se busca e imprime. Sin FK dura (como <c>Vinculo</c>): se une por clave del módulo + id.
/// </summary>
public class Supletoria : EntidadAuditable, IAnulable
{
    /// <summary>Clave del libro digital compartido (<c>LibroDigital.TipoSacramento</c>).</summary>
    public const string ClaveLibroDigital = "supletoria";

    /// <summary>Texto con el que el libro supletorio figura en el <c>Libro</c> del registro cuando no se conoce el original.</summary>
    public const string PrefijoLibro = "Supletorio";

    /// <summary>Clave del <c>IModuloSacramental</c> del registro (p. ej. "bautismo").</summary>
    public string TipoSacramento { get; set; } = "";
    public long RegistroId { get; set; }

    /// <summary>Libro, folio (hoja) y partida (número del margen) del Libro de Supletorias.</summary>
    public string Libro { get; set; } = "";
    public string Folio { get; set; } = "";
    public string Partida { get; set; } = "";

    /// <summary>Libro digital que numeró esta partida; null = transcrita de un libro supletorio de papel.</summary>
    public long? LibroDigitalId { get; set; }
    public bool EsAsientoDelSistema => LibroDigitalId is not null;

    // Derivadas (no se auditan).
    public string LibroOrden { get; private set; } = "";
    public string FolioOrden { get; private set; } = "";
    public string PartidaOrden { get; private set; } = "";

    /// <summary>Fecha del asiento ("En esta Parroquia a 2 de octubre de 2026").</summary>
    public FechaParcial FechaSupletoria { get; set; } = FechaParcial.Vacia;

    /// <summary>Quien compareció a dar fe y su parentesco con la persona ("Tía").</summary>
    public string Testigo { get; set; } = "";
    public string? ParentescoTestigo { get; set; }

    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }

    /// <summary>"Supletorio 3": cómo se escribe el libro supletorio dentro del registro del sacramento.</summary>
    public static string LibroEnRegistro(string? libro) => $"{PrefijoLibro} {TextoLibro.Limpiar(libro)}";

    /// <summary>true si el libro empieza por «Supletorio»: ese nombre solo lo usan los registros que nacen de una supletoria.</summary>
    public static bool EsLibroReservado(string? libro) =>
        TextoLibro.Limpiar(libro) is { } l && l.StartsWith(PrefijoLibro, StringComparison.OrdinalIgnoreCase);

    public void ActualizarDerivados()
    {
        LibroOrden = ClaveOrdenNatural.De(Libro);
        FolioOrden = ClaveOrdenNatural.De(Folio);
        PartidaOrden = ClaveOrdenNatural.De(Partida);
    }
}
