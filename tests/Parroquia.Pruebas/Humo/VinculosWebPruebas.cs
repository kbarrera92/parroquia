using System.Net;
using System.Text.RegularExpressions;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class VinculosWebPruebas(AppFixture app)
{
    private static string Unico() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<long> CrearComunionAsync(HttpClient c, string nombres, string apellidos)
    {
        var r = await AppFixture.PostFormAsync(c, "/Comunion/Editar", "/Comunion/Editar", new()
        {
            ["Input.Libro"] = "L" + Unico(), ["Input.Folio"] = "1", ["Input.Partida"] = "1",
            ["Input.Nombres"] = nombres, ["Input.Apellidos"] = apellidos, ["Input.Fecha"] = "2/10/2004",
            ["Input.Ministro"] = "", ["Input.NotasMarginales"] = "", ["Input.Revisar"] = "false",
            ["Input.MotivoRevision"] = "", ["Input.Version"] = "0", ["accion"] = "guardar",
        });
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return long.Parse(Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);
    }

    private static async Task<long> CrearConfirmacionAsync(HttpClient c, string nombres, string apellidos)
    {
        var r = await AppFixture.PostFormAsync(c, "/Confirmacion/Editar", "/Confirmacion/Editar", new()
        {
            ["Input.Libro"] = "L" + Unico(), ["Input.Folio"] = "1", ["Input.Partida"] = "1",
            ["Input.Nombres"] = nombres, ["Input.Apellidos"] = apellidos, ["Input.FechaConfirmacion"] = "5/6/2005",
            ["Input.Ministro"] = "", ["Input.NotasMarginales"] = "", ["Input.Revisar"] = "false",
            ["Input.MotivoRevision"] = "", ["Input.Version"] = "0", ["accion"] = "guardar",
        });
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return long.Parse(Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);
    }

    private static async Task<string> HtmlAsync(HttpClient c, string url) =>
        WebUtility.HtmlDecode(await (await c.GetAsync(url)).Content.ReadAsStringAsync());

    [Fact]
    public async Task Capturista_vincula_ve_la_cadena_y_desvincula()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var comunion = await CrearComunionAsync(c, "Ana", "Vinculada" + u);
        var confirmacion = await CrearConfirmacionAsync(c, "Ana", "Vinculada" + u);

        // La búsqueda de candidatos excluye al propio registro y ofrece el otro libro.
        var buscar = $"/Vinculos/comunion/{comunion}?handler=Buscar&texto=vinculada{u}";
        var candidatos = await HtmlAsync(c, buscar);
        candidatos.Should().Contain("Confirmación").And.NotContain("Primera Comunión");

        var vincular = await AppFixture.PostFormAsync(c, buscar, $"/Vinculos/comunion/{comunion}?handler=Vincular", new()
        {
            ["tipo"] = "confirmacion", ["idDestino"] = confirmacion.ToString(), ["personaDestino"] = "",
        });
        var html = WebUtility.HtmlDecode(await vincular.Content.ReadAsStringAsync());
        vincular.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Registros vinculados").And.Contain($"/Confirmacion/Detalle/{confirmacion}");

        // Ya vinculados: no aparecen como candidatos y el detalle del otro lado los muestra.
        (await HtmlAsync(c, buscar)).Should().NotContain("Es la misma persona");
        (await HtmlAsync(c, $"/Vinculos/confirmacion/{confirmacion}")).Should().Contain($"/Comunion/Detalle/{comunion}");

        var vinculoId = Regex.Match(html, @"name=""vinculoId"" value=""(\d+)""").Groups[1].Value;
        var quitar = await AppFixture.PostFormAsync(c, $"/Vinculos/comunion/{comunion}",
            $"/Vinculos/comunion/{comunion}?handler=Desvincular", new() { ["vinculoId"] = vinculoId });
        WebUtility.HtmlDecode(await quitar.Content.ReadAsStringAsync()).Should().Contain("Vínculo quitado");
        (await HtmlAsync(c, $"/Vinculos/comunion/{comunion}")).Should().Contain("No hay registros vinculados");
    }

    [Fact]
    public async Task El_detalle_carga_la_seccion_de_vinculos()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var id = await CrearComunionAsync(c, "Ana", "Detalle" + Unico());

        var html = await HtmlAsync(c, $"/Comunion/Detalle/{id}");

        html.Should().Contain($"hx-get=\"/Vinculos/comunion/{id}\"");
    }

    [Fact]
    public async Task Secretaria_ve_los_vinculos_pero_no_puede_vincular_ni_buscar()
    {
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearComunionAsync(capturista, "Ana", "Solo" + u);

        var secretaria = await app.ClienteConRolAsync(Roles.Secretaria);
        var vista = await HtmlAsync(secretaria, $"/Vinculos/comunion/{id}");
        vista.Should().Contain("No hay registros vinculados").And.NotContain("Vincular con otro registro");

        var buscar = await secretaria.GetAsync($"/Vinculos/comunion/{id}?handler=Buscar&texto=solo{u}");
        buscar.StatusCode.Should().Be(HttpStatusCode.Redirect);
        buscar.Headers.Location!.ToString().Should().Contain("AccesoDenegado");

        var post = await AppFixture.PostFormAsync(secretaria, "/Cuenta/Logout", $"/Vinculos/comunion/{id}?handler=Vincular", new()
        {
            ["tipo"] = "comunion", ["idDestino"] = "1", ["personaDestino"] = "",
        });
        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Contain("AccesoDenegado");
    }

    [Fact]
    public async Task Registro_inexistente_o_libro_desconocido_da_404()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        (await c.GetAsync("/Vinculos/comunion/999999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync("/Vinculos/noexiste/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.GetAsync("/Vinculos/comunion/1/esposo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
