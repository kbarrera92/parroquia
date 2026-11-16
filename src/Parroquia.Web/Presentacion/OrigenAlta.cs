using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Vinculos;
using Parroquia.Infraestructura.Vinculos;

namespace Parroquia.Web.Presentacion;

/// <summary>
/// Alta de la misma persona desde el botón «+» de la búsqueda: el registro de origen (libro, id y persona) y el
/// contrayente de destino (<c>Como</c>, solo Matrimonio). Se enlaza por GET (precarga) y viaja en campos ocultos
/// al guardar, donde el registro nuevo se vincula con el de origen. Lo usan las páginas <c>Editar</c> de todos los libros.
/// </summary>
public sealed class OrigenAlta
{
    public string? Clave { get; set; }
    public long? Id { get; set; }
    public string? Persona { get; set; }
    public string? Como { get; set; }

    public bool Presente => !string.IsNullOrWhiteSpace(Clave) && Id is not null;

    /// <summary>El origen existe y se pudo leer: hay datos que precargar y un vínculo que crear.</summary>
    public bool Valido { get; private set; }

    public DatosPersona? Datos { get; private set; }

    /// <summary>«Bautismo de Ana Prueba (libro 1, folio 2, partida 3)».</summary>
    public string? Descripcion { get; private set; }

    /// <summary>«Nombres Apellidos» de la persona de origen (para preguntar qué papel tiene en el destino).</summary>
    public string? NombreMostrado { get; private set; }

    public string? Aviso { get; private set; }

    public ExtremoVinculo Extremo => new(Clave ?? "", Id ?? 0, Persona ?? "");

    public async Task CargarAsync(
        string claveDestino, ServicioVinculos vinculos, IEnumerable<IModuloSacramental> modulos, CancellationToken ct)
    {
        if (!Presente) return;
        Persona ??= "";

        var origen = modulos.FirstOrDefault(m => m.Clave == Clave);
        var destino = modulos.FirstOrDefault(m => m.Clave == claveDestino);
        // Sin elección, la persona entra como la primera del destino (en Matrimonio, el esposo).
        if (string.IsNullOrEmpty(Como)) Como = destino?.Personas.FirstOrDefault() ?? "";
        var resumen = origen is null || destino is null || !destino.Personas.Contains(Como)
            ? null
            : await vinculos.ResumenAsync(Extremo, ct);
        var datos = resumen is null ? null : await origen!.DatosPersonaAsync(Id!.Value, Persona, ct);

        if (resumen is null || datos is null)
        {
            Aviso = "No se encontró el registro de origen (o está anulado): el formulario se abre vacío y no se vinculará.";
            return;
        }

        Valido = true;
        Datos = datos;
        NombreMostrado = $"{resumen.Nombres} {resumen.Apellidos}".Trim();
        Descripcion = $"{resumen.Sacramento} de {resumen.Nombres} {resumen.Apellidos}".Trim()
            + $" (libro {resumen.Libro}, folio {resumen.Folio}, partida {resumen.Partida})";
    }

    /// <summary>Vincula el registro recién guardado con el de origen. Devuelve la frase que se agrega al mensaje de éxito.</summary>
    public async Task<string> VincularAsync(
        string claveDestino, long idNuevo, bool puedeVincular, ServicioVinculos vinculos, CancellationToken ct)
    {
        if (!puedeVincular) return " No se vinculó con el registro de origen: su rol no puede vincular.";
        var r = await vinculos.VincularAsync(Extremo, new ExtremoVinculo(claveDestino, idNuevo, Como ?? ""), ct);
        return r.Ok
            ? $" Quedó vinculado con {Descripcion}."
            : $" No se pudo vincular con el registro de origen ({r.Error}); vincúlelo desde el detalle.";
    }
}
