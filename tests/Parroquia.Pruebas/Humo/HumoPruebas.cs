using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;
using Testcontainers.PostgreSql;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class HumoPruebas(AppFixture app)
{
    [Fact]
    public async Task Inicio_anonimo_redirige_a_login()
    {
        var r = await app.Cliente().GetAsync("/");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Fact]
    public async Task Login_se_muestra_en_espanol()
    {
        var r = await app.Cliente().GetAsync("/Cuenta/Login");
        var html = await r.Content.ReadAsStringAsync();

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("Iniciar sesión").And.Contain("lang=\"es-GT\"");
    }

    [Fact]
    public async Task Admin_sembrado_tiene_rol_y_todos_los_permisos()
    {
        using var scope = app.Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var admin = await usuarios.FindByNameAsync(AppFixture.UsuarioAdmin);
        admin.Should().NotBeNull();
        (await usuarios.IsInRoleAsync(admin!, Roles.Administrador)).Should().BeTrue();

        var rol = await roles.FindByNameAsync(Roles.Administrador);
        var permisos = (await roles.GetClaimsAsync(rol!))
            .Where(c => c.Type == Permisos.TipoClaim).Select(c => c.Value);
        permisos.Should().BeEquivalentTo(Permisos.Todos);
    }

    [Fact]
    public async Task Login_valido_da_acceso_al_inicio_y_password_mala_no()
    {
        var malo = app.Cliente();
        var rMalo = await AppFixture.LoginAsync(malo, AppFixture.UsuarioAdmin, "incorrecta-123456");
        rMalo.StatusCode.Should().Be(HttpStatusCode.OK); // vuelve a mostrar el formulario
        (await rMalo.Content.ReadAsStringAsync()).Should().Contain("incorrectos");

        var bueno = app.Cliente();
        var rBueno = await AppFixture.LoginAsync(bueno, AppFixture.UsuarioAdmin, AppFixture.PasswordAdmin);
        rBueno.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var inicio = await bueno.GetAsync("/");
        inicio.StatusCode.Should().Be(HttpStatusCode.OK);
        (await inicio.Content.ReadAsStringAsync()).Should().Contain("Búsqueda");
    }

    [Theory]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css", "text/css")]
    [InlineData("/css/site.css", "text/css")]
    [InlineData("/lib/bootstrap/dist/js/bootstrap.bundle.min.js", "javascript")]
    [InlineData("/lib/htmx/htmx.min.js", "javascript")]
    public async Task Recursos_estaticos_son_publicos_sin_sesion(string ruta, string tipoEsperado)
    {
        var r = await app.Cliente().GetAsync(ruta);

        r.StatusCode.Should().Be(HttpStatusCode.OK, "el login necesita CSS/JS antes de tener sesión");
        r.Content.Headers.ContentType!.MediaType.Should().Contain(tipoEsperado);
    }

    [Fact]
    public async Task Login_referencia_solo_recursos_que_responden_200_sin_sesion()
    {
        var cliente = app.Cliente();
        var html = await (await cliente.GetAsync("/Cuenta/Login")).Content.ReadAsStringAsync();

        var rutas = System.Text.RegularExpressions.Regex
            .Matches(html, "(?:href|src)=\"(/(?:css|lib)/[^\"?]+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();

        rutas.Should().NotBeEmpty();
        foreach (var ruta in rutas)
            (await cliente.GetAsync(ruta)).StatusCode.Should().Be(HttpStatusCode.OK, ruta);
    }
}
