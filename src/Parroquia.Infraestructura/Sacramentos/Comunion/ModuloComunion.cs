using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Registro de Primera Comunión en el menú y la búsqueda global.</summary>
public sealed class ModuloComunion(ServicioComunion servicio) : IModuloSacramental
{
    public const string ClaveModulo = "comunion";

    public string Clave => ClaveModulo;
    public string Nombre => "Primera Comunión";
    public string Entidad => ServicioComunion.NombreEntidad;
    public int Orden => 30;
    public string RutaListado => "/Comunion";
    public string RutaDetalle(long id) => $"/Comunion/Detalle/{id}";

    public async Task<ResultadosModulo> BuscarAsync(ConsultaBusqueda consulta, int maximo, CancellationToken ct = default)
    {
        var pagina = await servicio.BuscarAsync(
            new FiltroComunion(
                Texto: consulta.Texto, Libro: consulta.Libro, Folio: consulta.Folio,
                AnioDesde: consulta.AnioDesde, AnioHasta: consulta.AnioHasta, IncluirAnulados: consulta.IncluirAnulados), 1, maximo, ct);

        return new ResultadosModulo(
            [.. pagina.Items.Select(f => new ResultadoBusquedaRegistro(
                Clave, Nombre, f.Id, f.Nombres, f.Apellidos, f.Fecha, f.Libro, f.Folio, f.Partida,
                f.Revisar, f.Anulado, RutaDetalle(f.Id)))],
            pagina.Total);
    }

    public async Task<IReadOnlyList<ResultadoBusquedaRegistro>> ResumenesAsync(
        IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        [.. (await servicio.ResumenesAsync(ids, ct)).Select(f => new ResultadoBusquedaRegistro(
            Clave, Nombre, f.Id, f.Nombres, f.Apellidos, f.Fecha, f.Libro, f.Folio, f.Partida,
            f.Revisar, f.Anulado, RutaDetalle(f.Id)))];

    public async Task<ConstanciaPreparada?> PrepararConstanciaAsync(
        long id, ContextoConstancia contexto, CancellationToken ct = default)
    {
        var datos = await servicio.DatosParaConstanciaAsync(id, ct);
        return datos is null
            ? null
            : new ConstanciaPreparada(PlantillaComunion.Render(datos, contexto), PlantillaComunion.Advertencias(datos));
    }
}
