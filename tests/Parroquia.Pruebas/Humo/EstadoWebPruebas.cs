using System.Net;
using System.Text.RegularExpressions;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

/// <summary>Página «Estado del sistema» (Administración): versión, base de datos y marcadores que escriben los scripts de respaldo.</summary>
[Collection(AppCollection.Nombre)]
public class EstadoWebPruebas(AppFixture app)
{
    private string Respaldos => Path.Combine(app.CarpetaDatos, "respaldos");

    private void Marcador(string archivo, string? contenido)
    {
        Directory.CreateDirectory(Respaldos);
        var ruta = Path.Combine(Respaldos, archivo);
        if (contenido is null) File.Delete(ruta);
        else File.WriteAllText(ruta, contenido);
    }

    private static string RespaldoJson(DateTimeOffset fecha, string resultado = "ok") => $$"""
        {"fecha":"{{fecha:O}}","resultado":"{{resultado}}","mensaje":"{{(resultado == "ok" ? "" : "pg_dump falló")}}",
         "archivo":"parroquia-prueba.zip","bytes":1048576,"copia2":true,"copia3":false}
        """;

    /// <summary>Nivel (success/warning) de la alerta con el id dado, o null si no aparece.</summary>
    private static string? Nivel(string html, string id) =>
        Regex.Match(html, $"id=\"{id}\" class=\"alert (alert-[a-z]+)\"") is { Success: true } m ? m.Groups[1].Value : null;

    private async Task<string> EstadoComoAdminAsync()
    {
        var r = await (await app.ClienteAdminAsync()).GetAsync("/Admin/Estado");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_ve_version_base_de_datos_y_registros_por_libro()
    {
        var html = await EstadoComoAdminAsync();

        html.Should().Contain("Estado del sistema")
            .And.Contain("PostgreSQL")
            .And.Contain("Tamaño de la base de datos")
            .And.Contain("Primera Comunión");
        Regex.IsMatch(html, @"Versión de la aplicación</dt>\s*<dd[^>]*>1\.0\.0").Should().BeTrue();
    }

    [Theory]
    [InlineData(Roles.Capturista)]
    [InlineData(Roles.Secretaria)]
    public async Task Capturista_y_secretaria_no_entran(string rol)
    {
        var r = await (await app.ClienteConRolAsync(rol)).GetAsync("/Admin/Estado");

        // Acceso denegado redirige a /Cuenta/AccesoDenegado (o 403, según la política de la app).
        ((int)r.StatusCode).Should().BeOneOf(302, 403);
        if (r.StatusCode == HttpStatusCode.Redirect)
            r.Headers.Location!.ToString().Should().Contain("AccesoDenegado");
    }

    [Fact]
    public async Task Anonimo_va_al_login()
    {
        var r = await app.Cliente().GetAsync("/Admin/Estado");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Fact]
    public async Task Sin_marcador_de_respaldo_avisa()
    {
        Marcador("ultimo.json", null);

        Nivel(await EstadoComoAdminAsync(), "estado-respaldo").Should().Be("alert-warning");
    }

    [Fact]
    public async Task Respaldo_de_hace_40_horas_avisa()
    {
        Marcador("ultimo.json", RespaldoJson(DateTimeOffset.UtcNow.AddHours(-40)));

        var html = await EstadoComoAdminAsync();

        Nivel(html, "estado-respaldo").Should().Be("alert-warning");
        html.Should().Contain("36");
    }

    [Fact]
    public async Task Respaldo_reciente_esta_bien()
    {
        Marcador("ultimo.json", RespaldoJson(DateTimeOffset.UtcNow.AddHours(-2)));

        var html = await EstadoComoAdminAsync();

        Nivel(html, "estado-respaldo").Should().Be("alert-success");
        html.Should().Contain("parroquia-prueba.zip");
    }

    [Fact]
    public async Task Respaldo_fallido_avisa_aunque_sea_reciente()
    {
        Marcador("ultimo.json", RespaldoJson(DateTimeOffset.UtcNow.AddHours(-1), "error"));

        var html = await EstadoComoAdminAsync();

        Nivel(html, "estado-respaldo").Should().Be("alert-warning");
        html.Should().Contain("pg_dump falló");
    }

    [Fact]
    public async Task Marcador_ilegible_avisa_sin_romper_la_pagina()
    {
        Marcador("ultimo.json", "esto no es json");

        Nivel(await EstadoComoAdminAsync(), "estado-respaldo").Should().Be("alert-warning");
    }

    [Fact]
    public async Task Prueba_de_restauracion_de_hace_mas_de_100_dias_avisa_y_reciente_esta_bien()
    {
        Marcador("ultima-restauracion.json",
            $$"""{"fecha":"{{DateTimeOffset.UtcNow.AddDays(-120):O}}","resultado":"ok","archivo":"parroquia-prueba.zip"}""");
        Nivel(await EstadoComoAdminAsync(), "estado-restauracion").Should().Be("alert-warning");

        Marcador("ultima-restauracion.json",
            $$"""{"fecha":"{{DateTimeOffset.UtcNow.AddDays(-10):O}}","resultado":"ok","archivo":"parroquia-prueba.zip"}""");
        Nivel(await EstadoComoAdminAsync(), "estado-restauracion").Should().Be("alert-success");
    }
}
