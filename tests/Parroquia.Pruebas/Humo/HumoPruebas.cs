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

/// <summary>Arranca la app real contra PostgreSQL en contenedor (requiere Docker en marcha).</summary>
public sealed class AppFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string UsuarioAdmin = "admin_prueba";
    public const string PasswordAdmin = "Clave-De-Prueba-123";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();

    private const string VariableConexion = "ConnectionStrings__Parroquia";
    private readonly Dictionary<string, string?> _anteriores = [];

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        // Program.cs lee la configuración al crear el builder, antes de que ConfigureAppConfiguration
        // pueda sobrescribirla (se usaría la BD de desarrollo). Las variables de entorno sí se leen a tiempo.
        Fijar(VariableConexion, _db.GetConnectionString());
        Fijar(SembradorIdentidad.VariableUsuario, UsuarioAdmin);
        Fijar(SembradorIdentidad.VariablePassword, PasswordAdmin);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        foreach (var (nombre, valor) in _anteriores)
            Environment.SetEnvironmentVariable(nombre, valor);
        await _db.DisposeAsync();
    }

    private void Fijar(string nombre, string valor)
    {
        _anteriores[nombre] = Environment.GetEnvironmentVariable(nombre);
        Environment.SetEnvironmentVariable(nombre, valor);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");
}

public class HumoPruebas(AppFixture app) : IClassFixture<AppFixture>
{
    private static HttpClient Cliente(AppFixture app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task Inicio_anonimo_redirige_a_login()
    {
        var r = await Cliente(app).GetAsync("/");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Fact]
    public async Task Login_se_muestra_en_espanol()
    {
        var r = await Cliente(app).GetAsync("/Cuenta/Login");
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
        var malo = Cliente(app);
        var rMalo = await LoginAsync(malo, AppFixture.UsuarioAdmin, "incorrecta-123456");
        rMalo.StatusCode.Should().Be(HttpStatusCode.OK); // vuelve a mostrar el formulario
        (await rMalo.Content.ReadAsStringAsync()).Should().Contain("incorrectos");

        var bueno = Cliente(app);
        var rBueno = await LoginAsync(bueno, AppFixture.UsuarioAdmin, AppFixture.PasswordAdmin);
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
        var r = await Cliente(app).GetAsync(ruta);

        r.StatusCode.Should().Be(HttpStatusCode.OK, "el login necesita CSS/JS antes de tener sesión");
        r.Content.Headers.ContentType!.MediaType.Should().Contain(tipoEsperado);
    }

    [Fact]
    public async Task Login_referencia_solo_recursos_que_responden_200_sin_sesion()
    {
        var cliente = Cliente(app);
        var html = await (await cliente.GetAsync("/Cuenta/Login")).Content.ReadAsStringAsync();

        var rutas = System.Text.RegularExpressions.Regex
            .Matches(html, "(?:href|src)=\"(/(?:css|lib)/[^\"?]+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();

        rutas.Should().NotBeEmpty();
        foreach (var ruta in rutas)
            (await cliente.GetAsync(ruta)).StatusCode.Should().Be(HttpStatusCode.OK, ruta);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient c, string usuario, string password)
    {
        var pagina = await c.GetAsync("/Cuenta/Login");
        var html = await pagina.Content.ReadAsStringAsync();
        var token = System.Text.RegularExpressions.Regex
            .Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        return await c.PostAsync("/Cuenta/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Usuario"] = usuario,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }));
    }
}
