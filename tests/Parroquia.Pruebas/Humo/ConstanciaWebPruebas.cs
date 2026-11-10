using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Configuracion;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class ConstanciaWebPruebas(AppFixture app)
{
    private static string Unico() => Guid.NewGuid().ToString("N")[..8];
    private static DateOnly HoyGt => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6));

    /// <summary>La base de las pruebas web se comparte: si no hay párroco vigente se registra uno sintético.</summary>
    private async Task<string> AsegurarParrocoAsync()
    {
        using var scope = app.Services.CreateScope();
        var parrocos = scope.ServiceProvider.GetRequiredService<ServicioParroco>();
        if (await parrocos.ObtenerActualAsync() is null)
            (await parrocos.RegistrarNuevoAsync("P. Prueba Ejemplo", "Párroco", new DateOnly(2000, 1, 1))).Ok.Should().BeTrue();
        return (await parrocos.ObtenerActualAsync())!.Nombre;
    }

    private static async Task<long> CrearRegistroAsync(
        HttpClient capturista, string apellidos, string fecha = "2/10/2004", bool revisar = false)
    {
        var r = await AppFixture.PostFormAsync(capturista, "/Comunion/Editar", "/Comunion/Editar", new()
        {
            ["Input.Libro"] = "L" + Unico(), ["Input.Folio"] = "1", ["Input.Partida"] = "1",
            ["Input.Nombres"] = "Ana María", ["Input.Apellidos"] = apellidos, ["Input.Fecha"] = fecha,
            ["Input.Ministro"] = "", ["Input.NotasMarginales"] = "",
            ["Input.Revisar"] = revisar ? "true" : "false", ["Input.MotivoRevision"] = revisar ? "Dato dudoso" : "",
            ["Input.Version"] = "0", ["accion"] = "guardar",
        });
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return long.Parse(Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);
    }

    private static async Task<string> HtmlAsync(HttpClient c, string url) =>
        WebUtility.HtmlDecode(await (await c.GetAsync(url)).Content.ReadAsStringAsync());

    private static Task<HttpResponseMessage> ImprimirAsync(HttpClient c, long id, Dictionary<string, string>? campos = null) =>
        AppFixture.PostFormAsync(c, $"/Constancias/comunion/{id}", $"/Constancias/comunion/{id}", campos ?? []);

    private async Task<(long Id, string Apellido)> RegistroNuevoAsync(string fecha = "2/10/2004", bool revisar = false)
    {
        await AsegurarParrocoAsync();
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Const" + Unico();
        return (await CrearRegistroAsync(capturista, apellido, fecha, revisar), apellido);
    }

    [Fact]
    public async Task Secretaria_imprime_recibe_un_pdf_y_queda_en_el_detalle_y_la_bitacora()
    {
        var parroco = await AsegurarParrocoAsync();
        var (id, apellido) = await RegistroNuevoAsync();
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var vista = await HtmlAsync(s, $"/Constancias/comunion/{id}");
        vista.Should().Contain("CERTIFICACIÓN DE COMUNIÓN").And.Contain($"ANA MARÍA {apellido.ToUpperInvariant()}")
            .And.Contain("2 de octubre de 2004").And.Contain(parroco).And.Contain("Imprimir constancia (PDF)");

        var r = await ImprimirAsync(s, id);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        r.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        r.Headers.Contains("Content-Security-Policy").Should().BeFalse();   // el visor de PDF del navegador no debe heredar la CSP de las páginas
        r.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        var detalle = await HtmlAsync(s, $"/Comunion/Detalle/{id}");
        detalle.Should().Contain("Constancias impresas").And.Contain(parroco).And.Contain("Reimprimir constancia");

        var admin = await app.ClienteAdminAsync();
        (await HtmlAsync(admin, "/Admin/Historial?pestana=bitacora&entidad=Comunion"))
            .Should().Contain("Imprimir constancia").And.Contain($"Comunion #{id}");
    }

    [Fact]
    public async Task La_reimpresion_se_avisa_y_se_registra_otra_vez()
    {
        var (id, _) = await RegistroNuevoAsync();
        var s = await app.ClienteConRolAsync(Roles.Secretaria);
        (await ImprimirAsync(s, id)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await HtmlAsync(s, $"/Constancias/comunion/{id}")).Should().Contain("ya se imprimió 1 vez").And.Contain("Reimprimir constancia (PDF)");
        (await ImprimirAsync(s, id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var detalle = await HtmlAsync(s, $"/Comunion/Detalle/{id}");
        Regex.Matches(detalle, "<td class=\"text-nowrap\">\\d{2}/\\d{2}/\\d{4} \\d{2}:\\d{2}</td>").Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Capturista_no_imprime_ni_ve_el_boton()
    {
        var (id, _) = await RegistroNuevoAsync();
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var vista = await c.GetAsync($"/Constancias/comunion/{id}");
        vista.StatusCode.Should().Be(HttpStatusCode.Redirect);
        vista.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");

        var post = await AppFixture.PostFormAsync(c, "/Cuenta/Logout", $"/Constancias/comunion/{id}", []);
        post.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");

        (await HtmlAsync(c, $"/Comunion/Detalle/{id}")).Should().NotContain("Imprimir constancia");
    }

    [Fact]
    public async Task Secretaria_ve_el_boton_de_imprimir_en_el_detalle()
    {
        var (id, _) = await RegistroNuevoAsync();
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var detalle = await HtmlAsync(s, $"/Comunion/Detalle/{id}");

        detalle.Should().Contain("Imprimir constancia").And.Contain($"/Constancias/comunion/{id}");
    }

    [Fact]
    public async Task Registro_con_avisos_pide_confirmacion_antes_de_entregar_el_pdf()
    {
        var (id, _) = await RegistroNuevoAsync(fecha: "2004", revisar: true);
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var vista = await HtmlAsync(s, $"/Constancias/comunion/{id}");
        vista.Should().Contain("Revise antes de imprimir").And.Contain("fecha de la comunión está incompleta")
            .And.Contain("marcado para revisar").And.Contain("name=\"confirmar\"");

        var sin = await ImprimirAsync(s, id);
        sin.StatusCode.Should().Be(HttpStatusCode.OK);
        sin.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        WebUtility.HtmlDecode(await sin.Content.ReadAsStringAsync()).Should().Contain("confírmelos para imprimir");

        var con = await ImprimirAsync(s, id, new() { ["confirmar"] = "true" });
        con.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await HtmlAsync(s, $"/Comunion/Detalle/{id}")).Should().Contain("Con avisos");
    }

    [Fact]
    public async Task Solo_el_administrador_cambia_la_fecha_de_emision()
    {
        var (id, _) = await RegistroNuevoAsync();
        var ayer = HoyGt.AddDays(-1).ToString("yyyy-MM-dd");

        var s = await app.ClienteConRolAsync(Roles.Secretaria);
        (await HtmlAsync(s, $"/Constancias/comunion/{id}")).Should().NotContain("solo Administrador");
        var rechazo = await ImprimirAsync(s, id, new() { ["fecha"] = ayer });
        rechazo.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        WebUtility.HtmlDecode(await rechazo.Content.ReadAsStringAsync()).Should().Contain("Solo el Administrador puede cambiar la fecha");

        var admin = await app.ClienteAdminAsync();
        (await HtmlAsync(admin, $"/Constancias/comunion/{id}?fecha={ayer}")).Should().Contain("solo Administrador").And.Contain($"value=\"{ayer}\"");
        var ok = await ImprimirAsync(admin, id, new() { ["fecha"] = ayer });
        ok.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var detalle = await HtmlAsync(admin, $"/Comunion/Detalle/{id}");
        detalle.Should().Contain("ajustada por " + AppFixture.UsuarioAdmin);
    }

    [Fact]
    public async Task Fecha_futura_o_ilegible_se_rechaza_con_mensaje()
    {
        var (id, _) = await RegistroNuevoAsync();
        var admin = await app.ClienteAdminAsync();

        var futura = await ImprimirAsync(admin, id, new() { ["fecha"] = HoyGt.AddDays(3).ToString("yyyy-MM-dd") });
        WebUtility.HtmlDecode(await futura.Content.ReadAsStringAsync()).Should().Contain("no puede ser futura");

        var rara = await ImprimirAsync(admin, id, new() { ["fecha"] = "mañana" });
        WebUtility.HtmlDecode(await rara.Content.ReadAsStringAsync()).Should().Contain("no es válida");
    }

    [Fact]
    public async Task Sacramento_desconocido_registro_inexistente_o_anulado_dan_404()
    {
        var (id, _) = await RegistroNuevoAsync();
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        (await s.GetAsync($"/Constancias/sacramento-inexistente/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.GetAsync("/Constancias/comunion/99999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        await AppFixture.PostFormAsync(capturista, $"/Comunion/Detalle/{id}", $"/Comunion/Detalle/{id}?handler=Anular",
            new() { ["motivo"] = "Duplicado" });
        (await s.GetAsync($"/Constancias/comunion/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonimo_va_al_login()
    {
        var r = await app.Cliente().GetAsync("/Constancias/comunion/1");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }
}
