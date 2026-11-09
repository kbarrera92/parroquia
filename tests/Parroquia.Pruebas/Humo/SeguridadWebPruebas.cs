using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class SeguridadWebPruebas(AppFixture app)
{
    private static string TextoCsp(HttpResponseMessage r) =>
        string.Join(" ", r.Headers.GetValues("Content-Security-Policy"));

    [Fact]
    public async Task Las_paginas_llevan_cabeceras_de_seguridad_y_csp_sin_scripts_en_linea()
    {
        var r = await app.Cliente().GetAsync("/Cuenta/Login");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        r.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        r.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        r.Headers.Contains("Permissions-Policy").Should().BeTrue();

        var csp = TextoCsp(r);
        csp.Should().Contain("default-src 'self'")
            .And.Contain("script-src 'self'")
            .And.Contain("frame-ancestors 'none'")
            .And.Contain("object-src 'none'")
            .And.Contain("form-action 'self'")
            .And.Contain("base-uri 'self'");
        var scripts = csp.Split(';').Select(d => d.Trim()).Single(d => d.StartsWith("script-src", StringComparison.Ordinal));
        scripts.Should().NotContain("unsafe");
    }

    [Fact]
    public async Task Las_paginas_de_la_aplicacion_no_usan_scripts_en_linea_ni_manejadores_de_eventos()
    {
        var c = await app.ClienteAdminAsync();
        foreach (var url in new[] { "/", "/Admin/Usuarios", "/Admin/Usuarios/Editar", "/Cuenta/CambiarPassword", "/Comunion", "/Admin/Historial" })
        {
            var html = WebUtility.HtmlDecode(await (await c.GetAsync(url)).Content.ReadAsStringAsync());

            html.Should().NotMatchRegex(@"<script(?![^>]*\bsrc=)[^>]*>", $"{url} no debe tener <script> en línea (CSP)");
            html.Should().NotMatchRegex(@"\son(click|change|submit|load|input)\s*=", $"{url} no debe tener manejadores en línea (CSP)");
        }
    }

    [Fact]
    public async Task El_inicio_de_sesion_se_limita_por_ip_solo_en_el_envio_del_formulario()
    {
        using var fabrica = app.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Seguridad:LoginPorMinuto"] = "3" })));
        var c = fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var usuario = $"fantasma_{Guid.NewGuid():N}"[..20];

        for (var i = 0; i < 6; i++)   // ver la página no cuenta
            (await c.GetAsync("/Cuenta/Login")).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var i = 0; i < 3; i++)
            (await AppFixture.LoginAsync(c, usuario, "cualquiera-123456")).StatusCode.Should().Be(HttpStatusCode.OK);

        var cuarto = await AppFixture.LoginAsync(c, usuario, "cualquiera-123456");

        cuarto.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        cuarto.Headers.Contains("Retry-After").Should().BeTrue();
        WebUtility.HtmlDecode(await cuarto.Content.ReadAsStringAsync()).Should().Contain("Demasiados intentos");
        (await c.GetAsync("/Cuenta/Login")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Con_el_limite_normal_el_uso_habitual_no_se_bloquea()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        for (var i = 0; i < 15; i++)
            (await c.GetAsync("/Comunion")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
