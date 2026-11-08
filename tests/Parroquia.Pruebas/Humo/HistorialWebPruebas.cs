using System.Net;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class HistorialWebPruebas(AppFixture app)
{
    [Fact]
    public async Task Anonimo_va_al_login()
    {
        var r = await app.Cliente().GetAsync("/Admin/Historial");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Theory]
    [InlineData(Roles.Capturista)]
    [InlineData(Roles.Secretaria)]
    public async Task Sin_permiso_de_auditoria_se_deniega(string rol)
    {
        var c = await app.ClienteConRolAsync(rol);

        var r = await c.GetAsync("/Admin/Historial");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Fact]
    public async Task Auditoria_muestra_accion_campo_y_valores_de_un_cambio_real()
    {
        var c = await app.ClienteAdminAsync();
        var nombre = $"P. Historial {Guid.NewGuid():N}"[..26];
        await AppFixture.PostFormAsync(c, "/Admin/Ministros", "/Admin/Ministros?handler=Crear", new() { ["valor"] = nombre });

        var html = await (await c.GetAsync(
            $"/Admin/Historial?pestana=auditoria&entidad=Ministro&usuario={AppFixture.UsuarioAdmin}")).Content.ReadAsStringAsync();

        html.Should().Contain("Alta").And.Contain("Ministro").And.Contain("Nombre").And.Contain(nombre);
        html.Should().Contain(AppFixture.UsuarioAdmin);
    }

    [Fact]
    public async Task Bitacora_muestra_los_inicios_de_sesion_del_usuario()
    {
        var c = await app.ClienteAdminAsync();

        var html = await (await c.GetAsync(
            $"/Admin/Historial?pestana=bitacora&usuario={AppFixture.UsuarioAdmin}")).Content.ReadAsStringAsync();

        System.Net.WebUtility.HtmlDecode(html).Should().Contain("Inicio de sesión").And.Contain(AppFixture.UsuarioAdmin);
    }

    [Fact]
    public async Task Filtro_que_no_coincide_muestra_mensaje_de_sin_resultados()
    {
        var c = await app.ClienteAdminAsync();

        var html = await (await c.GetAsync(
            $"/Admin/Historial?pestana=auditoria&usuario=nadie_{Guid.NewGuid():N}")).Content.ReadAsStringAsync();

        html.Should().Contain("No hay movimientos");
    }

    [Fact]
    public async Task Los_valores_se_escapan_y_no_se_ejecutan_como_html()
    {
        var c = await app.ClienteAdminAsync();
        var marca = Guid.NewGuid().ToString("N")[..8];
        await AppFixture.PostFormAsync(c, "/Admin/Ministros", "/Admin/Ministros?handler=Crear",
            new() { ["valor"] = $"<script>alert('{marca}')</script>" });

        var html = await (await c.GetAsync(
            "/Admin/Historial?pestana=auditoria&entidad=Ministro&usuario=" + AppFixture.UsuarioAdmin)).Content.ReadAsStringAsync();

        html.Should().NotContain($"<script>alert('{marca}')");
        html.Should().Contain($"&lt;script&gt;alert(");
    }

    [Fact]
    public async Task Fechas_invalidas_en_la_URL_no_rompen_la_pagina()
    {
        var c = await app.ClienteAdminAsync();

        var r = await c.GetAsync("/Admin/Historial?desde=no-es-fecha&hasta=2026-99-99&pagina=abc");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(Roles.Administrador, true)]
    [InlineData(Roles.Secretaria, false)]
    [InlineData(Roles.Capturista, false)]
    public async Task El_menu_de_administracion_solo_aparece_con_permisos(string rol, bool debeVerlo)
    {
        var c = rol == Roles.Administrador ? await app.ClienteAdminAsync() : await app.ClienteConRolAsync(rol);

        var html = await (await c.GetAsync("/")).Content.ReadAsStringAsync();

        if (debeVerlo)
            html.Should().Contain("Administración").And.Contain("/Admin/Historial")
                .And.Contain("/Admin/Ministros").And.Contain("/Admin/Configuracion").And.Contain("/Admin/Parrocos");
        else
            html.Should().NotContain("Administración").And.NotContain("/Admin/");
    }
}
