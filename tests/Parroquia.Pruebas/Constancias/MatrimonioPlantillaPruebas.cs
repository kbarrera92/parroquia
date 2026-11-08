using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;

namespace Parroquia.Pruebas.Constancias;

/// <summary>
/// Golden de la constancia de Matrimonio contra docs/referencias/constancia_matrimonio.jpeg.
/// Diferencias conscientes con el papel: "ante los testigos" y "San Bartolomé" corregidos (decisión 5),
/// fechas del sacramento con "de" (decisión 20) y la errata "parriquia" no se reproduce.
/// Datos del ejemplo (permitidos por CLAUDE.md regla 3).
/// </summary>
public class MatrimonioPlantillaPruebas
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
        FechaEmision: new DateOnly(2026, 11, 6),
        Logo: null);

    private static DatosMatrimonio Ejemplo() => new(
        Esposo: new DatosContrayente("don", "Carlos Genaro", "Alvarado de León", 29,
            "San Gabriel", "P. San Bartolome", "Samuel Alvarado", "Estefana de León Pérez"),
        Esposa: new DatosContrayente("la Srita.", "Gloria Inés", "Ceballos Tupul", 22,
            "San Gabriel", "parroquia San Bartolome", "Bonifacio Ceballos", "Concepcion Tupul"),
        FechaMatrimonio: FechaParcial.Crear(2007, 4, 21, Hoy),
        LugarCelebracion: "Iglesia de Mazatenango, Suchitepéquez",
        Testigos: ["Noe Lopez Garcia", "Alma Luna de Lopez"],
        Ministro: "P. Andrés Martínez",
        NotasMarginales: null,
        Libro: "18", Folio: "5", Partida: "15", Revisar: false);

    private static string Esperado() =>
        Normalizar(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Constancias", "Golden", "matrimonio.txt")));

    private static string Normalizar(string texto) => texto.Replace("\r\n", "\n").TrimEnd('\n');

    private static string Texto(DatosMatrimonio d) => PlantillaMatrimonio.Render(d, Contexto).ATextoPlano();

    [Fact]
    public void Texto_coincide_con_el_ejemplo_caracter_por_caracter()
    {
        Normalizar(Texto(Ejemplo())).Should().Be(Esperado());
    }

    [Fact]
    public void Titulo_estilos_y_linea_de_firma_siguen_el_ejemplo()
    {
        var documento = PlantillaMatrimonio.Render(Ejemplo(), Contexto);

        documento.Titulo.Should().Be("CERTIFICACIÓN DE MATRIMONIO");
        documento.LineaFirma.Should().BeTrue();
        documento.Firmante.Alineacion.Should().Be(Alineacion.Derecha);

        var dice = documento.Cuerpo.Single(p => p.ATexto().StartsWith("<<En la"));
        dice.Segmentos.Should().Contain(s => s.Negrita && s.Texto == "Carlos Genaro Alvarado de León");
        dice.Segmentos.Should().Contain(s => s.Subrayado && s.Texto == "21 de abril de 2007");
    }

    [Fact]
    public void Fecha_de_cierre_es_dd_MM_yyyy()
    {
        var d = PlantillaMatrimonio.Render(Ejemplo(), Contexto with { FechaEmision = new DateOnly(2026, 3, 9) });

        d.ATextoPlano().Should().Contain("a los: 09/03/2026\n");
    }

    [Theory]
    [InlineData(2007, null, null, "en fecha: 2007 previo")]
    [InlineData(2007, 4, null, "en fecha: abril de 2007 previo")]
    public void Fecha_parcial_imprime_lo_que_existe(int anio, int? mes, int? dia, string esperado)
    {
        var fecha = FechaParcial.Crear((short)anio, (short?)mes, (short?)dia, Hoy);

        Texto(Ejemplo() with { FechaMatrimonio = fecha }).Should().Contain(esperado);
    }

    [Fact]
    public void Un_solo_testigo_y_un_solo_progenitor_no_llevan_y()
    {
        var datos = Ejemplo() with
        {
            Testigos = ["Noe Lopez Garcia"],
            Esposo = Ejemplo().Esposo with { Madre = null },
        };

        var texto = Texto(datos);

        texto.Should().Contain("ante los testigos: Noe Lopez Garcia contrajo don:");
        texto.Should().Contain("hijo legítimo de: Samuel Alvarado con la Srita.");
    }

    [Fact]
    public void Tres_testigos_se_separan_con_comas_y_una_y_final()
    {
        Texto(Ejemplo() with { Testigos = ["Ana Paz", "Luis Mena", "Rosa Díaz"] })
            .Should().Contain("testigos: Ana Paz, Luis Mena y Rosa Díaz contrajo");
    }

    [Fact]
    public void Sin_tratamiento_no_deja_dos_puntos_sueltos()
    {
        var datos = Ejemplo() with
        {
            Esposo = Ejemplo().Esposo with { Tratamiento = null },
            Esposa = Ejemplo().Esposa with { Tratamiento = " " },
        };

        var texto = Texto(datos);

        texto.Should().Contain(" contrajo Carlos Genaro Alvarado de León de 29 años");
        texto.Should().Contain(" con Gloria Inés Ceballos Tupul de 22 años");
    }

    [Fact]
    public void Sin_edad_se_omite_el_fragmento_de_N_años()
    {
        var datos = Ejemplo() with { Esposa = Ejemplo().Esposa with { Edad = null } };

        Texto(datos).Should().Contain("Gloria Inés Ceballos Tupul, originaria de: San Gabriel");
    }

    [Fact]
    public void Notas_marginales_reemplazan_sin_anotaciones()
    {
        Texto(Ejemplo() with { NotasMarginales = "  Se anuló el 3/5/2010.  " })
            .Should().Contain("Al margen se lee: \"Se anuló el 3/5/2010.\">>");
    }

    [Fact]
    public void Nombres_se_imprimen_tal_cual_el_libro()
    {
        var datos = Ejemplo() with
        {
            Esposo = Ejemplo().Esposo with { Nombres = "JOSÉ ángel", Apellidos = "Peña  Muñoz" },
        };

        Texto(datos).Should().Contain("contrajo don: JOSÉ ángel Peña Muñoz de 29 años");
    }

    [Fact]
    public void Libro_folio_y_partida_alfanumericos_se_imprimen_tal_cual()
    {
        Texto(Ejemplo() with { Libro = " 2-A ", Folio = "10 bis", Partida = "7 A" })
            .Should().Contain("libro de Matrimonio #: 2-A en el folio #: 10 bis, de esta parroquia")
            .And.Contain("la partida No. 7 A que literalmente dice:");
    }

    [Fact]
    public void Lugar_vacio_usa_el_de_la_parroquia()
    {
        Texto(Ejemplo() with { LugarCelebracion = null })
            .Should().Contain("<<En la Iglesia de Mazatenango, Suchitepéquez en fecha:");
    }

    [Fact]
    public void Sin_problemas_no_hay_advertencias()
    {
        PlantillaMatrimonio.Advertencias(Ejemplo()).Should().BeEmpty();
    }

    [Fact]
    public void Advierte_datos_faltantes_sin_marcar_el_texto()
    {
        var vacio = new DatosContrayente(null, "", "", null, null, null, null, null);
        var datos = Ejemplo() with
        {
            Esposo = vacio,
            Esposa = Ejemplo().Esposa with { Edad = null, Padre = null, Madre = null },
            FechaMatrimonio = FechaParcial.Crear(2007, null, null, Hoy),
            Testigos = [],
            Ministro = null,
            Libro = null, Folio = " ", Partida = null,
            Revisar = true,
        };

        var avisos = PlantillaMatrimonio.Advertencias(datos);

        avisos.Should().Contain(a => a.Contains("nombre") && a.Contains("esposo"));
        avisos.Should().NotContain(a => a.Contains("nombre") && a.Contains("esposa"));
        avisos.Should().Contain(a => a.Contains("edad") && a.Contains("esposo"));
        avisos.Should().Contain(a => a.Contains("edad") && a.Contains("esposa"));
        avisos.Should().Contain(a => a.Contains("lugar de origen") && a.Contains("esposo"));
        avisos.Should().Contain(a => a.Contains("feligresía") && a.Contains("esposo"));
        avisos.Should().Contain(a => a.Contains("padres") && a.Contains("esposo"));
        avisos.Should().Contain(a => a.Contains("padres") && a.Contains("esposa"));
        avisos.Should().Contain(a => a.Contains("fecha del matrimonio"));
        avisos.Should().Contain(a => a.Contains("testigos"));
        avisos.Should().Contain(a => a.Contains("ministro"));
        avisos.Should().Contain(a => a.Contains("libro"));
        avisos.Should().Contain(a => a.Contains("folio"));
        avisos.Should().Contain(a => a.Contains("partida"));
        avisos.Should().Contain(a => a.Contains("revisar"));

        PlantillaMatrimonio.Render(datos, Contexto).ATextoPlano().Should().NotContain("revisar");
    }
}
