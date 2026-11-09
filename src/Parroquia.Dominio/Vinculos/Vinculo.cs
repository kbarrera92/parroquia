using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Vinculos;

/// <summary>
/// Una persona dentro de un registro: <c>Tipo</c> es la clave del módulo ("bautismo"), <c>Persona</c> es "" salvo en
/// Matrimonio ("esposo" / "esposa").
/// </summary>
public readonly record struct ExtremoVinculo(string Tipo, long Id, string Persona = "") : IComparable<ExtremoVinculo>
{
    public int CompareTo(ExtremoVinculo otro)
    {
        var c = string.CompareOrdinal(Tipo, otro.Tipo);
        if (c != 0) return c;
        c = Id.CompareTo(otro.Id);
        return c != 0 ? c : string.CompareOrdinal(Persona, otro.Persona);
    }
}

/// <summary>
/// Relación simétrica "misma persona" entre dos extremos de libros distintos (o del mismo libro). Se guarda en orden
/// canónico (el menor como origen) para que el par sea único. No hay FK entre tablas: lo valida el servicio.
/// Se desvincula anulando (con motivo), nunca borrando; volver a vincular el par reactiva la misma fila.
/// </summary>
public sealed class Vinculo : EntidadAuditable, IAnulable
{
    public string TipoOrigen { get; set; } = "";
    public long IdOrigen { get; set; }
    public string PersonaOrigen { get; set; } = "";
    public string TipoDestino { get; set; } = "";
    public long IdDestino { get; set; }
    public string PersonaDestino { get; set; } = "";

    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }

    public ExtremoVinculo Origen => new(TipoOrigen, IdOrigen, PersonaOrigen);
    public ExtremoVinculo Destino => new(TipoDestino, IdDestino, PersonaDestino);

    /// <summary>Asigna los extremos en orden canónico.</summary>
    public void Establecer(ExtremoVinculo a, ExtremoVinculo b)
    {
        var (menor, mayor) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        (TipoOrigen, IdOrigen, PersonaOrigen) = (menor.Tipo, menor.Id, menor.Persona);
        (TipoDestino, IdDestino, PersonaDestino) = (mayor.Tipo, mayor.Id, mayor.Persona);
    }
}
