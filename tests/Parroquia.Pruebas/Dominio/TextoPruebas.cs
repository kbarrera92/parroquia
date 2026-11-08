using Parroquia.Dominio.Comun;

namespace Parroquia.Pruebas.Dominio;

public class NormalizadorBusquedaPruebas
{
    [Theory]
    [InlineData("Juan Carlos Hidalgo Arévalo", "juan carlos hidalgo arevalo")]
    [InlineData("  JUAN   CARLOS  ", "juan carlos")]
    [InlineData("López de Pérez", "lopez perez")]
    [InlineData("María de la Cruz del Valle", "maria cruz valle")]
    [InlineData("Muñoz Güell", "munoz guell")]
    [InlineData("Pérez y López", "perez lopez")]
    [InlineData("de los Santos", "santos")]
    [InlineData("O'Connor-Díaz", "o connor diaz")]
    [InlineData("Delia Lasso", "delia lasso")]   // solo tokens completos
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normaliza_como_el_libro_sin_tildes_ni_particulas(string entrada, string esperado) =>
        NormalizadorBusqueda.Normalizar(entrada).Should().Be(esperado);

    [Fact]
    public void Null_da_vacio() => NormalizadorBusqueda.Normalizar((string?)null).Should().BeEmpty();

    [Fact]
    public void Une_varias_partes_en_orden()
    {
        NormalizadorBusqueda.Normalizar("Juan Carlos", "Hidalgo Arévalo").Should().Be("juan carlos hidalgo arevalo");
        NormalizadorBusqueda.Normalizar("Ana", null, "de León").Should().Be("ana leon");
    }

    [Fact]
    public void Consulta_y_dato_se_normalizan_igual()
    {
        var guardado = NormalizadorBusqueda.Normalizar("Juan Carlos", "Hidalgo Arévalo");
        guardado.Should().Contain(NormalizadorBusqueda.Normalizar("hidalgo arevalo"));
        guardado.Should().Contain(NormalizadorBusqueda.Normalizar("HIDALGO ARÉVALO"));
    }

    [Fact]
    public void Es_idempotente()
    {
        var una = NormalizadorBusqueda.Normalizar("María de la Cruz");
        NormalizadorBusqueda.Normalizar(una).Should().Be(una);
    }
}

public class ClaveOrdenNaturalPruebas
{
    [Fact]
    public void Numeros_ordenan_por_valor_no_por_texto()
    {
        var ordenados = new[] { "10", "2", "1", "100", "21" }
            .OrderBy(ClaveOrdenNatural.De, StringComparer.Ordinal).ToArray();

        ordenados.Should().Equal("1", "2", "10", "21", "100");
    }

    [Fact]
    public void Sufijos_y_bis_van_despues_del_numero_base()
    {
        var ordenados = new[] { "10", "2 bis", "2", "2A", "3" }
            .OrderBy(ClaveOrdenNatural.De, StringComparer.Ordinal).ToArray();

        ordenados.Should().Equal("2", "2A", "2 bis", "3", "10"); // sufijos en orden alfabético
    }

    [Theory]
    [InlineData("2bis", "2 bis")]
    [InlineData("2-bis", "2 BIS")]
    [InlineData("Á5", "a5")]
    public void Ignora_mayusculas_tildes_y_separadores(string a, string b) =>
        ClaveOrdenNatural.De(a).Should().Be(ClaveOrdenNatural.De(b));

    [Fact]
    public void Letras_antes_que_numeros_no_rompen_el_orden()
    {
        var ordenados = new[] { "B2", "A10", "A2" }
            .OrderBy(ClaveOrdenNatural.De, StringComparer.Ordinal).ToArray();

        ordenados.Should().Equal("A2", "A10", "B2");
    }

    [Fact]
    public void Vacio_y_null_dan_cadena_vacia()
    {
        ClaveOrdenNatural.De(null).Should().BeEmpty();
        ClaveOrdenNatural.De("  ").Should().BeEmpty();
    }
}

public class TextoLibroPruebas
{
    [Theory]
    [InlineData("  80 ", "80")]
    [InlineData("López   de  Pérez", "López de Pérez")]
    [InlineData("MARÍA  josé", "MARÍA josé")]   // el libro manda: no se cambia capitalización ni tildes
    [InlineData("2\tbis", "2 bis")]
    public void Recorta_y_colapsa_espacios_sin_tocar_mayusculas(string entrada, string esperado) =>
        TextoLibro.Limpiar(entrada).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Vacio_pasa_a_null(string? entrada) => TextoLibro.Limpiar(entrada).Should().BeNull();
}
