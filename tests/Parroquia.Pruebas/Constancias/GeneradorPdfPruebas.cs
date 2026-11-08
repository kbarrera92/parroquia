using System.Text;
using Parroquia.Aplicacion.Constancias;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Comun;
using Parroquia.Infraestructura.Constancias;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Parroquia.Pruebas.Constancias;

/// <summary>
/// El PDF solo maqueta el modelo de documento: aquí se comprueba que se genera, que incrusta las fuentes propias
/// y que no depende del equipo. El parecido visual con el ejemplo se revisa a ojo (ver variable PARROQUIA_PDF_SALIDA).
/// </summary>
public class GeneradorPdfPruebas
{
    private static readonly ContextoConstancia Contexto = new(
        "6a. Avenida y 7a. Calle zona 1", "Mazatenango, Suchitepéquez Guatemala C.A.", "(502) 7872-0051",
        "Mazatenango", "Mazatenango, Such.", "P. Neil Contreras", "Párroco", new DateOnly(2026, 11, 3), null);

    private static DocumentoConstancia Ejemplo() => PlantillaComunion.Render(
        new DatosComunion("Alejandra Maria", "Penagos Samayoa",
            FechaParcial.Crear(2004, 10, 2, new DateOnly(2026, 11, 7)), "3", "60", false),
        Contexto);

    [Fact]
    public void Genera_un_pdf_valido_de_una_pagina_carta()
    {
        var pdf = new GeneradorPdfConstancia().Generar(Ejemplo(), logo: null);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        pdf.Length.Should().BeGreaterThan(5_000);
        var texto = Encoding.Latin1.GetString(pdf);
        texto.Should().Contain("/Count 1");
        texto.Should().Contain("612").And.Contain("792"); // carta en puntos
    }

    [Fact]
    public void Incrusta_las_fuentes_Liberation_y_no_usa_otras()
    {
        var texto = Encoding.Latin1.GetString(new GeneradorPdfConstancia().Generar(Ejemplo(), null));

        texto.Should().Contain("LiberationSerif").And.Contain("LiberationSans");
        texto.Should().NotContain("Arial").And.NotContain("TimesNewRoman").And.NotContain("Lato");
    }

    [Fact]
    public void Funciona_con_logo_png()
    {
        // PNG de 1x1 píxel, suficiente para probar la ruta con imagen.
        var logo = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        var sin = new GeneradorPdfConstancia().Generar(Ejemplo(), null);
        var con = new GeneradorPdfConstancia().Generar(Ejemplo(), logo);

        con.Length.Should().BeGreaterThan(sin.Length);
    }

    [Fact]
    public void Tolera_nombres_largos_y_caracteres_del_libro_sin_romper_el_documento()
    {
        var datos = new DatosComunion("María de los Ángeles Concepción Ñuñez", "Peña de la Cruz y Muñoz-Álvarez",
            FechaParcial.Crear(1950, null, null, new DateOnly(2026, 11, 7)), "12 bis", "100", false);

        var pdf = new GeneradorPdfConstancia().Generar(PlantillaComunion.Render(datos, Contexto), null);

        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Muestra_visual_opcional_para_revisar_contra_el_ejemplo()
    {
        var carpeta = Environment.GetEnvironmentVariable("PARROQUIA_PDF_SALIDA");
        if (string.IsNullOrWhiteSpace(carpeta)) return; // solo se ejecuta a pedido

        Directory.CreateDirectory(carpeta);
        var generador = new GeneradorPdfConstancia();
        var logo = LogoDeReferencia();
        File.WriteAllBytes(Path.Combine(carpeta, "comunion.pdf"), generador.Generar(Ejemplo(), logo));
        generador.Construir(Ejemplo(), logo).GenerateImages(
            i => Path.Combine(carpeta, $"comunion-{i}.png"),
            new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 110 });
    }

    /// <summary>El logo de docs/referencias, si se encuentra subiendo desde la carpeta de las pruebas.</summary>
    private static byte[]? LogoDeReferencia()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var ruta = Path.Combine(dir.FullName, "docs", "referencias", "logo_parroquia.jpeg");
            if (File.Exists(ruta)) return File.ReadAllBytes(ruta);
        }
        return null;
    }
}
