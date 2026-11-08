using System.Reflection;
using Parroquia.Aplicacion.Constancias;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Parroquia.Infraestructura.Constancias;

/// <summary>
/// Maqueta un <see cref="DocumentoConstancia"/> en PDF (hoja carta en blanco, monocromo). No decide texto alguno:
/// todo el contenido viene del modelo que produce la plantilla del sacramento. Sirve para todos los sacramentos.
/// </summary>
public sealed class GeneradorPdfConstancia
{
    public const string FuenteMembrete = "Liberation Serif";
    public const string FuenteCuerpo = "Liberation Sans";

    private static readonly string[] Fuentes =
    [
        "Fuentes.LiberationSerif-Bold.ttf",
        "Fuentes.LiberationSans-Regular.ttf",
        "Fuentes.LiberationSans-Bold.ttf",
    ];

    static GeneradorPdfConstancia()
    {
        // Community: la parroquia es una organización de beneficio público (ver docs/04, ADR-06).
        QuestPDF.Settings.License = LicenseType.Community;

        // Solo fuentes incrustadas: el PDF sale igual en cualquier equipo y falla fuerte si falta una.
        QuestPDF.Settings.UseSystemFonts = false;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = true;
        var ensamblado = typeof(GeneradorPdfConstancia).Assembly;
        foreach (var recurso in Fuentes) FontManager.RegisterFontFromEmbeddedResource(ensamblado, recurso);
    }

    public byte[] Generar(DocumentoConstancia documento, byte[]? logo) => Construir(documento, logo).GeneratePdf();

    public IDocument Construir(DocumentoConstancia d, byte[]? logo) => Document.Create(contenedor =>
    {
        contenedor.Page(pagina =>
        {
            pagina.Size(PageSizes.Letter);
            pagina.Margin(2, Unit.Centimetre);
            pagina.DefaultTextStyle(t => t.FontFamily(FuenteCuerpo).FontSize(12.5f).FontColor(Colors.Black));

            pagina.Content().Column(col =>
            {
                col.Item().Element(c => Membrete(c, d, logo));

                col.Item().PaddingTop(16).AlignCenter().MinWidth(250).Border(1).CornerRadius(7)
                    .PaddingVertical(5).PaddingHorizontal(26).AlignCenter()
                    .Text(d.Titulo).Bold().FontSize(18);

                col.Item().PaddingTop(10).Column(cuerpo =>
                {
                    cuerpo.Spacing(8);
                    foreach (var p in d.Cuerpo) cuerpo.Item().Element(c => Parrafo(c, p));
                    cuerpo.Item().Element(c => Parrafo(c, d.Cierre));
                    cuerpo.Item().Element(c => Firma(c, d));
                });
            });
        });
    }).WithMetadata(new DocumentMetadata { Title = d.Titulo });

    private static void Membrete(IContainer c, DocumentoConstancia d, byte[]? logo)
    {
        c.Column(col =>
        {
            col.Item().Row(fila =>
            {
                fila.ConstantItem(125).AlignMiddle().Element(l =>
                {
                    if (logo is { Length: > 0 }) l.Image(logo).FitWidth();
                });

                fila.RelativeItem().PaddingLeft(10).AlignMiddle().Column(lineas =>
                {
                    foreach (var linea in d.Membrete)
                        lineas.Item().Text(linea).FontFamily(FuenteMembrete).Bold().FontSize(11.5f);
                });
            });

            // Línea doble bajo el membrete, como en el ejemplo.
            col.Item().PaddingTop(8).LineHorizontal(1.2f);
            col.Item().PaddingTop(2.5f).LineHorizontal(0.8f);
        });
    }

    private static void Parrafo(IContainer c, Parrafo p)
    {
        var caja = c.PaddingTop(p.EspacioAntes);
        if (p.LineaInferior) caja = caja.PaddingHorizontal(36).BorderBottom(1).PaddingBottom(2);

        if (p.Rotulo is null)
        {
            caja.Text(t => Segmentos(t, p));
            return;
        }

        // Fila "rótulo: valor": el valor arranca siempre en la misma columna.
        caja.Row(fila =>
        {
            fila.ConstantItem(AnchoRotulo).Text(p.Rotulo);
            fila.RelativeItem().Text(t => Segmentos(t, p));
        });
    }

    /// <summary>Ancho (pt) de la columna de rótulos de las filas "rótulo: valor".</summary>
    private const float AnchoRotulo = 130;

    private static void Segmentos(TextDescriptor t, Parrafo p)
    {
        Alinear(t, p.Alineacion);
        foreach (var s in p.Segmentos)
        {
            var span = t.Span(s.Texto);
            if (s.Negrita) span.Bold();
            if (s.Subrayado) span.Underline();
        }
    }

    /// <summary>Nombre y cargo del firmante, a la derecha, con aire arriba para la firma y el sello manuales.</summary>
    private static void Firma(IContainer c, DocumentoConstancia d)
    {
        c.PaddingTop(d.Firmante.EspacioAntes).AlignRight().Column(col =>
        {
            col.Item().AlignCenter().Text(d.Firmante.ATexto()).Bold();
            col.Item().AlignCenter().Text(d.TituloFirmante).Bold();
        });
    }

    private static void Alinear(TextDescriptor t, Alineacion a)
    {
        switch (a)
        {
            case Alineacion.Izquierda: t.AlignLeft(); break;
            case Alineacion.Derecha: t.AlignRight(); break;
            default: t.AlignCenter(); break;
        }
    }
}
