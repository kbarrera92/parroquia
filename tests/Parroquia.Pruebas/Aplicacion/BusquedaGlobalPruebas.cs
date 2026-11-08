using Parroquia.Aplicacion.Sacramentos;

namespace Parroquia.Pruebas.Aplicacion;

public class BusquedaGlobalPruebas
{
    private sealed class ModuloFalso(string clave, int orden, params ResultadoBusquedaRegistro[] registros) : IModuloSacramental
    {
        public int Llamadas { get; private set; }
        public ConsultaBusqueda? UltimaConsulta { get; private set; }
        public int UltimoMaximo { get; private set; }

        public string Clave => clave;
        public string Nombre => $"Módulo {clave}";
        public string Entidad => clave;
        public int Orden => orden;
        public string RutaListado => $"/{clave}";
        public string RutaDetalle(long id) => $"/{clave}/Detalle/{id}";

        public Task<ResultadosModulo> BuscarAsync(ConsultaBusqueda consulta, int maximo, CancellationToken ct = default)
        {
            Llamadas++;
            UltimaConsulta = consulta;
            UltimoMaximo = maximo;
            return Task.FromResult(new ResultadosModulo([.. registros.Take(maximo)], registros.Length));
        }
    }

    private static ResultadoBusquedaRegistro Fila(string clave, long id, string nombres, string apellidos) =>
        new(clave, $"Módulo {clave}", id, nombres, apellidos, "2004", "1", "2", "3", false, false, $"/{clave}/Detalle/{id}");

    [Fact]
    public async Task Une_los_modulos_ordenados_por_apellidos_sin_tildes_y_luego_nombres()
    {
        var a = new ModuloFalso("a", 1, Fila("a", 1, "Zoila", "Álvarez"), Fila("a", 2, "Luis", "Pérez"));
        var b = new ModuloFalso("b", 2, Fila("b", 3, "Ana", "Alvarez"), Fila("b", 4, "Carlos", "Perez"), Fila("b", 5, "Ana", "Perez"));
        var global = new BusquedaGlobal([b, a]);

        var r = await global.BuscarAsync("perez", 10);

        r.Items.Select(x => $"{x.Clave}{x.Id}").Should().Equal("b3", "a1", "b5", "b4", "a2");
    }

    [Fact]
    public async Task Misma_persona_en_dos_libros_conserva_el_orden_del_modulo()
    {
        var a = new ModuloFalso("a", 1, Fila("a", 1, "Ana", "Perez"));
        var b = new ModuloFalso("b", 2, Fila("b", 2, "Ana", "Perez"));

        var r = await new BusquedaGlobal([b, a]).BuscarAsync("ana perez", 10);

        r.Items.Select(x => x.Clave).Should().Equal("a", "b");
    }

    [Fact]
    public async Task Limita_por_modulo_e_informa_cuantos_hay_en_total()
    {
        var a = new ModuloFalso("a", 1, Fila("a", 1, "A", "Uno"), Fila("a", 2, "B", "Dos"), Fila("a", 3, "C", "Tres"));
        var b = new ModuloFalso("b", 2, Fila("b", 4, "D", "Cuatro"));

        var r = await new BusquedaGlobal([a, b]).BuscarAsync("xx", 2, default);

        a.UltimoMaximo.Should().Be(2);
        r.Items.Should().HaveCount(3);
        r.Modulos.Select(m => (m.Clave, m.Mostrados, m.Total)).Should().Equal(("a", 2, 3), ("b", 1, 1));
        r.Modulos[0].RutaListado.Should().Be("/a");
        r.Modulos[0].HayMas.Should().BeTrue();
        r.Modulos[1].HayMas.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("de la")] // solo partículas: normaliza a vacío
    [InlineData(".,;")]
    public async Task Texto_vacio_o_demasiado_corto_no_consulta_los_modulos(string? texto)
    {
        var a = new ModuloFalso("a", 1, Fila("a", 1, "A", "Uno"));

        var r = await new BusquedaGlobal([a]).BuscarAsync(texto, 10);

        a.Llamadas.Should().Be(0);
        r.Items.Should().BeEmpty();
        r.TextoValido.Should().BeFalse();
    }

    [Fact]
    public async Task Pasa_el_texto_tal_cual_al_modulo_que_lo_normaliza_el_mismo()
    {
        var a = new ModuloFalso("a", 1);

        var r = await new BusquedaGlobal([a]).BuscarAsync("  Hidalgo  Arévalo ", 10);

        r.TextoValido.Should().BeTrue();
        a.UltimaConsulta!.Texto.Should().Be("  Hidalgo  Arévalo ");
        a.UltimaConsulta.IncluirAnulados.Should().BeFalse();
    }

    [Fact]
    public async Task Sin_modulos_registrados_devuelve_vacio()
    {
        var r = await new BusquedaGlobal([]).BuscarAsync("perez", 10);

        r.Items.Should().BeEmpty();
        r.Modulos.Should().BeEmpty();
    }
}
