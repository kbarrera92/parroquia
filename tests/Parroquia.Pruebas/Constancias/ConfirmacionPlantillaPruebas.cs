using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;

namespace Parroquia.Pruebas.Constancias;

/// <summary>
/// Golden de la constancia de Confirmación contra docs/referencias/constancia_confirmacion.jpeg.
/// Diferencias conscientes con el papel: "Confirmación" con tilde (decisión 5) y fechas con "de" (decisión 20).
/// Datos del ejemplo (permitidos por CLAUDE.md regla 3).
/// </summary>
public class ConfirmacionPlantillaPruebas
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

    private static DatosConfirmacion Ejemplo() => new(
        Nombres: "Juan Carlos",
        Apellidos: "Hidalgo Arevalo",
        FechaConfirmacion: FechaParcial.Crear(2026, 10, 17, Hoy),
        Padrinos: ["Marvin Herrera"],
        Libro: "1",
        Folio: "349",
        Revisar: false);

    private static string Esperado() =>
        Normalizar(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Constancias", "Golden", "confirmacion.txt")));

    private static string Normalizar(string texto) => texto.Replace("\r\n", "\n").TrimEnd('\n');

    private static string Texto(DatosConfirmacion d) => PlantillaConfirmacion.Render(d, Contexto).ATextoPlano();

    [Fact]
    public void Texto_coincide_con_el_ejemplo_caracter_por_caracter()
    {
        Normalizar(Texto(Ejemplo())).Should().Be(Esperado());
    }

    [Fact]
    public void Titulo_y_estilos_siguen_el_ejemplo()
    {
        var documento = PlantillaConfirmacion.Render(Ejemplo(), Contexto);

        documento.Titulo.Should().Be("CERTIFICACIÓN DE CONFIRMACIÓN");

        var nombre = documento.Cuerpo.Single(p => p.ATexto() == "JUAN CARLOS HIDALGO AREVALO");
        nombre.Segmentos.Should().OnlyContain(s => s.Negrita);

        var fecha = documento.Cuerpo.Single(p => p.ATexto() == "17 de octubre de 2026");
        fecha.Segmentos.Should().OnlyContain(s => s.Negrita && !s.Subrayado);

        var padrinos = documento.Cuerpo.Single(p => p.ATexto() == "Marvin Herrera");
        padrinos.Segmentos.Should().OnlyContain(s => s.Negrita);

        var consta = documento.Cuerpo.Single(p => p.ATexto().StartsWith("Según consta"));
        consta.Segmentos.Should().OnlyContain(s => s.Negrita);

        documento.Cierre.Alineacion.Should().Be(Alineacion.Izquierda);
        documento.Firmante.Alineacion.Should().Be(Alineacion.Derecha);
    }

    [Fact]
    public void Dos_padrinos_se_unen_con_y()
    {
        Texto(Ejemplo() with { Padrinos = ["Marvin Herrera", "Ana Paz"] })
            .Should().Contain("Sus padrinos fueron:\nMarvin Herrera y Ana Paz\n");
    }

    [Fact]
    public void Tres_o_mas_padrinos_se_separan_con_comas_y_una_y_final()
    {
        Texto(Ejemplo() with { Padrinos = ["Ana Paz", "Luis Mena", "Rosa Díaz"] })
            .Should().Contain("Sus padrinos fueron:\nAna Paz, Luis Mena y Rosa Díaz\n");
    }

    [Fact]
    public void Padrinos_vacios_se_ignoran()
    {
        Texto(Ejemplo() with { Padrinos = ["Ana Paz", " ", "Luis  Mena"] })
            .Should().Contain("Sus padrinos fueron:\nAna Paz y Luis Mena\n");
    }

    [Fact]
    public void Sin_padrinos_deja_la_linea_en_blanco()
    {
        Texto(Ejemplo() with { Padrinos = [] })
            .Should().Contain("Sus padrinos fueron:\n\nSegún consta");
    }

    [Theory]
    [InlineData(2026, null, null, "2026")]
    [InlineData(2026, 10, null, "octubre de 2026")]
    public void Fecha_parcial_imprime_lo_que_existe(int anio, int? mes, int? dia, string esperado)
    {
        var fecha = FechaParcial.Crear((short)anio, (short?)mes, (short?)dia, Hoy);

        Texto(Ejemplo() with { FechaConfirmacion = fecha })
            .Should().Contain($"el día:\n{esperado}\n");
    }

    [Fact]
    public void Nombre_en_mayusculas_conserva_tildes_del_libro()
    {
        var texto = Texto(Ejemplo() with { Nombres = "José Ángel", Apellidos = "Peña  Muñoz" });

        texto.Should().Contain("\nJOSÉ ÁNGEL PEÑA MUÑOZ\n");
    }

    [Fact]
    public void Libro_y_folio_alfanumericos_se_imprimen_tal_cual()
    {
        Texto(Ejemplo() with { Libro = " 2-A ", Folio = "10 bis" })
            .Should().Contain("Según consta en el libro de Confirmación No. 2-A folio 10 bis\n");
    }

    [Fact]
    public void Sin_problemas_no_hay_advertencias()
    {
        PlantillaConfirmacion.Advertencias(Ejemplo()).Should().BeEmpty();
    }

    [Fact]
    public void Advierte_datos_faltantes_sin_marcar_el_texto()
    {
        var datos = Ejemplo() with
        {
            Nombres = "",
            FechaConfirmacion = FechaParcial.Crear(2026, null, null, Hoy),
            Padrinos = [],
            Libro = null,
            Folio = " ",
            Revisar = true,
        };

        var advertencias = PlantillaConfirmacion.Advertencias(datos);

        advertencias.Should().HaveCount(6);
        advertencias.Should().Contain(a => a.Contains("nombre"));
        advertencias.Should().Contain(a => a.Contains("fecha de la confirmación"));
        advertencias.Should().Contain(a => a.Contains("padrinos"));
        advertencias.Should().Contain(a => a.Contains("libro"));
        advertencias.Should().Contain(a => a.Contains("folio"));
        advertencias.Should().Contain(a => a.Contains("revisar"));

        PlantillaConfirmacion.Render(datos, Contexto).ATextoPlano().Should().NotContain("revisar");
    }
}
