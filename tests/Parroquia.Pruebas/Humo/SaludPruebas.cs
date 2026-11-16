using System.Net;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class SaludPruebas(AppFixture app)
{
    [Fact]
    public async Task Salud_responde_sin_sesion_y_sin_datos()
    {
        var r = await app.Cliente().GetAsync("/salud");
        var cuerpo = await r.Content.ReadAsStringAsync();

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        cuerpo.Should().Be("Healthy");
    }
}
