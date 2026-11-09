using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Pruebas.Seguridad;

public class GeneradorContrasenaTemporalPruebas
{
    [Fact]
    public void Genera_16_caracteres_sin_ambiguos()
    {
        for (var i = 0; i < 200; i++)
        {
            var p = GeneradorContrasenaTemporal.Generar();

            p.Should().HaveLength(GeneradorContrasenaTemporal.Longitud);
            p.Should().MatchRegex("^[A-HJ-NP-Za-km-z2-9]+$");
        }
    }

    [Fact]
    public void Cumple_la_politica_de_contrasenas()
    {
        GeneradorContrasenaTemporal.Longitud.Should().BeGreaterThanOrEqualTo(PoliticaContrasena.LongitudMinima);

        for (var i = 0; i < 200; i++)
            ValidadorContrasenaComun.Evaluar(GeneradorContrasenaTemporal.Generar(), "usuario").Should().BeEmpty();
    }

    [Fact]
    public void No_repite_valores()
    {
        Enumerable.Range(0, 500).Select(_ => GeneradorContrasenaTemporal.Generar())
            .Distinct().Should().HaveCount(500);
    }
}
