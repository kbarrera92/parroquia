using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura;
using Parroquia.Infraestructura.Identidad;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Infraestructura;

public class RestablecimientoEmergenciaPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private const string PasswordNueva = "vela azul puente nube";

    private async Task<ServiceProvider> NuevoProveedorAsync()
    {
        var bd = await servidor.NuevaBaseAsync();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Parroquia"] = bd.Cadena })
            .Build();

        var servicios = new ServiceCollection();
        servicios.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        servicios.AddDataProtection();
        servicios.AddSingleton<IConfiguration>(config);
        servicios.AddInfraestructura(config);
        servicios.AddSingleton<IUsuarioActual>(new UsuarioFalso { Id = null, Nombre = "sistema" });

        var proveedor = servicios.BuildServiceProvider();
        using var scope = proveedor.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var rol in Roles.Todos)
            (await roles.CreateAsync(new IdentityRole(rol))).Succeeded.Should().BeTrue();
        return proveedor;
    }

    private static async Task<Usuario> CrearAsync(IServiceProvider proveedor, string nombre, string rol, string password = "Clave-Anterior-123")
    {
        using var scope = proveedor.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var u = new Usuario { UserName = nombre, NombreCompleto = "Admin", Activo = true, LockoutEnabled = true };
        (await usuarios.CreateAsync(u, password)).Succeeded.Should().BeTrue();
        (await usuarios.AddToRoleAsync(u, rol)).Succeeded.Should().BeTrue();
        return u;
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void La_bandera_de_restablecimiento_se_lee_de_la_configuracion(string? valor, bool esperado)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SembradorIdentidad.VariableRestablecer] = valor })
            .Build();

        SembradorIdentidad.RestablecerSolicitado(config).Should().Be(esperado);
    }

    [Fact]
    public async Task Restablece_al_administrador_existente_lo_activa_desbloquea_y_exige_cambio()
    {
        await using var proveedor = await NuevoProveedorAsync();
        var admin = await CrearAsync(proveedor, "admin", Roles.Administrador);
        using (var scope = proveedor.CreateScope())
        {
            var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var u = (await usuarios.FindByIdAsync(admin.Id))!;
            u.Activo = false;
            await usuarios.UpdateAsync(u);
            await usuarios.SetLockoutEndDateAsync(u, DateTimeOffset.UtcNow.AddHours(1));
        }

        await SembradorIdentidad.RestablecerAdministradorAsync(proveedor, "admin", PasswordNueva);

        using var verificar = proveedor.CreateScope();
        var um = verificar.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var luego = (await um.FindByIdAsync(admin.Id))!;
        (await um.CheckPasswordAsync(luego, PasswordNueva)).Should().BeTrue();
        (await um.CheckPasswordAsync(luego, "Clave-Anterior-123")).Should().BeFalse();
        luego.Activo.Should().BeTrue();
        luego.DebeCambiarPassword.Should().BeTrue();
        (await um.IsLockedOutAsync(luego)).Should().BeFalse();
        luego.SecurityStamp.Should().NotBe(admin.SecurityStamp);

        var db = verificar.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        var bitacora = await db.BitacoraAccesos.AsNoTracking().Where(b => b.Evento == EventoAcceso.PasswordRestablecida).ToListAsync();
        bitacora.Should().ContainSingle().Which.Detalle.Should().Contain("emergencia").And.NotContain(PasswordNueva);
    }

    [Fact]
    public async Task Rechaza_una_contrasena_que_incumple_la_politica_y_no_cambia_nada()
    {
        await using var proveedor = await NuevoProveedorAsync();
        await CrearAsync(proveedor, "admin", Roles.Administrador);

        var accion = () => SembradorIdentidad.RestablecerAdministradorAsync(proveedor, "admin", "corta");

        (await accion.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("12");
        using var scope = proveedor.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        (await um.CheckPasswordAsync((await um.FindByNameAsync("admin"))!, "Clave-Anterior-123")).Should().BeTrue();
    }

    [Fact]
    public async Task No_asciende_a_un_usuario_que_no_es_administrador()
    {
        await using var proveedor = await NuevoProveedorAsync();
        await CrearAsync(proveedor, "cap1", Roles.Capturista);

        var accion = () => SembradorIdentidad.RestablecerAdministradorAsync(proveedor, "cap1", PasswordNueva);

        (await accion.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Administrador");
    }

    [Fact]
    public async Task Si_el_usuario_no_existe_crea_un_administrador_con_cambio_obligatorio()
    {
        await using var proveedor = await NuevoProveedorAsync();

        await SembradorIdentidad.RestablecerAdministradorAsync(proveedor, "admin2", PasswordNueva);

        using var scope = proveedor.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var u = (await um.FindByNameAsync("admin2"))!;
        (await um.IsInRoleAsync(u, Roles.Administrador)).Should().BeTrue();
        u.DebeCambiarPassword.Should().BeTrue();
        (await um.CheckPasswordAsync(u, PasswordNueva)).Should().BeTrue();
    }
}
