using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;

namespace Parroquia.Pruebas.Constancias;

/// <summary>
/// Golden de la constancia de Bautismo contra docs/referencias/constancia_bautismo.jpeg.
/// Diferencias conscientes con el papel: nombre sin rótulo (decisión 4), fechas con "de" (decisión 20)
/// y "Nació" con tilde. Datos del ejemplo (permitidos por CLAUDE.md regla 3).
/// </summary>
public class BautismoPlantillaPruebas
{
    private static readonly DateOnly Hoy = new(2026, 11, 7);

    private static readonly ContextoConstancia Contexto = new(
        Direccion: "6a. Avenida y 7a. Calle zona 1",
        Municipio: "Mazatenango, Suchitepéquez Guatemala C.A.",
        Telefono: "(502) 7872-0051",
        CiudadAbreviada: "Mazatenango",
        TextoLugarEmision: "Mazatenango, Such.",
        FirmanteNombre: "P. Neil Contreras",
        FirmanteTitulo: "Párroco",
        FechaEmision: new DateOnly(2026, 11, 4),
        Logo: null);

    private static DatosBautismo Ejemplo() => new(
        Nombres: "Juan Carlos",
        Apellidos: "Hidalgo Arevalo",
        FechaBautismo: FechaParcial.Crear(1978, 8, 13, Hoy),
        FechaNacimiento: FechaParcial.Crear(1977, 1, 5, Hoy),
        Padre: "Carlos B. Hidalgo",
        Madre: "Josefa Arévalo",
        Padrinos: ["Gildardo Alvarado", "Sonia de Alvarado"],
        Ministro: "P. Julio Rodriguez",
        NotasMarginales: null,
        Libro: "80",
        Folio: "388",
        Revisar: false);

    private static string Esperado() =>
        Normalizar(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Constancias", "Golden", "bautismo.txt")));

    private static string Normalizar(string texto) => texto.Replace("\r\n", "\n").TrimEnd('\n');

    private static string Texto(DatosBautismo d) => PlantillaBautismo.Render(d, Contexto).ATextoPlano();

    [Fact]
    public void Texto_coincide_con_el_ejemplo_caracter_por_caracter()
    {
        Normalizar(Texto(Ejemplo())).Should().Be(Esperado());
    }

    [Fact]
    public void Titulo_y_estilos_siguen_el_ejemplo()
    {
        var documento = PlantillaBautismo.Render(Ejemplo(), Contexto);

        documento.Titulo.Should().Be("CERTIFICACIÓN DE BAUTISMO");

        var nombre = documento.Cuerpo.Single(p => p.ATexto() == "JUAN CARLOS HIDALGO AREVALO");
        nombre.Segmentos.Should().OnlyContain(s => s.Negrita);
        nombre.Alineacion.Should().Be(Alineacion.Centro);
        nombre.Rotulo.Should().BeNull("el nombre va sin rótulo");

        var filas = documento.Cuerpo.Where(p => p.Rotulo is not null).ToList();
        filas.Select(p => p.Rotulo).Should().Equal(
            "Bautizado el día:", "hijo (a) de:", "Sus padrinos fueron:", "Firma de ministro:", "Al margen se lee:");
        filas.Should().OnlyContain(p => p.Alineacion == Alineacion.Izquierda);
        filas.SelectMany(p => p.Segmentos).Should().OnlyContain(s => s.Subrayado);

        documento.Cierre.Alineacion.Should().Be(Alineacion.Izquierda);
        documento.Firmante.Alineacion.Should().Be(Alineacion.Derecha);
    }

    [Fact]
    public void Sin_notas_marginales_imprime_sin_anotaciones_y_con_notas_las_imprime()
    {
        Texto(Ejemplo() with { NotasMarginales = "  " }).Should().Contain("Al margen se lee: Sin anotaciones");
        Texto(Ejemplo() with { NotasMarginales = "Se casó en 2001" }).Should().Contain("Al margen se lee: Se casó en 2001");
    }

    [Fact]
    public void Un_solo_progenitor_o_padrino_no_imprime_la_y()
    {
        var texto = Texto(Ejemplo() with { Madre = null, Padrinos = ["Gildardo Alvarado"] });

        texto.Should().Contain("hijo (a) de: Carlos B. Hidalgo\n");
        texto.Should().Contain("Sus padrinos fueron: Gildardo Alvarado\n");

        Texto(Ejemplo() with { Padre = " ", Madre = "Josefa Arévalo" })
            .Should().Contain("hijo (a) de: Josefa Arévalo\n");
    }

    [Fact]
    public void Tres_o_mas_padrinos_se_separan_con_comas_y_una_y_final()
    {
        var texto = Texto(Ejemplo() with { Padrinos = ["Ana Paz", "Luis Mena", "Rosa Díaz"] });

        texto.Should().Contain("Sus padrinos fueron: Ana Paz, Luis Mena y Rosa Díaz\n");
    }

    [Fact]
    public void Padrinos_vacios_se_ignoran()
    {
        var texto = Texto(Ejemplo() with { Padrinos = ["Ana Paz", " ", "Luis  Mena"] });

        texto.Should().Contain("Sus padrinos fueron: Ana Paz y Luis Mena\n");
    }

    [Theory]
    [InlineData(1978, null, null, "1978")]
    [InlineData(1978, 8, null, "agosto de 1978")]
    public void Fecha_parcial_imprime_lo_que_existe(int anio, int? mes, int? dia, string esperado)
    {
        var fecha = FechaParcial.Crear((short)anio, (short?)mes, (short?)dia, Hoy);

        Texto(Ejemplo() with { FechaBautismo = fecha })
            .Should().Contain($"Bautizado el día: {esperado}, Nació el 5 de enero de 1977\n");
    }

    [Fact]
    public void Sin_fecha_de_nacimiento_solo_imprime_la_del_bautismo()
    {
        var texto = Texto(Ejemplo() with { FechaNacimiento = FechaParcial.Vacia });

        texto.Should().Contain("Bautizado el día: 13 de agosto de 1978\n");
        texto.Should().NotContain("Nació");
    }

    [Fact]
    public void Nombre_en_mayusculas_conserva_tildes_del_libro()
    {
        var texto = Texto(Ejemplo() with { Nombres = "José Ángel", Apellidos = "Peña  Muñoz" });

        texto.Should().Contain("\nJOSÉ ÁNGEL PEÑA MUÑOZ\n");
    }

    [Fact]
    public void Sin_problemas_no_hay_advertencias()
    {
        PlantillaBautismo.Advertencias(Ejemplo()).Should().BeEmpty();
    }

    [Fact]
    public void Advierte_datos_faltantes_sin_marcar_el_texto()
    {
        var datos = Ejemplo() with
        {
            Nombres = "",
            FechaBautismo = FechaParcial.Crear(1978, null, null, Hoy),
            FechaNacimiento = FechaParcial.Vacia,
            Padre = null,
            Madre = null,
            Padrinos = [],
            Ministro = " ",
            Folio = " ",
            Revisar = true,
        };

        var advertencias = PlantillaBautismo.Advertencias(datos);

        advertencias.Should().HaveCount(8);
        advertencias.Should().Contain(a => a.Contains("nombre"));
        advertencias.Should().Contain(a => a.Contains("fecha del bautismo"));
        advertencias.Should().Contain(a => a.Contains("nacimiento"));
        advertencias.Should().Contain(a => a.Contains("padres"));
        advertencias.Should().Contain(a => a.Contains("padrinos"));
        advertencias.Should().Contain(a => a.Contains("ministro"));
        advertencias.Should().Contain(a => a.Contains("folio"));
        advertencias.Should().Contain(a => a.Contains("revisar"));

        PlantillaBautismo.Render(datos, Contexto).ATextoPlano().Should().NotContain("revisar");
    }
}
