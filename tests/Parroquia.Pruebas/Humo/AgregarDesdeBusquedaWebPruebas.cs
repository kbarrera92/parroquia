using System.Net;
using System.Text.RegularExpressions;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

/// <summary>Botón «+» de la búsqueda: sacramentos que faltan, alta precargada y vínculo automático. Datos sintéticos.</summary>
[Collection(AppCollection.Nombre)]
public class AgregarDesdeBusquedaWebPruebas(AppFixture app)
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

    private static async Task<string> HtmlAsync(HttpClient c, string url) =>
        WebUtility.HtmlDecode(await (await c.GetAsync(url)).Content.ReadAsStringAsync());

    [Fact]
    public async Task La_busqueda_muestra_el_boton_solo_a_quien_puede_crear()
    {
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearComunionAsync(capturista, "Ana", "Boton" + u);

        (await HtmlAsync(capturista, $"/?texto=boton{u}")).Should().Contain($"/Agregar/comunion/{id}");

        var secretaria = await app.ClienteConRolAsync(Roles.Secretaria);
        (await HtmlAsync(secretaria, $"/?texto=boton{u}")).Should().Contain("Boton" + u).And.NotContain("/Agregar/");
    }

    [Fact]
    public async Task El_menu_ofrece_los_libros_que_faltan_y_secretaria_no_entra()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var id = await CrearComunionAsync(c, "Ana", "Menu" + Unico());

        var html = await HtmlAsync(c, $"/Agregar/comunion/{id}");

        html.Should().Contain("Bautismo").And.Contain("Confirmación")
            .And.Contain("Matrimonio (como esposo)").And.Contain("Matrimonio (como esposa)")
            .And.NotContain("Primera Comunión")
            .And.Contain($"Origen.Clave=comunion&Origen.Id={id}");

        var secretaria = await app.ClienteConRolAsync(Roles.Secretaria);
        var r = await secretaria.GetAsync($"/Agregar/comunion/{id}");
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("AccesoDenegado");

        (await c.GetAsync("/Agregar/comunion/999999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Alta_desde_el_menu_precarga_vincula_y_deja_de_ofrecer_ese_libro()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearComunionAsync(c, "Ana", "Alta" + u);
        var url = $"/Confirmacion/Editar?Origen.Clave=comunion&Origen.Id={id}";

        var formulario = await HtmlAsync(c, url);
        formulario.Should().Contain("misma persona").And.Contain("Alta" + u)
            .And.Contain("name=\"Origen.Id\"");

        var r = await AppFixture.PostFormAsync(c, url, "/Confirmacion/Editar", new()
        {
            ["Input.Libro"] = "L" + Unico(), ["Input.Folio"] = "1", ["Input.Partida"] = "1",
            ["Input.Nombres"] = "Ana", ["Input.Apellidos"] = "Alta" + u, ["Input.FechaConfirmacion"] = "5/6/2005",
            ["Input.Ministro"] = "", ["Input.NotasMarginales"] = "", ["Input.Revisar"] = "false",
            ["Input.MotivoRevision"] = "", ["Input.Version"] = "0", ["accion"] = "guardar",
            ["Origen.Clave"] = "comunion", ["Origen.Id"] = id.ToString(), ["Origen.Persona"] = "", ["Origen.Como"] = "",
        });
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var cadena = await HtmlAsync(c, $"/Vinculos/comunion/{id}");
        cadena.Should().Contain("/Confirmacion/Detalle/");

        var menu = await HtmlAsync(c, $"/Agregar/comunion/{id}");
        menu.Should().NotContain("Confirmación").And.Contain("Bautismo");
    }

    [Fact]
    public async Task Matrimonio_como_esposa_precarga_a_la_persona_en_ese_contrayente()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearComunionAsync(c, "Gloria", "Esposa" + u);

        var html = await HtmlAsync(c, $"/Matrimonio/Editar?Origen.Clave=comunion&Origen.Id={id}&Origen.Como=esposa");

        html.Should().Contain("misma persona").And.Contain("Origen.Como");
        Regex.Match(html, @"name=""Input\.Esposa\.Apellidos""[^>]*value=""([^""]*)""").Groups[1].Value.Should().Be("Esposa" + u);
        Regex.Match(html, @"name=""Input\.Esposo\.Apellidos""[^>]*value=""([^""]*)""").Groups[1].Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Origen_inexistente_abre_el_formulario_vacio_con_aviso()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var html = await HtmlAsync(c, "/Comunion/Editar?Origen.Clave=bautismo&Origen.Id=999999999");

        html.Should().Contain("No se encontró el registro de origen").And.NotContain("name=\"Origen.Id\"");
    }
}
