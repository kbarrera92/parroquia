using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
