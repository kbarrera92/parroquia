using System.Net;
using System.Text.RegularExpressions;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

/// <summary>
/// Quién puede dar de alta qué: el Capturista transcribe libros físicos; la Secretaria asienta lo nuevo (numera el sistema).
/// El servidor lo exige aunque se fuerce el POST. Datos sintéticos únicos por prueba.
/// </summary>
[Collection(AppCollection.Nombre)]
public class PermisosAltaWebPruebas(AppFixture app)
{
    private static string Unico() => Guid.NewGuid().ToString("N")[..8];
    private const string Denegado = "/Cuenta/AccesoDenegado";

    private static Dictionary<string, string> Campos(string apellidos, bool asientoNuevo) => new()
    {
        ["Input.Libro"] = asientoNuevo ? "" : "P" + Unico(), ["Input.Folio"] = asientoNuevo ? "" : "1",
        ["Input.Partida"] = asientoNuevo ? "" : "1",
        ["Input.Nombres"] = "Perfil", ["Input.Apellidos"] = apellidos, ["Input.Fecha"] = "2/10/2004",
        ["Input.Ministro"] = "", ["Input.NotasMarginales"] = "", ["Input.Revisar"] = "false",
        ["Input.MotivoRevision"] = "", ["Input.Version"] = "0", ["accion"] = "guardar",
        ["Input.AsientoNuevo"] = asientoNuevo ? "true" : "false",
    };

    private static async Task<string> HtmlAsync(HttpClient c, string url) =>
        WebUtility.HtmlDecode(await (await c.GetAsync(url)).Content.ReadAsStringAsync());

    private static Task<HttpResponseMessage> PostAsync(HttpClient c, Dictionary<string, string> campos) =>
        AppFixture.PostFormAsync(c, "/Comunion/Editar", "/Comunion/Editar", campos);

    [Fact]
    public async Task Capturista_transcribe_pero_no_asienta_nuevo_ni_ve_esa_pestana()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var html = await HtmlAsync(c, "/Comunion/Editar");
        html.Should().Contain("Transcribir de un libro físico").And.NotContain("Asiento nuevo (numera el sistema)");

        // Pedir el modo sistema a mano no sirve: cae a transcribir.
        (await HtmlAsync(c, "/Comunion/Editar?modo=sistema")).Should().Contain("Transcribir de un libro físico")
            .And.NotContain("Próximo número");

        var r = await PostAsync(c, Campos("Nuevo" + Unico(), asientoNuevo: true));
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain(Denegado);

        var ok = await PostAsync(c, Campos("Trans" + Unico(), asientoNuevo: false));
        ok.Headers.Location!.ToString().Should().Contain("Detalle/");
    }

    [Fact]
    public async Task Secretaria_asienta_nuevo_pero_no_transcribe_aunque_fuerce_el_envio()
    {
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var r = await PostAsync(s, Campos("Trans" + Unico(), asientoNuevo: false));
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain(Denegado);

        // Su GET de alta abre directo en «Asiento nuevo».
        (await s.GetAsync("/Comunion/Editar")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Administrador_ve_ambas_pestanas()
    {
        var a = await app.ClienteAdminAsync();

        (await HtmlAsync(a, "/Comunion/Editar")).Should().Contain("Transcribir de un libro físico")
            .And.Contain("Asiento nuevo (numera el sistema)");
    }

    [Fact]
    public async Task La_supletoria_transcrita_es_del_capturista_y_la_numerada_de_la_secretaria()
    {
        var cap = await app.ClienteConRolAsync(Roles.Capturista);
        var sec = await app.ClienteConRolAsync(Roles.Secretaria);

        var deCap = await HtmlAsync(cap, "/Comunion/Editar?modo=supletoria");
        Regex.Match(deCap, @"name=""Input\.Supletoria\.NumeraSistema""[^>]*value=""([^""]*)""").Groups[1].Value.Should().Be("false");

        var campos = Campos("Sup" + Unico(), asientoNuevo: false);
        campos["Input.Supletoria.NumeraSistema"] = "true";
        campos["Input.Supletoria.Fecha"] = "2/10/2026";
        campos["Input.Supletoria.Testigo"] = "Testigo Prueba";
        var r = await AppFixture.PostFormAsync(cap, "/Comunion/Editar?modo=supletoria", "/Comunion/Editar?modo=supletoria", campos);
        r.Headers.Location!.ToString().Should().Contain(Denegado);

        campos["Input.Supletoria.NumeraSistema"] = "false";
        var s = await AppFixture.PostFormAsync(sec, "/Comunion/Editar?modo=supletoria", "/Comunion/Editar?modo=supletoria", campos);
        s.Headers.Location!.ToString().Should().Contain(Denegado);
    }
}
