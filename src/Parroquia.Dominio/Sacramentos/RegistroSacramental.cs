using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Sacramentos;

/// <summary>
/// Campos comunes de todo registro de libro sacramental (docs/02 §3.1). Cada sacramento hereda en su propia tabla (sin TPH).
/// Los textos del libro se guardan tal cual; las columnas "Orden" y <see cref="BusquedaNormalizada"/> son derivadas.
/// </summary>
public abstract class RegistroSacramental : EntidadAuditable, IAnulable
{
    /// <summary>Texto (admite letras y "bis"). El folio reinicia por libro.</summary>
    public string Libro { get; set; } = "";
    public string Folio { get; set; } = "";
    public string Partida { get; set; } = "";

    // Derivadas (no se auditan): orden natural para listar "2" antes que "10".
    public string LibroOrden { get; private set; } = "";
    public string FolioOrden { get; private set; } = "";
    public string PartidaOrden { get; private set; } = "";

    /// <summary>Texto de búsqueda sin tildes ni partículas (derivada, no se audita).</summary>
    public string BusquedaNormalizada { get; protected set; } = "";

    public bool Revisar { get; set; }
    public string? MotivoRevision { get; set; }

    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }

    public string? NotasMarginales { get; set; }

    /// <summary>Ministro del catálogo; si no está en la lista se guarda en <see cref="MinistroTexto"/>.</summary>
    public long? MinistroId { get; set; }
    public Ministro? Ministro { get; set; }
    public string? MinistroTexto { get; set; }

    /// <summary>Nombre del ministro tal como debe mostrarse (catálogo o texto libre).</summary>
    public string? NombreMinistro => Ministro?.Nombre ?? MinistroTexto;

    /// <summary>Recalcula las columnas derivadas. Llamar antes de guardar cuando cambien Libro/Folio/Partida o los nombres.</summary>
    public void ActualizarDerivados()
    {
        LibroOrden = ClaveOrdenNatural.De(Libro);
        FolioOrden = ClaveOrdenNatural.De(Folio);
        PartidaOrden = ClaveOrdenNatural.De(Partida);
        BusquedaNormalizada = TextoBusqueda();
    }

    /// <summary>Texto ya normalizado con los nombres de la(s) persona(s) del registro.</summary>
    protected abstract string TextoBusqueda();
}
