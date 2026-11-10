using Microsoft.AspNetCore.DataProtection;
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

public class ServicioUsuariosPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private const string PasswordInicial = "Clave-De-Prueba-123";

    /// <summary>Servicios reales de Identity + ServicioUsuarios sobre una base nueva; "actor" es quien administra.</summary>
    private sealed class Entorno(ServiceProvider proveedor, UsuarioFalso actor) : IAsyncDisposable
    {
        public UsuarioFalso Actor { get; } = actor;
        public IServiceScope Alcance { get; } = proveedor.CreateScope();
        public ServicioUsuarios Servicio => Alcance.ServiceProvider.GetRequiredService<ServicioUsuarios>();
        public UserManager<Usuario> Usuarios => Alcance.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        public ParroquiaDbContext Db => Alcance.ServiceProvider.GetRequiredService<ParroquiaDbContext>();

        public async Task<Usuario> CrearAsync(string rol, bool activo = true, string? nombre = null)
        {
            var u = new Usuario
            {
                UserName = nombre ?? $"{rol}_{Guid.NewGuid():N}"[..20].ToLowerInvariant(),
                NombreCompleto = "Prueba", Activo = activo, LockoutEnabled = true,
            };
            (await Usuarios.CreateAsync(u, PasswordInicial)).Succeeded.Should().BeTrue();
            (await Usuarios.AddToRoleAsync(u, rol)).Succeeded.Should().BeTrue();
            return u;
        }

        public async Task<List<BitacoraAcceso>> BitacoraAsync(EventoAcceso evento) =>
            await Db.BitacoraAccesos.AsNoTracking().Where(b => b.Evento == evento).OrderBy(b => b.Id).ToListAsync();

        public async ValueTask DisposeAsync()
        {
            Alcance.Dispose();
            await proveedor.DisposeAsync();
        }
    }

    private async Task<Entorno> NuevoEntornoAsync()
    {
        var bd = await servidor.NuevaBaseAsync();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Parroquia"] = bd.Cadena })
            .Build();

        var actor = new UsuarioFalso { Id = null, Nombre = "sistema" };
        var servicios = new ServiceCollection();
        servicios.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        servicios.AddDataProtection();
        servicios.AddSingleton<IConfiguration>(config);
        servicios.AddInfraestructura(config);
        servicios.AddSingleton<IUsuarioActual>(actor);

        var proveedor = servicios.BuildServiceProvider();
        using (var scope = proveedor.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var rol in Roles.Todos)
                (await roles.CreateAsync(new IdentityRole(rol))).Succeeded.Should().BeTrue();
        }
        return new Entorno(proveedor, actor);
    }

    [Fact]
    public async Task Crear_devuelve_temporal_valida_exige_cambio_y_deja_bitacora_sin_contrasena()
    {
        await using var e = await NuevoEntornoAsync();

        var r = await e.Servicio.CrearAsync("  MPerez ", "María Pérez", Roles.Capturista);

        r.Ok.Should().BeTrue(r.Error);
        r.Temporal.Should().HaveLength(GeneradorContrasenaTemporal.Longitud);
        var u = await e.Usuarios.FindByNameAsync("mperez");
        u!.DebeCambiarPassword.Should().BeTrue();
        u.Activo.Should().BeTrue();
        u.NombreCompleto.Should().Be("María Pérez");
        u.CreadoEn.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        (await e.Usuarios.IsInRoleAsync(u, Roles.Capturista)).Should().BeTrue();
        (await e.Usuarios.CheckPasswordAsync(u, r.Temporal!)).Should().BeTrue();

        var bitacora = await e.BitacoraAsync(EventoAcceso.UsuarioCreado);
        bitacora.Should().ContainSingle().Which.Detalle.Should().Contain("mperez").And.NotContain(r.Temporal!);
    }

    [Theory]
    [InlineData("ab", "Nombre", Roles.Capturista)]
    [InlineData("con espacio", "Nombre", Roles.Capturista)]
    [InlineData("ñandu", "Nombre", Roles.Capturista)]
    [InlineData("valido", "  ", Roles.Capturista)]
    [InlineData("valido", "Nombre", "Superusuario")]
    public async Task Crear_rechaza_datos_invalidos(string usuario, string nombre, string rol)
    {
        await using var e = await NuevoEntornoAsync();

        var r = await e.Servicio.CrearAsync(usuario, nombre, rol);

        r.Ok.Should().BeFalse();
        r.Error.Should().NotBeNullOrWhiteSpace();
        r.Temporal.Should().BeNull();
        (await e.Usuarios.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Crear_rechaza_usuario_repetido_ignorando_mayusculas()
    {
        await using var e = await NuevoEntornoAsync();
        (await e.Servicio.CrearAsync("repetido", "Uno", Roles.Secretaria)).Ok.Should().BeTrue();

        var r = await e.Servicio.CrearAsync("REPETIDO", "Dos", Roles.Capturista);

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("ya existe");
    }

    [Fact]
    public async Task Editar_cambia_nombre_y_rol_invalida_sesiones_y_registra_la_diferencia()
    {
        await using var e = await NuevoEntornoAsync();
        var u = await e.CrearAsync(Roles.Capturista);
        var sello = u.SecurityStamp;

        var r = await e.Servicio.EditarAsync(u.Id, "Nombre Nuevo", Roles.Secretaria);

        r.Ok.Should().BeTrue(r.Error);
        var luego = await e.Usuarios.FindByIdAsync(u.Id);
        luego!.NombreCompleto.Should().Be("Nombre Nuevo");
        (await e.Usuarios.GetRolesAsync(luego)).Should().Equal(Roles.Secretaria);
        luego.SecurityStamp.Should().NotBe(sello);

        (await e.BitacoraAsync(EventoAcceso.UsuarioModificado)).Should().ContainSingle()
            .Which.Detalle.Should().Contain("Capturista").And.Contain("Secretaria");
    }

    [Fact]
    public async Task Editar_solo_el_nombre_no_invalida_sesiones()
    {
        await using var e = await NuevoEntornoAsync();
        var u = await e.CrearAsync(Roles.Capturista);
        var sello = u.SecurityStamp;

        (await e.Servicio.EditarAsync(u.Id, "Otro Nombre", Roles.Capturista)).Ok.Should().BeTrue();

        (await e.Usuarios.FindByIdAsync(u.Id))!.SecurityStamp.Should().Be(sello);
    }

    [Fact]
    public async Task Nadie_puede_cambiarse_el_propio_rol_ni_desactivarse()
    {
        await using var e = await NuevoEntornoAsync();
        var yo = await e.CrearAsync(Roles.Administrador);
        await e.CrearAsync(Roles.Administrador);   // hay otro admin: la regla es "no a uno mismo", no "último admin"
        e.Actor.Id = yo.Id;

        (await e.Servicio.EditarAsync(yo.Id, "Yo", Roles.Capturista)).Ok.Should().BeFalse();
        (await e.Servicio.CambiarEstadoAsync(yo.Id, activo: false)).Ok.Should().BeFalse();
        (await e.Servicio.RestablecerPasswordAsync(yo.Id)).Ok.Should().BeFalse();

        (await e.Usuarios.IsInRoleAsync(yo, Roles.Administrador)).Should().BeTrue();
        (await e.Usuarios.FindByIdAsync(yo.Id))!.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task Nunca_deja_el_sistema_sin_administrador_activo()
    {
        await using var e = await NuevoEntornoAsync();
        var unico = await e.CrearAsync(Roles.Administrador);
        await e.CrearAsync(Roles.Administrador, activo: false);   // un admin desactivado no cuenta
        e.Actor.Id = "otro-actor";

        (await e.Servicio.CambiarEstadoAsync(unico.Id, activo: false)).Ok.Should().BeFalse();
        (await e.Servicio.EditarAsync(unico.Id, "X", Roles.Secretaria)).Ok.Should().BeFalse();

        var segundo = await e.CrearAsync(Roles.Administrador);
        (await e.Servicio.CambiarEstadoAsync(unico.Id, activo: false)).Ok.Should().BeTrue();
        (await e.Servicio.CambiarEstadoAsync(segundo.Id, activo: false)).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Desactivar_y_activar_invalidan_sesiones_y_quedan_en_la_bitacora()
    {
        await using var e = await NuevoEntornoAsync();
        var u = await e.CrearAsync(Roles.Secretaria);
        var sello = u.SecurityStamp;

        (await e.Servicio.CambiarEstadoAsync(u.Id, activo: false)).Ok.Should().BeTrue();
        var apagado = await e.Usuarios.FindByIdAsync(u.Id);
        apagado!.Activo.Should().BeFalse();
        apagado.SecurityStamp.Should().NotBe(sello);

        (await e.Servicio.CambiarEstadoAsync(u.Id, activo: true)).Ok.Should().BeTrue();
        (await e.Usuarios.FindByIdAsync(u.Id))!.Activo.Should().BeTrue();

        (await e.BitacoraAsync(EventoAcceso.UsuarioDesactivado)).Should().ContainSingle();
        (await e.BitacoraAsync(EventoAcceso.UsuarioActivado)).Should().ContainSingle();
    }

    [Fact]
    public async Task Restablecer_da_temporal_nueva_exige_cambio_desbloquea_y_cierra_sesiones()
    {
        await using var e = await NuevoEntornoAsync();
        var u = await e.CrearAsync(Roles.Capturista);
        var sello = u.SecurityStamp;
        await e.Usuarios.SetLockoutEndDateAsync(u, DateTimeOffset.UtcNow.AddMinutes(10));

        var r = await e.Servicio.RestablecerPasswordAsync(u.Id);

        r.Ok.Should().BeTrue(r.Error);
        var luego = await e.Usuarios.FindByIdAsync(u.Id);
        (await e.Usuarios.CheckPasswordAsync(luego!, r.Temporal!)).Should().BeTrue();
        (await e.Usuarios.CheckPasswordAsync(luego!, PasswordInicial)).Should().BeFalse();
        luego!.DebeCambiarPassword.Should().BeTrue();
        luego.SecurityStamp.Should().NotBe(sello);
        (await e.Usuarios.IsLockedOutAsync(luego)).Should().BeFalse();

        (await e.BitacoraAsync(EventoAcceso.PasswordRestablecida)).Should().ContainSingle()
            .Which.Detalle.Should().NotContain(r.Temporal!);
    }

    [Fact]
    public async Task Desbloquear_quita_el_bloqueo_y_los_intentos_fallidos()
    {
        await using var e = await NuevoEntornoAsync();
        var u = await e.CrearAsync(Roles.Capturista);
        await e.Usuarios.SetLockoutEndDateAsync(u, DateTimeOffset.UtcNow.AddMinutes(10));
        await e.Usuarios.AccessFailedAsync(u);

        (await e.Servicio.DesbloquearAsync(u.Id)).Ok.Should().BeTrue();

        var luego = await e.Usuarios.FindByIdAsync(u.Id);
        (await e.Usuarios.IsLockedOutAsync(luego!)).Should().BeFalse();
        (await e.Usuarios.GetAccessFailedCountAsync(luego!)).Should().Be(0);
        (await e.BitacoraAsync(EventoAcceso.CuentaDesbloqueada)).Should().ContainSingle();
    }

    [Fact]
    public async Task Listar_muestra_rol_estado_bloqueo_y_ultimo_inicio_y_filtra()
    {
        await using var e = await NuevoEntornoAsync();
        var cap = await e.CrearAsync(Roles.Capturista);
        var sec = await e.CrearAsync(Roles.Secretaria, activo: false);
        await e.Usuarios.SetLockoutEndDateAsync(cap, DateTimeOffset.UtcNow.AddMinutes(10));
        e.Db.BitacoraAccesos.Add(new BitacoraAcceso
        {
            Fecha = DateTimeOffset.UtcNow.AddHours(-1), UsuarioId = cap.Id, UsuarioNombre = cap.UserName!,
            Evento = EventoAcceso.InicioSesion,
        });
        await e.Db.SaveChangesAsync();

        var todos = await e.Servicio.ListarAsync();
        todos.Should().HaveCount(2);
        var filaCap = todos.Single(f => f.Id == cap.Id);
        filaCap.Rol.Should().Be(Roles.Capturista);
        filaCap.Bloqueado.Should().BeTrue();
        filaCap.UltimoInicio.Should().NotBeNull();
        todos.Single(f => f.Id == sec.Id).Activo.Should().BeFalse();

        (await e.Servicio.ListarAsync(rol: Roles.Secretaria)).Should().ContainSingle().Which.Id.Should().Be(sec.Id);
        (await e.Servicio.ListarAsync(activo: true)).Should().ContainSingle().Which.Id.Should().Be(cap.Id);
    }

    [Fact]
    public async Task Operaciones_sobre_un_usuario_inexistente_fallan_con_mensaje()
    {
        await using var e = await NuevoEntornoAsync();

        (await e.Servicio.EditarAsync("no-existe", "X", Roles.Capturista)).Ok.Should().BeFalse();
        (await e.Servicio.CambiarEstadoAsync("no-existe", false)).Ok.Should().BeFalse();
        (await e.Servicio.RestablecerPasswordAsync("no-existe")).Ok.Should().BeFalse();
        (await e.Servicio.DesbloquearAsync("no-existe")).Ok.Should().BeFalse();
    }
}
