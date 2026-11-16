using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Supletorias;

namespace Parroquia.Pruebas.Dominio;

public class SupletoriaPruebas
{
    [Theory]
    [InlineData("1", "Supletorio 1")]
    [InlineData("  12 ", "Supletorio 12")]
    [InlineData("2 bis", "Supletorio 2 bis")]
    public void LibroEnRegistro_antepone_el_prefijo_al_numero_del_libro_supletorio(string libro, string esperado) =>
        Supletoria.LibroEnRegistro(libro).Should().Be(esperado);

    [Theory]
    [InlineData("Supletorio 1", true)]
    [InlineData("  supletorio 7", true)]
    [InlineData("SUPLETORIO", true)]
    [InlineData("80", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsLibroReservado_detecta_el_prefijo_sin_importar_mayusculas(string? libro, bool esperado) =>
        Supletoria.EsLibroReservado(libro).Should().Be(esperado);

    [Fact]
    public void ActualizarDerivados_calcula_el_orden_natural_de_libro_folio_y_partida()
    {
        var s = new Supletoria { Libro = "2", Folio = "10", Partida = "200" };

        s.ActualizarDerivados();

        s.LibroOrden.Should().Be(ClaveOrdenNatural.De("2"));
        s.FolioOrden.Should().Be(ClaveOrdenNatural.De("10"));
        s.PartidaOrden.Should().Be(ClaveOrdenNatural.De("200"));
        string.CompareOrdinal(s.FolioOrden, ClaveOrdenNatural.De("9")).Should().BePositive();
    }
}
