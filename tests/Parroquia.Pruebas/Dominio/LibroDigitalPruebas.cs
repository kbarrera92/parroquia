using Parroquia.Dominio.Numeracion;

namespace Parroquia.Pruebas.Dominio;

public class LibroDigitalPruebas
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(-6));

    private static LibroDigital Nuevo(
        int folioInicial = 1, int partidaInicial = 1, int porFolio = 3, int folios = 2,
        ModoPartida modo = ModoPartida.Correlativa, int yaEnFolio = 0) =>
        LibroDigital.Abrir("bautismo", "81", folioInicial, partidaInicial, porFolio, folios, modo, Ahora, yaEnFolio);

    [Fact]
    public void Entrega_folio_y_partida_correlativos()
    {
        var libro = Nuevo();

        var entregados = Enumerable.Range(0, 5).Select(_ => libro.Asignar()).ToArray();

        entregados.Should().Equal((1, 1), (1, 2), (1, 3), (2, 4), (2, 5));
        libro.Entregados.Should().Be(5);
    }

    [Fact]
    public void Siguiente_no_modifica_el_libro()
    {
        var libro = Nuevo();

        libro.Siguiente().Should().Be((1, 1));
        libro.Siguiente().Should().Be((1, 1));
        libro.Entregados.Should().Be(0);
    }

    [Fact]
    public void Con_reinicio_por_folio_la_partida_vuelve_a_uno()
    {
        var libro = Nuevo(modo: ModoPartida.ReiniciaPorFolio);

        var entregados = Enumerable.Range(0, 5).Select(_ => libro.Asignar()).ToArray();

        entregados.Should().Equal((1, 1), (1, 2), (1, 3), (2, 1), (2, 2));
    }

    [Fact]
    public void Continua_un_libro_fisico_a_medio_llenar()
    {
        // Libro 80 físico: folio 151, con 1 de 3 partidas ya usada; la última partida escrita fue la 301.
        var libro = Nuevo(folioInicial: 2, partidaInicial: 302, folios: 3, yaEnFolio: 1);

        var entregados = Enumerable.Range(0, 3).Select(_ => libro.Asignar()).ToArray();

        entregados.Should().Equal((2, 302), (2, 303), (3, 304));
    }

    [Fact]
    public void Se_llena_al_ocupar_la_ultima_partida_del_ultimo_folio()
    {
        var libro = Nuevo(); // 2 folios x 3 partidas = 6
        for (var i = 0; i < 5; i++) libro.Asignar();

        libro.EstaLleno.Should().BeFalse();
        libro.Restantes.Should().Be(1);

        libro.Asignar().Should().Be((2, 6));

        libro.EstaLleno.Should().BeTrue();
        libro.Restantes.Should().Be(0);
        libro.Siguiente().Should().BeNull();
        libro.Invoking(l => l.Asignar()).Should().Throw<InvalidOperationException>().WithMessage("*lleno*");
    }

    [Fact]
    public void Restantes_cuenta_desde_el_inicio_a_medio_libro()
    {
        Nuevo(folioInicial: 2, yaEnFolio: 1).Restantes.Should().Be(2);
        Nuevo().Restantes.Should().Be(6);
    }

    [Fact]
    public void Un_libro_cerrado_no_entrega_numeros()
    {
        var libro = Nuevo();
        libro.Cerrar(Ahora);

        libro.Abierto.Should().BeFalse();
        libro.CerradoEn.Should().Be(Ahora);
        libro.Invoking(l => l.Asignar()).Should().Throw<InvalidOperationException>().WithMessage("*cerrado*");
    }

    [Fact]
    public void El_libro_siguiente_continua_la_partida_correlativa()
    {
        var libro = Nuevo();
        for (var i = 0; i < 6; i++) libro.Asignar();

        var siguiente = libro.AbrirSiguiente("82", Ahora);

        siguiente.Numero.Should().Be("82");
        siguiente.Abierto.Should().BeTrue();
        siguiente.Siguiente().Should().Be((1, 7));
        siguiente.PartidasPorFolio.Should().Be(3);
        siguiente.FoliosPorLibro.Should().Be(2);
    }

    [Fact]
    public void Reinicia_por_libro_es_correlativa_dentro_del_libro_y_vuelve_a_uno_en_el_siguiente()
    {
        var libro = Nuevo(modo: ModoPartida.ReiniciaPorLibro);

        var entregados = Enumerable.Range(0, 4).Select(_ => libro.Asignar()).ToArray();
        entregados.Should().Equal((1, 1), (1, 2), (1, 3), (2, 4));

        libro.Asignar(); libro.Asignar(); // llena el libro (6)
        libro.AbrirSiguiente("82", Ahora).Siguiente().Should().Be((1, 1));
    }

    [Fact]
    public void El_libro_siguiente_con_reinicio_empieza_la_partida_en_uno()
    {
        var libro = Nuevo(modo: ModoPartida.ReiniciaPorFolio);
        for (var i = 0; i < 6; i++) libro.Asignar();

        libro.AbrirSiguiente("82", Ahora).Siguiente().Should().Be((1, 1));
    }

    [Theory]
    [InlineData("", 1, 1, 3, 2, 0)]
    [InlineData("81", 0, 1, 3, 2, 0)]   // folio inicial < 1
    [InlineData("81", 3, 1, 3, 2, 0)]   // folio inicial > folios por libro
    [InlineData("81", 1, 0, 3, 2, 0)]   // partida inicial < 1
    [InlineData("81", 1, 1, 0, 2, 0)]   // sin partidas por folio
    [InlineData("81", 1, 1, 3, 0, 0)]   // sin folios
    [InlineData("81", 1, 1, 3, 2, 4)]   // más partidas ya usadas que las del folio
    public void Rechaza_configuraciones_invalidas(string numero, int folio, int partida, int porFolio, int folios, int yaEnFolio)
    {
        var abrir = () => LibroDigital.Abrir("bautismo", numero, folio, partida, porFolio, folios, ModoPartida.Correlativa, Ahora, yaEnFolio);

        abrir.Should().Throw<ArgumentException>();
    }
}
