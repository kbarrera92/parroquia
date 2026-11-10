using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class CambioPasswordWebPruebas(AppFixture app)
{
    private const string PasswordTemporal = "Temporal-Prueba-1234";
    private const string Pagina = "/Cuenta/CambiarPassword";

    private async Task<string> CrearUsuarioAsync(bool debeCambiar, bool activo = true)
    {
        var nombre = $"cp_{Guid.NewGuid():N}"[..20];
        using var scope = app.Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var u = new Usuario
        {
            UserName = nombre, NombreCompleto = "Prueba", Activo = activo,
            LockoutEnabled = true, DebeCambiarPassword = debeCambiar,
        };
        (await usuarios.CreateAsync(u, PasswordTemporal)).Succeeded.Should().BeTrue();
        (await usuarios.AddToRoleAsync(u, Roles.Capturista)).Succeeded.Should().BeTrue();
        return nombre;
    }

    private static async Task<string> TextoAsync(HttpResponseMessage r) =>
        WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private static Task<HttpResponseMessage> CambiarAsync(HttpClient c, string actual, string nueva, string? confirmar = null) =>
        AppFixture.PostFormAsync(c, Pagina, Pagina, new Dictionary<string, string>
        {
            ["PasswordActual"] = actual,
            ["PasswordNueva"] = nueva,
            ["PasswordConfirmar"] = confirmar ?? nueva,
        });

    [Fact]
    public async Task Con_cambio_obligatorio_el_login_lleva_a_cambiar_y_el_resto_del_sistema_queda_bloqueado()
    {
        var nombre = await CrearUsuarioAsync(debeCambiar: true);
        var c = app.Cliente();

        var login = await AppFixture.LoginAsync(c, nombre, PasswordTemporal);

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.Should().StartWith(Pagina);

        var otra = await c.GetAsync("/Comunion");
        otra.StatusCode.Should().Be(HttpStatusCode.Redirect);
        otra.Headers.Location!.OriginalString.Should().StartWith(Pagina);

        (await c.GetAsync(Pagina)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/Cuenta/Logout")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sin_cambio_obligatorio_el_sistema_funciona_normal()
    {
        var nombre = await CrearUsuarioAsync(debeCambiar: false);
        var c = app.Cliente();
        await AppFixture.LoginAsync(c, nombre, PasswordTemporal);

        (await c.GetAsync("/Comunion")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("corta-123", "al menos 12 caracteres")]
    [InlineData("password1234", "muy común")]
    public async Task Rechaza_contrasenas_que_incumplen_la_politica_con_mensaje_en_espanol(string nueva, string fragmento)
    {
        var nombre = await CrearUsuarioAsync(debeCambiar: true);
        var c = app.Cliente();
        await AppFixture.LoginAsync(c, nombre, PasswordTemporal);

        var r = await CambiarAsync(c, PasswordTemporal, nueva);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TextoAsync(r)).Should().Contain(fragmento);
    }

    [Fact]
    public async Task Rechaza_actual_incorrecta_confirmacion_distinta_y_nueva_igual_a_la_actual()
    {
        var nombre = await CrearUsuarioAsync(debeCambiar: true);
        var c = app.Cliente();
        await AppFixture.LoginAsync(c, nombre, PasswordTemporal);

        var mal = await CambiarAsync(c, "no-es-la-actual-123", "Una-Nueva-Clave-987");
        (await TextoAsync(mal)).Should().Contain("contraseña actual no es correcta");

        var distinta = await CambiarAsync(c, PasswordTemporal, "Una-Nueva-Clave-987", "Otra-Distinta-Clave-1");
        (await TextoAsync(distinta)).Should().Contain("no coinciden");

        var igual = await CambiarAsync(c, PasswordTemporal, PasswordTemporal);
        (await TextoAsync(igual)).Should().Contain("distinta");
    }

    [Fact]
    public async Task Cambio_valido_libera_el_sistema_deja_bitacora_sin_contrasenas_y_la_nueva_funciona()
    {
        var nombre = await CrearUsuarioAsync(debeCambiar: true);
        const string nueva = "vela azul puente nube";
        var c = app.Cliente();
        await AppFixture.LoginAsync(c, nombre, PasswordTemporal);

        var r = await CambiarAsync(c, PasswordTemporal, nueva);

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await c.GetAsync("/Comunion")).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
            var entradas = await db.BitacoraAccesos.AsNoTracking().Where(b => b.UsuarioNombre == nombre).ToListAsync();
            entradas.Should().Contain(b => b.Evento == EventoAcceso.PasswordCambiada);
            entradas.Select(b => b.Detalle ?? "").Should().NotContain(d => d.Contains(nueva) || d.Contains(PasswordTemporal));

            var u = await scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByNameAsync(nombre);
            u!.DebeCambiarPassword.Should().BeFalse();
        }

        var nuevo = app.Cliente();
        (await AppFixture.LoginAsync(nuevo, nombre, nueva)).Headers.Location!.OriginalString.Should().Be("/");
        (await AppFixture.LoginAsync(app.Cliente(), nombre, PasswordTemporal)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Usuario_desactivado_con_la_contrasena_correcta_no_entra()
    {
        var nombre = await CrearUsuarioAsync(debeCambiar: false, activo: false);

        var r = await AppFixture.LoginAsync(app.Cliente(), nombre, PasswordTemporal);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TextoAsync(r)).Should().Contain("Usuario o contraseña incorrectos");
    }
}
