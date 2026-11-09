using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class UsuariosWebPruebas(AppFixture app)
{
    private const string Lista = "/Admin/Usuarios";
    private const string Editar = "/Admin/Usuarios/Editar";

    private static async Task<string> TextoAsync(HttpResponseMessage r) =>
        WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private static string NombreUnico() => $"w_{Guid.NewGuid():N}"[..14];

    [Theory]
    [InlineData(Roles.Capturista)]
    [InlineData(Roles.Secretaria)]
    public async Task Solo_el_administrador_entra_a_usuarios(string rol)
    {
        var c = await app.ClienteConRolAsync(rol);

        (await c.GetAsync(Lista)).StatusCode.Should().Be(HttpStatusCode.Redirect);   // AccesoDenegado
        (await c.GetAsync(Editar)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await TextoAsync(await c.GetAsync("/"))).Should().NotContain("/Admin/Usuarios");
    }

    [Fact]
    public async Task Sin_sesion_va_al_login()
    {
        var r = await app.Cliente().GetAsync(Lista);

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.OriginalString.Should().Contain("/Cuenta/Login");
    }

    [Fact]
    public async Task El_administrador_ve_el_menu_y_la_lista()
    {
        var c = await app.ClienteAdminAsync();

        var inicio = await TextoAsync(await c.GetAsync("/"));
        inicio.Should().Contain("/Admin/Usuarios");

        var lista = await c.GetAsync(Lista);
        lista.StatusCode.Should().Be(HttpStatusCode.OK);
        lista.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await TextoAsync(lista)).Should().Contain(AppFixture.UsuarioAdmin);
    }

    [Fact]
    public async Task Crear_muestra_la_temporal_una_sola_vez_y_el_nuevo_usuario_debe_cambiarla()
    {
        var admin = await app.ClienteAdminAsync();
        var nombre = NombreUnico();

        var crear = await AppFixture.PostFormAsync(admin, Editar, Editar, new Dictionary<string, string>
        {
            ["Usuario"] = nombre, ["NombreCompleto"] = "Persona de Prueba", ["Rol"] = Roles.Secretaria,
        });
        crear.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var vista = await TextoAsync(await admin.GetAsync(crear.Headers.Location!.OriginalString));
        var temporal = Regex.Match(vista, "<code[^>]*>([^<]+)</code>").Groups[1].Value;
        temporal.Should().HaveLength(GeneradorContrasenaTemporal.Longitud);
        vista.Should().Contain(nombre);

        (await TextoAsync(await admin.GetAsync(Lista))).Should().NotContain(temporal);   // ya no se muestra

        var nuevo = app.Cliente();
        var login = await AppFixture.LoginAsync(nuevo, nombre, temporal);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.Should().StartWith("/Cuenta/CambiarPassword");
    }

    [Fact]
    public async Task Crear_con_datos_invalidos_o_repetidos_muestra_el_error_y_no_crea()
    {
        var admin = await app.ClienteAdminAsync();

        var corto = await AppFixture.PostFormAsync(admin, Editar, Editar, new Dictionary<string, string>
        {
            ["Usuario"] = "ab", ["NombreCompleto"] = "X", ["Rol"] = Roles.Capturista,
        });
        corto.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TextoAsync(corto)).Should().Contain("de 3 a 50 caracteres");

        var repetido = await AppFixture.PostFormAsync(admin, Editar, Editar, new Dictionary<string, string>
        {
            ["Usuario"] = AppFixture.UsuarioAdmin.ToUpperInvariant(), ["NombreCompleto"] = "X", ["Rol"] = Roles.Capturista,
        });
        (await TextoAsync(repetido)).Should().Contain("ya existe");
    }

    [Fact]
    public async Task Desactivar_impide_el_inicio_de_sesion_y_activar_lo_devuelve()
    {
        var admin = await app.ClienteAdminAsync();
        var nombre = NombreUnico();
        var id = await CrearAsync(admin, nombre, Roles.Capturista);
        const string password = "Clave-De-Prueba-123";
        await FijarPasswordAsync(id, password);

        await PostEstadoAsync(admin, id, activo: false);
        (await AppFixture.LoginAsync(app.Cliente(), nombre, password)).StatusCode.Should().Be(HttpStatusCode.OK);

        await PostEstadoAsync(admin, id, activo: true);
        (await AppFixture.LoginAsync(app.Cliente(), nombre, password)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task El_administrador_no_puede_desactivarse_ni_restablecerse_a_si_mismo()
    {
        var admin = await app.ClienteAdminAsync();
        string idAdmin;
        using (var scope = app.Services.CreateScope())
            idAdmin = (await scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByNameAsync(AppFixture.UsuarioAdmin))!.Id;

        var estado = await PostEstadoAsync(admin, idAdmin, activo: false);
        (await TextoAsync(await admin.GetAsync(estado.Headers.Location!.OriginalString))).Should().Contain("No puede desactivar su propia cuenta");

        var reset = await AppFixture.PostFormAsync(admin, Lista, $"{Lista}?handler=Restablecer&id={idAdmin}", []);
        (await TextoAsync(await admin.GetAsync(reset.Headers.Location!.OriginalString))).Should().Contain("Para cambiar su propia contraseña");
    }

    [Fact]
    public async Task Restablecer_genera_temporal_que_obliga_a_cambiar()
    {
        var admin = await app.ClienteAdminAsync();
        var nombre = NombreUnico();
        var id = await CrearAsync(admin, nombre, Roles.Capturista);

        var r = await AppFixture.PostFormAsync(admin, Lista, $"{Lista}?handler=Restablecer&id={id}", []);
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var vista = await TextoAsync(await admin.GetAsync(r.Headers.Location!.OriginalString));
        var temporal = Regex.Match(vista, "<code[^>]*>([^<]+)</code>").Groups[1].Value;

        var login = await AppFixture.LoginAsync(app.Cliente(), nombre, temporal);
        login.Headers.Location!.OriginalString.Should().StartWith("/Cuenta/CambiarPassword");
    }

    [Fact]
    public async Task Editar_cambia_el_rol_y_los_permisos_cambian()
    {
        var admin = await app.ClienteAdminAsync();
        var nombre = NombreUnico();
        var id = await CrearAsync(admin, nombre, Roles.Capturista);

        var r = await AppFixture.PostFormAsync(admin, $"{Editar}?id={id}", $"{Editar}?id={id}", new Dictionary<string, string>
        {
            ["Id"] = id, ["NombreCompleto"] = "Nombre Editado", ["Rol"] = Roles.Secretaria,
        });

        r.StatusCode.Should().Be(HttpStatusCode.Redirect, because: Regex.Match(await TextoAsync(r), "alert-danger[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value);
        var lista = await TextoAsync(await admin.GetAsync($"{Lista}?Rol={Roles.Secretaria}"));
        lista.Should().Contain(nombre).And.Contain("Nombre Editado");
    }

    private async Task<string> CrearAsync(HttpClient admin, string nombre, string rol)
    {
        var r = await AppFixture.PostFormAsync(admin, Editar, Editar, new Dictionary<string, string>
        {
            ["Usuario"] = nombre, ["NombreCompleto"] = "Persona de Prueba", ["Rol"] = rol,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByNameAsync(nombre))!.Id;
    }

    /// <summary>Deja una contraseña conocida y sin cambio obligatorio, para probar el login sin pasar por la temporal.</summary>
    private async Task FijarPasswordAsync(string id, string password)
    {
        using var scope = app.Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var u = (await usuarios.FindByIdAsync(id))!;
        (await usuarios.ResetPasswordAsync(u, await usuarios.GeneratePasswordResetTokenAsync(u), password)).Succeeded.Should().BeTrue();
        u.DebeCambiarPassword = false;
        await usuarios.UpdateAsync(u);
    }

    private static Task<HttpResponseMessage> PostEstadoAsync(HttpClient admin, string id, bool activo) =>
        AppFixture.PostFormAsync(admin, Lista, $"{Lista}?handler=Estado&id={id}&activo={activo}", []);
}
