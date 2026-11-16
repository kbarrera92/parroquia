using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Pruebas.Humo;

/// <summary>Pantalla Administración → Perfiles. Los cambios de permisos se restauran: la base web se comparte.</summary>
[Collection(AppCollection.Nombre)]
public class PerfilesWebPruebas(AppFixture app)
{
    private const string Pagina = "/Admin/Perfiles";

    private static async Task<string> HtmlAsync(HttpClient c, string url) =>
        WebUtility.HtmlDecode(await (await c.GetAsync(url)).Content.ReadAsStringAsync());

    [Theory]
    [InlineData(Roles.Capturista)]
    [InlineData(Roles.Secretaria)]
    public async Task Solo_el_administrador_entra_a_perfiles(string rol)
    {
        var c = await app.ClienteConRolAsync(rol);

        var r = await c.GetAsync(Pagina);

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("AccesoDenegado");
        var post = await AppFixture.PostFormAsync(c, "/", Pagina, new() { ["rol"] = Roles.Capturista, ["permisos"] = Permisos.RegistrosVer });
        post.Headers.Location!.ToString().Should().Contain("AccesoDenegado");
    }

    [Fact]
    public async Task El_administrador_ve_los_perfiles_con_el_suyo_fijo_y_el_enlace_en_el_menu()
    {
        var a = await app.ClienteAdminAsync();

        var html = await HtmlAsync(a, Pagina);

        html.Should().Contain("Capturista").And.Contain("Secretaria").And.Contain("Administrador")
            .And.Contain("Transcribir registros de un libro físico").And.Contain("Asentar sacramentos nuevos")
            .And.Contain("no se puede cambiar");
        (await HtmlAsync(a, "/")).Should().Contain("Perfiles y permisos");
    }

    [Fact]
    public async Task Quitar_un_permiso_a_un_perfil_se_aplica_a_quien_entre_despues_y_se_puede_restaurar()
    {
        var admin = await app.ClienteAdminAsync();
        var original = MatrizPermisos.De(Roles.Capturista).ToList();
        var id = await CrearComunionAsync(admin);
        try
        {
            // Guarda el perfil Capturista sin «Anular».
            var sinAnular = original.Where(p => p != Permisos.RegistrosAnular).ToList();
            var r = await Guardar(admin, Roles.Capturista, sinAnular);
            r.StatusCode.Should().Be(HttpStatusCode.Redirect);
            (await HtmlAsync(admin, Pagina)).Should().Contain("Se guardaron los permisos de Capturista");

            using (var scope = app.Services.CreateScope())
            {
                var perfiles = await scope.ServiceProvider.GetRequiredService<ServicioPerfiles>().ListarAsync();
                perfiles.Single(p => p.Rol == Roles.Capturista).Permisos.Should().BeEquivalentTo(sinAnular);
            }

            // Un capturista que entra ahora ya no puede anular (el servidor lo exige, no solo la pantalla).
            var capturista = await app.ClienteConRolAsync(Roles.Capturista);
            var anular = await AppFixture.PostFormAsync(capturista, "/Cuenta/Logout", $"/Comunion/Detalle/{id}?handler=Anular",
                new() { ["motivo"] = "No debería poder" });
            anular.Headers.Location!.ToString().Should().Contain("AccesoDenegado");
        }
        finally
        {
            (await Guardar(admin, Roles.Capturista, original)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        }

        using var s = app.Services.CreateScope();
        (await s.ServiceProvider.GetRequiredService<ServicioPerfiles>().ListarAsync())
            .Single(p => p.Rol == Roles.Capturista).Permisos.Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task Intentar_darle_un_permiso_reservado_o_cambiar_al_administrador_muestra_el_error_y_no_cambia_nada()
    {
        var admin = await app.ClienteAdminAsync();

        await Guardar(admin, Roles.Secretaria, [Permisos.RegistrosVer, Permisos.UsuariosGestionar]);
        (await HtmlAsync(admin, Pagina)).Should().Contain("solo del Administrador");

        await Guardar(admin, Roles.Administrador, [Permisos.RegistrosVer]);
        (await HtmlAsync(admin, Pagina)).Should().Contain("siempre tiene todos los permisos");

        using var s = app.Services.CreateScope();
        var perfiles = await s.ServiceProvider.GetRequiredService<ServicioPerfiles>().ListarAsync();
        perfiles.Single(p => p.Rol == Roles.Secretaria).Permisos.Should().BeEquivalentTo(MatrizPermisos.De(Roles.Secretaria));
        perfiles.Single(p => p.Rol == Roles.Administrador).Permisos.Should().BeEquivalentTo(Permisos.Todos);
    }

    private static async Task<long> CrearComunionAsync(HttpClient c)
    {
        var u = Guid.NewGuid().ToString("N")[..8];
        var r = await AppFixture.PostFormAsync(c, "/Comunion/Editar", "/Comunion/Editar", new()
        {
            ["Input.Libro"] = "PF" + u, ["Input.Folio"] = "1", ["Input.Partida"] = "1",
            ["Input.Nombres"] = "Perfil", ["Input.Apellidos"] = "Prueba" + u, ["Input.Fecha"] = "2/10/2004",
            ["Input.Ministro"] = "", ["Input.NotasMarginales"] = "", ["Input.Revisar"] = "false",
            ["Input.MotivoRevision"] = "", ["Input.Version"] = "0", ["accion"] = "guardar",
        });
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return long.Parse(System.Text.RegularExpressions.Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> Guardar(HttpClient admin, string rol, IEnumerable<string> permisos)
    {
        var token = await AppFixture.TokenAsync(admin, Pagina);
        var campos = new List<KeyValuePair<string, string>> { new("rol", rol), new("__RequestVerificationToken", token) };
        campos.AddRange(permisos.Select(p => new KeyValuePair<string, string>("permisos", p)));
        return await admin.PostAsync(Pagina, new FormUrlEncodedContent(campos));
    }
}
