using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Sacramentos;

namespace Parroquia.Pruebas.Dominio;

public class RegistroSacramentalPruebas
{
    private static readonly DateOnly Hoy = new(2026, 11, 7);

    [Theory]
    [InlineData("60", "61")]
    [InlineData("9", "10")]
    [InlineData("099", "100")]
    [InlineData("009", "010")]
    [InlineData(" 7 ", "8")]
    [InlineData("2 bis", "2 bis")]
    [InlineData("A", "A")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NumeroSiguiente_suma_uno_solo_a_numeros_puros(string? entrada, string esperado) =>
        NumeroSiguiente.De(entrada).Should().Be(esperado);

    [Theory]
    [InlineData(2004, 10, 2, "02/10/2004")]
    [InlineData(2004, 10, null, "10/2004")]
    [InlineData(2004, null, null, "2004")]
    public void FormatoCaptura_se_vuelve_a_leer_igual(int anio, int? mes, int? dia, string esperado)
    {
        var fecha = FechaParcial.Crear((short)anio, (short?)mes, (short?)dia, Hoy);

        var texto = fecha.ATextoCaptura();

        texto.Should().Be(esperado);
        FechaParcial.TryParsear(texto, Hoy, out var leida, out _).Should().BeTrue();
        leida.Should().Be(fecha);
    }

    [Fact]
    public void FormatoCaptura_de_fecha_vacia_es_texto_vacio() =>
        FechaParcial.Vacia.ATextoCaptura().Should().BeEmpty();

    [Fact]
    public void ActualizarDerivados_calcula_ordenes_naturales_y_busqueda()
    {
        var c = new Comunion
        {
            Libro = "3", Folio = "2 bis", Partida = "10",
            Nombres = "José Ángel", Apellidos = "de la Peña",
        };

        c.ActualizarDerivados();

        c.LibroOrden.Should().Be(ClaveOrdenNatural.De("3"));
        c.FolioOrden.Should().Be(ClaveOrdenNatural.De("2 bis"));
        c.PartidaOrden.Should().Be(ClaveOrdenNatural.De("10"));
        c.BusquedaNormalizada.Should().Be("jose angel pena");
        string.CompareOrdinal(c.FolioOrden, ClaveOrdenNatural.De("10")).Should().BeLessThan(0);
    }
}
