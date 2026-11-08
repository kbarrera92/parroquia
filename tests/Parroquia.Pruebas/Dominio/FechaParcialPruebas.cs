using Parroquia.Dominio.Comun;

namespace Parroquia.Pruebas.Dominio;

public class FechaParcialPruebas
{
    private static readonly DateOnly Hoy = new(2026, 11, 7);

    [Theory]
    [InlineData(1978, 8, 13, "13 de agosto de 1978")]
    [InlineData(1978, 8, null, "agosto de 1978")]
    [InlineData(1978, null, null, "1978")]
    [InlineData(2000, 1, 1, "1 de enero de 2000")]
    public void ATexto_imprime_lo_que_exista(int anio, int? mes, int? dia, string esperado)
    {
        var f = FechaParcial.Crear((short)anio, (short?)mes, (short?)dia, Hoy);
        f.ATexto().Should().Be(esperado);
    }

    [Fact]
    public void Vacia_imprime_texto_vacio_y_clave_cero()
    {
        FechaParcial.Vacia.ATexto().Should().BeEmpty();
        FechaParcial.Vacia.EsVacia.Should().BeTrue();
        FechaParcial.Vacia.ClaveOrden.Should().Be(0);
    }

    [Fact]
    public void EsCompleta_solo_con_anio_mes_y_dia()
    {
        FechaParcial.Crear(1978, 8, 13, Hoy).EsCompleta.Should().BeTrue();
        FechaParcial.Crear(1978, 8, null, Hoy).EsCompleta.Should().BeFalse();
        FechaParcial.Crear(1978, null, null, Hoy).EsCompleta.Should().BeFalse();
        FechaParcial.Vacia.EsCompleta.Should().BeFalse();
    }

    [Theory]
    [InlineData(1499, null, null)]   // antes de 1500
    [InlineData(2027, null, null)]   // año futuro
    [InlineData(1978, 13, null)]
    [InlineData(1978, 0, null)]
    [InlineData(1978, 4, 31)]        // abril tiene 30
    [InlineData(1978, 2, 29)]        // 1978 no es bisiesto
    [InlineData(1978, 8, 0)]
    [InlineData(null, 8, null)]      // mes sin año
    [InlineData(null, null, 13)]     // día sin mes ni año
    [InlineData(1978, null, 13)]     // día sin mes
    [InlineData(2026, 11, 8)]        // fecha completa futura
    [InlineData(2026, 12, null)]     // mes futuro del año actual
    public void Crear_rechaza_fechas_invalidas(int? anio, int? mes, int? dia)
    {
        var ok = FechaParcial.TryCrear((short?)anio, (short?)mes, (short?)dia, Hoy, out _, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
        FluentActions.Invoking(() => FechaParcial.Crear((short?)anio, (short?)mes, (short?)dia, Hoy))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(2024, 2, 29)]  // bisiesto
    [InlineData(2000, 2, 29)]  // divisible por 400
    [InlineData(1500, null, null)]
    [InlineData(2026, 11, 7)]  // hoy
    [InlineData(2026, null, null)]
    public void Crear_acepta_fechas_validas_en_los_limites(int anio, int? mes, int? dia)
    {
        FechaParcial.TryCrear((short)anio, (short?)mes, (short?)dia, Hoy, out _, out _).Should().BeTrue();
    }

    [Fact]
    public void Crear_29_de_febrero_en_siglo_no_bisiesto_falla()
    {
        FechaParcial.TryCrear(1900, 2, 29, Hoy, out _, out _).Should().BeFalse(); // 1900 no es bisiesto
    }

    [Theory]
    [InlineData("13/8/1978", 1978, 8, 13)]
    [InlineData("13-08-1978", 1978, 8, 13)]
    [InlineData("13.08.1978", 1978, 8, 13)]
    [InlineData(" 1/1/2000 ", 2000, 1, 1)]
    [InlineData("8/1978", 1978, 8, null)]
    [InlineData("08-1978", 1978, 8, null)]
    [InlineData("1978", 1978, null, null)]
    public void Parsear_acepta_formatos_comunes(string texto, int anio, int? mes, int? dia)
    {
        FechaParcial.TryParsear(texto, Hoy, out var f, out var error).Should().BeTrue(error);

        f!.Anio.Should().Be((short)anio);
        f.Mes.Should().Be((short?)mes);
        f.Dia.Should().Be((short?)dia);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parsear_vacio_da_fecha_vacia(string? texto)
    {
        FechaParcial.TryParsear(texto, Hoy, out var f, out _).Should().BeTrue();
        f!.EsVacia.Should().BeTrue();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("32/1/2000")]
    [InlineData("1/13/2000")]
    [InlineData("1/2/3/2000")]
    [InlineData("29/2/1978")]
    [InlineData("13/8/2999")]
    [InlineData("78")]
    public void Parsear_rechaza_texto_invalido(string texto)
    {
        FechaParcial.TryParsear(texto, Hoy, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ClaveOrden_es_anio_mes_dia_con_ceros_si_falta()
    {
        FechaParcial.Crear(1978, 8, 13, Hoy).ClaveOrden.Should().Be(19780813);
        FechaParcial.Crear(1978, 8, null, Hoy).ClaveOrden.Should().Be(19780800);
        FechaParcial.Crear(1978, null, null, Hoy).ClaveOrden.Should().Be(19780000);
    }

    [Fact]
    public void Ordena_por_clave_con_parciales_antes_que_completas_del_mismo_periodo()
    {
        var a = FechaParcial.Crear(1978, null, null, Hoy);
        var b = FechaParcial.Crear(1978, 8, null, Hoy);
        var c = FechaParcial.Crear(1978, 8, 13, Hoy);
        var d = FechaParcial.Crear(1979, null, null, Hoy);

        var ordenadas = new[] { d, c, FechaParcial.Vacia, b, a }.OrderBy(x => x).ToArray();

        ordenadas.Should().Equal(FechaParcial.Vacia, a, b, c, d);
    }

    [Fact]
    public void Igualdad_por_valor()
    {
        FechaParcial.Crear(1978, 8, 13, Hoy).Should().Be(FechaParcial.Crear(1978, 8, 13, Hoy));
        FechaParcial.Crear(1978, 8, null, Hoy).Should().NotBe(FechaParcial.Crear(1978, 8, 13, Hoy));
    }

    [Fact]
    public void Comparacion_es_menor_o_igual_para_validar_nacimiento_antes_de_bautismo()
    {
        var nacimiento = FechaParcial.Crear(1978, 8, 13, Hoy);
        var bautismo = FechaParcial.Crear(1978, 9, null, Hoy);

        (nacimiento.CompareTo(bautismo) <= 0).Should().BeTrue();
        (bautismo.CompareTo(nacimiento) <= 0).Should().BeFalse();
    }
}
