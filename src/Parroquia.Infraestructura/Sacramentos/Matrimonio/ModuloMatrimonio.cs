using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Registro de Matrimonio en el menú y la búsqueda global. Aporta una fila por contrayente que coincide.</summary>
public sealed class ModuloMatrimonio(ServicioMatrimonio servicio) : IModuloSacramental
{
    public const string ClaveModulo = "matrimonio";

    public string Clave => ClaveModulo;
    public string Nombre => "Matrimonio";
    public string Entidad => ServicioMatrimonio.NombreEntidad;
    public int Orden => 40;
    public string RutaListado => "/Matrimonio";
    public string RutaDetalle(long id) => $"/Matrimonio/Detalle/{id}";

    public async Task<ResultadosModulo> BuscarAsync(ConsultaBusqueda consulta, int maximo, CancellationToken ct = default)
    {
        var (filas, total) = await servicio.BuscarPersonasAsync(
            new FiltroMatrimonio(Texto: consulta.Texto, IncluirAnulados: consulta.IncluirAnulados), maximo, ct);

        var items = new List<ResultadoBusquedaRegistro>();
        foreach (var f in filas)
        {
            if (f.CoincideEsposo) items.Add(Resultado(f, f.EsposoNombres, f.EsposoApellidos));
            if (f.CoincideEsposa) items.Add(Resultado(f, f.EsposaNombres, f.EsposaApellidos));
        }
        return new ResultadosModulo(items, total);
    }

    private ResultadoBusquedaRegistro Resultado(FilaMatrimonio f, string nombres, string apellidos) => new(
        Clave, Nombre, f.Id, nombres, apellidos, f.Fecha, f.Libro, f.Folio, f.Partida,
        f.Revisar, f.Anulado, RutaDetalle(f.Id));

    public async Task<ConstanciaPreparada?> PrepararConstanciaAsync(
        long id, ContextoConstancia contexto, CancellationToken ct = default)
    {
        var datos = await servicio.DatosParaConstanciaAsync(id, ct);
        return datos is null
            ? null
            : new ConstanciaPreparada(PlantillaMatrimonio.Render(datos, contexto), PlantillaMatrimonio.Advertencias(datos));
    }
}
