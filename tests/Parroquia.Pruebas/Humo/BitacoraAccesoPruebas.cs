using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class BitacoraAccesoPruebas(AppFixture app)
{
    private async Task<List<BitacoraAcceso>> EntradasAsync(string usuarioNombre)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        return await db.BitacoraAccesos.AsNoTracking()
            .Where(b => b.UsuarioNombre == usuarioNombre).OrderBy(b => b.Id).ToListAsync();
    }

    private async Task<string> CrearUsuarioAsync(string prefijo, bool activo = true)
    {
        var nombre = $"{prefijo}_{Guid.NewGuid():N}"[..24];
        using var scope = app.Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var u = new Usuario { UserName = nombre, NombreCompleto = "Prueba", Activo = activo, LockoutEnabled = true };
        (await usuarios.CreateAsync(u, "Clave-De-Prueba-123")).Succeeded.Should().BeTrue();
        (await usuarios.AddToRoleAsync(u, Roles.Capturista)).Succeeded.Should().BeTrue();
        return nombre;
    }

    [Fact]
    public async Task Login_correcto_y_cierre_de_sesion_quedan_en_la_bitacora()
    {
        var nombre = await CrearUsuarioAsync("ok");
        var c = app.Cliente();

        (await AppFixture.LoginAsync(c, nombre, "Clave-De-Prueba-123")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await AppFixture.LogoutAsync(c)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var e = await EntradasAsync(nombre);
        e.Select(x => x.Evento).Should().Equal(EventoAcceso.InicioSesion, EventoAcceso.CierreSesion);
        e[0].UsuarioId.Should().NotBeNullOrEmpty();
        e[0].Fecha.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_fallido_se_registra_con_el_usuario_intentado_y_sin_la_contrasena()
    {
        var nombre = await CrearUsuarioAsync("mal");

        var r = await AppFixture.LoginAsync(app.Cliente(), nombre, "incorrecta-123456");
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var e = await EntradasAsync(nombre);
        e.Should().ContainSingle();
        e[0].Evento.Should().Be(EventoAcceso.InicioSesionFallido);
        e[0].Detalle.Should().NotContain("incorrecta-123456");
    }

    [Fact]
    public async Task Usuario_inexistente_tambien_queda_registrado_como_intento_fallido()
    {
        var nombre = $"fantasma_{Guid.NewGuid():N}"[..20];

        await AppFixture.LoginAsync(app.Cliente(), nombre, "cualquiera-123456");

        (await EntradasAsync(nombre)).Should().ContainSingle()
            .Which.Evento.Should().Be(EventoAcceso.InicioSesionFallido);
    }

    [Fact]
    public async Task Usuario_desactivado_no_entra_y_el_intento_se_registra()
    {
        var nombre = await CrearUsuarioAsync("off", activo: false);

        var r = await AppFixture.LoginAsync(app.Cliente(), nombre, "Clave-De-Prueba-123");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await EntradasAsync(nombre)).Should().ContainSingle()
            .Which.Evento.Should().Be(EventoAcceso.InicioSesionFallido);
    }

    [Fact]
    public async Task Intentos_repetidos_bloquean_la_cuenta_y_se_registra_el_bloqueo()
    {
        var nombre = await CrearUsuarioAsync("lock");
        var c = app.Cliente();

        for (var i = 0; i < 6; i++)
            await AppFixture.LoginAsync(c, nombre, "incorrecta-123456");

        var e = await EntradasAsync(nombre);
        e.Should().Contain(x => x.Evento == EventoAcceso.CuentaBloqueada);
        e.Count(x => x.Evento == EventoAcceso.InicioSesionFallido).Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task La_bitacora_no_se_puede_modificar_ni_borrar_desde_el_servicio()
    {
        // El servicio solo expone RegistrarAsync: append-only por diseño.
        typeof(IBitacoraAcceso).GetMethods().Select(m => m.Name).Should().Equal("RegistrarAsync");
        await Task.CompletedTask;
    }
}
