using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;
using Testcontainers.PostgreSql;

namespace Parroquia.Pruebas.Humo;

/// <summary>Arranca la app real contra PostgreSQL en contenedor (requiere Docker en marcha). Una sola instancia para todas las pruebas web.</summary>
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
        // Muchas pruebas inician sesión desde la misma IP: el límite real (10/min) se prueba aparte con su propia configuración.
        Fijar("Seguridad__LoginPorMinuto", "1000");
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

    /// <summary>Cliente con cookies y sin seguir redirecciones.</summary>
    public HttpClient Cliente() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public const string PasswordUsuariosPrueba = "Clave-De-Prueba-123";

    /// <summary>Cliente con sesión iniciada como el administrador sembrado.</summary>
    public async Task<HttpClient> ClienteAdminAsync()
    {
        var c = Cliente();
        (await LoginAsync(c, UsuarioAdmin, PasswordAdmin)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        return c;
    }

    /// <summary>Crea un usuario sintético con el rol indicado y devuelve un cliente con su sesión.</summary>
    public async Task<HttpClient> ClienteConRolAsync(string rol)
    {
        var nombre = $"{rol}_{Guid.NewGuid():N}"[..24];
        using (var scope = Services.CreateScope())
        {
            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var u = new Usuario { UserName = nombre, NombreCompleto = "Prueba", Activo = true, LockoutEnabled = true };
            (await usuarios.CreateAsync(u, PasswordUsuariosPrueba)).Succeeded.Should().BeTrue();
            (await usuarios.AddToRoleAsync(u, rol)).Succeeded.Should().BeTrue();
        }

        var c = Cliente();
        (await LoginAsync(c, nombre, PasswordUsuariosPrueba)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        return c;
    }

    /// <summary>POST de formulario con token antiforgery tomado de <paramref name="paginaConToken"/>.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient c, string paginaConToken, string urlPost, Dictionary<string, string> campos)
    {
        campos["__RequestVerificationToken"] = await TokenAsync(c, paginaConToken);
        return await c.PostAsync(urlPost, new FormUrlEncodedContent(campos));
    }

    /// <summary>Inicia sesión por el formulario real. Devuelve la respuesta del POST.</summary>
    public static async Task<HttpResponseMessage> LoginAsync(HttpClient c, string usuario, string password)
    {
        var token = await TokenAsync(c, "/Cuenta/Login");
        return await c.PostAsync("/Cuenta/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Usuario"] = usuario,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token,
        }));
    }

    public static async Task<HttpResponseMessage> LogoutAsync(HttpClient c)
    {
        var token = await TokenAsync(c, "/Cuenta/Logout");
        return await c.PostAsync("/Cuenta/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        }));
    }

    /// <summary>Token antiforgery del formulario de la página indicada.</summary>
    public static async Task<string> TokenAsync(HttpClient c, string url)
    {
        var pagina = await c.GetAsync(url);
        pagina.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await pagina.Content.ReadAsStringAsync();
        return Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    }
}

[CollectionDefinition(Nombre)]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
    public const string Nombre = "App";
}
