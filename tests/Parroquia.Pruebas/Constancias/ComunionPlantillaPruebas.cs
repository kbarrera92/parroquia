using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;

namespace Parroquia.Pruebas.Constancias;

/// <summary>
/// Golden de la constancia de Comunión contra docs/referencias/constancia_comunion.jpeg.
/// Única diferencia consciente con el papel: fechas con "de" ("2 de octubre de 2004"), decisión del 7-nov-2026.
/// Datos del ejemplo (permitidos por CLAUDE.md regla 3).
/// </summary>
public class ComunionPlantillaPruebas
{
    private static readonly ContextoConstancia Contexto = new(
        Direccion: "6a. Avenida y 7a. Calle zona 1",
        Municipio: "Mazatenango, Suchitepéquez Guatemala C.A.",
        Telefono: "(502) 7872-0051",
        CiudadAbreviada: "Mazatenango",
        TextoLugarEmision: "Mazatenango, Such.",
        FirmanteNombre: "P. Neil Contreras",
        FirmanteTitulo: "Párroco",
        FechaEmision: new DateOnly(2026, 11, 3),
        Logo: null);

    private static DatosComunion Ejemplo(FechaParcial? fecha = null) => new(
        Nombres: "Alejandra Maria",
        Apellidos: "Penagos Samayoa",
        FechaComunion: fecha ?? FechaParcial.Crear(2004, 10, 2, new DateOnly(2026, 11, 7)),
        Libro: "3",
        Folio: "60",
        Revisar: false);

    private static string Esperado() =>
        Normalizar(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Constancias", "Golden", "comunion.txt")));

    private static string Normalizar(string texto) => texto.Replace("\r\n", "\n").TrimEnd('\n');

    [Fact]
    public void Texto_coincide_con_el_ejemplo_caracter_por_caracter()
    {
        var documento = PlantillaComunion.Render(Ejemplo(), Contexto);

        Normalizar(documento.ATextoPlano()).Should().Be(Esperado());
    }

    [Fact]
    public void Titulo_y_estilos_siguen_el_ejemplo()
    {
        var documento = PlantillaComunion.Render(Ejemplo(), Contexto);

        documento.Titulo.Should().Be("CERTIFICACIÓN DE COMUNIÓN");
        var nombre = documento.Cuerpo.Single(p => p.ATexto() == "ALEJANDRA MARIA PENAGOS SAMAYOA");
        nombre.Segmentos.Should().OnlyContain(s => s.Negrita);
        nombre.LineaInferior.Should().BeTrue();
        var fecha = documento.Cuerpo.Single(p => p.ATexto() == "2 de octubre de 2004");
        fecha.Segmentos.Should().OnlyContain(s => s.Negrita && s.Subrayado);
        documento.Cierre.Alineacion.Should().Be(Alineacion.Izquierda);
        documento.Firmante.Alineacion.Should().Be(Alineacion.Derecha);
    }

    [Theory]
    [InlineData(2004, null, null, "2004")]
    [InlineData(2004, 10, null, "octubre de 2004")]
    public void Fecha_parcial_imprime_lo_que_existe(int anio, int? mes, int? dia, string esperado)
    {
        var fecha = FechaParcial.Crear((short)anio, (short?)mes, (short?)dia, new DateOnly(2026, 11, 7));

        var texto = PlantillaComunion.Render(Ejemplo(fecha), Contexto).ATextoPlano();

        texto.Should().Contain($"el día:\n{esperado}\n");
    }

    [Fact]
    public void Nombre_en_mayusculas_conserva_tildes_del_libro()
    {
        var datos = Ejemplo() with { Nombres = "José Ángel", Apellidos = "Peña  Muñoz" };

        var texto = PlantillaComunion.Render(datos, Contexto).ATextoPlano();

        texto.Should().Contain("\nJOSÉ ÁNGEL PEÑA MUÑOZ\n");
    }

    [Fact]
    public void Sin_problemas_no_hay_advertencias()
    {
        PlantillaComunion.Advertencias(Ejemplo()).Should().BeEmpty();
    }

    [Fact]
    public void Advierte_datos_faltantes_sin_marcar_el_texto()
    {
        var datos = Ejemplo(FechaParcial.Crear(2004, null, null, new DateOnly(2026, 11, 7))) with
        {
            Nombres = "",
            Folio = " ",
            Revisar = true,
        };

        var advertencias = PlantillaComunion.Advertencias(datos);

        advertencias.Should().HaveCount(4);
        advertencias.Should().Contain(a => a.Contains("nombre"));
        advertencias.Should().Contain(a => a.Contains("fecha"));
        advertencias.Should().Contain(a => a.Contains("folio"));
        advertencias.Should().Contain(a => a.Contains("revisar"));

        PlantillaComunion.Render(datos, Contexto).ATextoPlano().Should().NotContain("revisar");
    }
}
