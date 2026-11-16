using Parroquia.Infraestructura.Supletorias;

namespace Parroquia.Web.Presentacion;

/// <summary>
/// Datos propios de la partida supletoria dentro del formulario de cada sacramento (<c>Input.Supletoria.*</c>).
/// Todo es opcional a nivel de binding: las reglas viven en <c>ServicioSupletorias</c>.
/// </summary>
public class EntradaSupletoria
{
    /// <summary>El sistema asigna libro/folio/partida del Libro de Supletorias; false = se transcriben de un libro de papel.</summary>
    public bool NumeraSistema { get; set; }

    public string? Libro { get; set; }
    public string? Folio { get; set; }
    public string? Partida { get; set; }

    public string? Fecha { get; set; }
    public string? Testigo { get; set; }
    public string? ParentescoTestigo { get; set; }

    public DatosSupletoriaFormulario ADatos() =>
        new(NumeraSistema, Libro, Folio, Partida, Fecha, Testigo, ParentescoTestigo);

    public static EntradaSupletoria De(VistaSupletoria v) => new()
    {
        NumeraSistema = v.AsientoDelSistema,
        Libro = v.Libro, Folio = v.Folio, Partida = v.Partida,
        Fecha = v.FechaCaptura, Testigo = v.Testigo, ParentescoTestigo = v.ParentescoTestigo,
    };
}
