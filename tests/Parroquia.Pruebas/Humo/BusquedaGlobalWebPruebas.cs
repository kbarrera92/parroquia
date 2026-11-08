using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class BusquedaGlobalWebPruebas(AppFixture app)
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
    public async Task Inicio_encuentra_a_la_persona_sin_tildes_con_enlace_al_detalle()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearComunionAsync(c, "Juan Carlos", "Hidalgo Arévalo" + u);

        var html = await HtmlAsync(c, $"/?texto=hidalgo+arevalo{u}");

        html.Should().Contain("Hidalgo Arévalo" + u).And.Contain("Primera Comunión").And.Contain("2 de octubre de 2004");
        html.Should().Contain($"href=\"/Comunion/Detalle/{id}\"");
    }

    [Fact]
    public async Task Inicio_sin_texto_o_con_texto_corto_no_busca_y_orienta()
    {
        var c = await app.ClienteConRolAsync(Roles.Secretaria);

        (await HtmlAsync(c, "/")).Should().Contain("Los resultados aparecerán aquí");
        (await HtmlAsync(c, "/?texto=a")).Should().Contain("Escriba al menos 2 letras");
    }

    [Fact]
    public async Task Inicio_avisa_cuando_no_hay_coincidencias()
    {
        var c = await app.ClienteConRolAsync(Roles.Secretaria);

        (await HtmlAsync(c, $"/?texto=nadie{Unico()}")).Should().Contain("No se encontró a nadie");
    }

    [Fact]
    public async Task Inicio_no_muestra_registros_anulados()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearComunionAsync(c, "Ana", "Anulada" + u);
        await AppFixture.PostFormAsync(c, $"/Comunion/Detalle/{id}", $"/Comunion/Detalle/{id}?handler=Anular",
            new() { ["motivo"] = "Duplicado" });

        (await HtmlAsync(c, $"/?texto=anulada{u}")).Should().Contain("No se encontró a nadie");
    }

    [Fact]
    public async Task Peticion_htmx_devuelve_solo_los_resultados()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        await CrearComunionAsync(c, "Ana", "Parcial" + u);

        var peticion = new HttpRequestMessage(HttpMethod.Get, $"/?texto=parcial{u}");
        peticion.Headers.Add("HX-Request", "true");
        var parcial = await (await c.SendAsync(peticion)).Content.ReadAsStringAsync();

        parcial.Should().Contain("Parcial" + u).And.NotContain("<html");
    }

    [Fact]
    public async Task Los_nombres_se_escapan_en_los_resultados()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        await CrearComunionAsync(c, "Ana", $"<i>cursiva{u}</i>");

        var crudo = await (await c.GetAsync($"/?texto=cursiva{u}")).Content.ReadAsStringAsync();

        crudo.Should().NotContain($"<i>cursiva{u}</i>").And.Contain($"&lt;i&gt;cursiva{u}&lt;/i&gt;");
    }

    [Fact]
    public async Task El_menu_Libros_lista_los_modulos_registrados()
    {
        var c = await app.ClienteConRolAsync(Roles.Secretaria);

        var html = await HtmlAsync(c, "/");

        html.Should().Contain("Libros").And.Contain("Primera Comunión").And.Contain("href=\"/Comunion\"");
    }

    [Fact]
    public async Task Comunion_se_descubre_por_la_interfaz_de_modulos()
    {
        using var scope = app.Services.CreateScope();

        var modulos = scope.ServiceProvider.GetServices<IModuloSacramental>().ToList();

        var comunion = modulos.Should().ContainSingle(m => m.Clave == "comunion").Subject;
        comunion.Nombre.Should().Be("Primera Comunión");
        comunion.Entidad.Should().Be("Comunion");
        comunion.RutaListado.Should().Be("/Comunion");
        comunion.RutaDetalle(7).Should().Be("/Comunion/Detalle/7");
    }
}
