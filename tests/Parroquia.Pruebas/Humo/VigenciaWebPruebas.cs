using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Humo;

/// <summary>Cuentas temporales («Acceso hasta») desde la pantalla de usuarios y el inicio de sesión.</summary>
[Collection(AppCollection.Nombre)]
public class VigenciaWebPruebas(AppFixture app)
{
    private const string Lista = "/Admin/Usuarios";
    private const string Editar = "/Admin/Usuarios/Editar";

    private static DateOnly Hoy => HoraGuatemala.Hoy(TimeProvider.System);
    private static string NombreUnico() => $"v_{Guid.NewGuid():N}"[..14];

    private static async Task<string> TextoAsync(HttpResponseMessage r) =>
        WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    private async Task CrearDirectoAsync(string nombre, DateOnly? hasta)
    {
        using var scope = app.Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var u = new Usuario
        {
            UserName = nombre, NombreCompleto = "Temporal", Activo = true, LockoutEnabled = true, VigenteHasta = hasta,
        };
        (await usuarios.CreateAsync(u, AppFixture.PasswordUsuariosPrueba)).Succeeded.Should().BeTrue();
        (await usuarios.AddToRoleAsync(u, Roles.Capturista)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task El_administrador_crea_un_capturista_temporal_y_la_lista_muestra_su_fecha()
    {
        var admin = await app.ClienteAdminAsync();
        var nombre = NombreUnico();
        var hasta = Hoy.AddDays(30);

        var r = await AppFixture.PostFormAsync(admin, Editar, Editar, new()
        {
            ["Usuario"] = nombre, ["NombreCompleto"] = "Capturista Temporal", ["Rol"] = Roles.Capturista,
            ["VigenteHasta"] = hasta.ToString("yyyy-MM-dd"),
        });

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var lista = await TextoAsync(await admin.GetAsync($"{Lista}?Rol={Roles.Capturista}"));
        lista.Should().Contain(nombre).And.Contain(hasta.ToString("dd/MM/yyyy"));
    }

    [Fact]
    public async Task Una_fecha_pasada_se_rechaza_con_un_mensaje_claro_y_no_crea_el_usuario()
    {
        var admin = await app.ClienteAdminAsync();
        var nombre = NombreUnico();

        var r = await AppFixture.PostFormAsync(admin, Editar, Editar, new()
        {
            ["Usuario"] = nombre, ["NombreCompleto"] = "Capturista Temporal", ["Rol"] = Roles.Capturista,
            ["VigenteHasta"] = Hoy.AddDays(-1).ToString("yyyy-MM-dd"),
        });

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TextoAsync(r)).Should().Contain("anterior a hoy");
        using var scope = app.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByNameAsync(nombre)).Should().BeNull();
    }

    [Fact]
    public async Task La_cuenta_vencida_no_inicia_sesion_y_queda_el_motivo_en_la_bitacora()
    {
        var nombre = NombreUnico();
        await CrearDirectoAsync(nombre, Hoy.AddDays(-1));

        var login = await AppFixture.LoginAsync(app.Cliente(), nombre, AppFixture.PasswordUsuariosPrueba);

        login.StatusCode.Should().Be(HttpStatusCode.OK);   // vuelve al formulario con el mensaje genérico
        (await TextoAsync(login)).Should().Contain("Usuario o contraseña incorrectos");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        (await db.BitacoraAccesos.AsNoTracking()
            .Where(b => b.Evento == EventoAcceso.InicioSesionFallido && b.UsuarioNombre == nombre)
            .Select(b => b.Detalle).SingleAsync()).Should().Be("Cuenta vencida");
    }

    [Fact]
    public async Task El_ultimo_dia_de_vigencia_todavia_entra()
    {
        var nombre = NombreUnico();
        await CrearDirectoAsync(nombre, Hoy);

        var login = await AppFixture.LoginAsync(app.Cliente(), nombre, AppFixture.PasswordUsuariosPrueba);

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }
}
