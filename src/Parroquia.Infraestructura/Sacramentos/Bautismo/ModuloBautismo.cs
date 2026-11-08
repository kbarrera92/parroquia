using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Registro de Bautismo en el menú y la búsqueda global.</summary>
public sealed class ModuloBautismo(ServicioBautismo servicio) : IModuloSacramental
{
    public const string ClaveModulo = "bautismo";

    public string Clave => ClaveModulo;
    public string Nombre => "Bautismo";
    public string Entidad => ServicioBautismo.NombreEntidad;
    public int Orden => 10;
    public string RutaListado => "/Bautismo";
    public string RutaDetalle(long id) => $"/Bautismo/Detalle/{id}";

    public async Task<ResultadosModulo> BuscarAsync(ConsultaBusqueda consulta, int maximo, CancellationToken ct = default)
    {
        var pagina = await servicio.BuscarAsync(
            new FiltroBautismo(Texto: consulta.Texto, IncluirAnulados: consulta.IncluirAnulados), 1, maximo, ct);

        return new ResultadosModulo(
            [.. pagina.Items.Select(f => new ResultadoBusquedaRegistro(
                Clave, Nombre, f.Id, f.Nombres, f.Apellidos, f.Fecha, f.Libro, f.Folio, f.Partida,
                f.Revisar, f.Anulado, RutaDetalle(f.Id)))],
            pagina.Total);
    }

    public async Task<ConstanciaPreparada?> PrepararConstanciaAsync(
        long id, ContextoConstancia contexto, CancellationToken ct = default)
    {
        var datos = await servicio.DatosParaConstanciaAsync(id, ct);
        return datos is null
            ? null
            : new ConstanciaPreparada(PlantillaBautismo.Render(datos, contexto), PlantillaBautismo.Advertencias(datos));
    }
}
