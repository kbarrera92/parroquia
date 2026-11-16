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

/// <summary>Cuentas temporales: «Acceso hasta» (último día, hora de Guatemala). Reloj falso: hoy = 7/11/2026.</summary>
public class VigenciaUsuarioPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private static readonly DateOnly Hoy = new(2026, 11, 7);

    // ---------- Regla pura ----------

    [Theory]
    [InlineData(true, null, true)]          // sin vencimiento
    [InlineData(true, "2026-11-07", true)]  // el último día todavía entra
    [InlineData(true, "2026-11-06", false)] // ayer venció
    [InlineData(false, null, false)]        // desactivado
    [InlineData(false, "2026-12-31", false)]
    public void Puede_ingresar_segun_estado_y_vigencia(bool activo, string? hasta, bool esperado)
    {
        var u = new Usuario { Activo = activo, VigenteHasta = hasta is null ? null : DateOnly.Parse(hasta) };

        u.PuedeIngresar(Hoy).Should().Be(esperado);
        u.Vencida(Hoy).Should().Be(hasta is not null && DateOnly.Parse(hasta) < Hoy);
    }

    [Theory]
    [InlineData("2026-11-08T05:59:00Z", true)]   // 23:59 del 7/11 en Guatemala: aún vigente
    [InlineData("2026-11-08T06:00:00Z", false)]  // 00:00 del 8/11 en Guatemala: vencida
    public async Task El_dia_se_cuenta_en_hora_de_Guatemala_no_en_UTC(string instante, bool puede)
    {
        var reloj = new RelojFalso { Ahora = DateTimeOffset.Parse(instante) };
        var confirmacion = new ConfirmacionUsuarioActivo(reloj);
        var u = new Usuario { Activo = true, VigenteHasta = Hoy };

        (await confirmacion.IsConfirmedAsync(null!, u)).Should().Be(puede);
    }

    // ---------- Servicio y sesión (base real) ----------

    private sealed class Entorno(ServiceProvider proveedor, RelojFalso reloj) : IAsyncDisposable
    {
        public RelojFalso Reloj { get; } = reloj;
        public IServiceScope Alcance { get; } = proveedor.CreateScope();
        public ServicioUsuarios Servicio => Alcance.ServiceProvider.GetRequiredService<ServicioUsuarios>();
        public UserManager<Usuario> Usuarios => Alcance.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        public ParroquiaDbContext Db => Alcance.ServiceProvider.GetRequiredService<ParroquiaDbContext>();

        public async Task<Usuario> CrearAsync(string rol, DateOnly? hasta = null)
        {
            var u = new Usuario
            {
                UserName = $"{rol}_{Guid.NewGuid():N}"[..20].ToLowerInvariant(), NombreCompleto = "Prueba",
                Activo = true, LockoutEnabled = true, VigenteHasta = hasta,
            };
            (await Usuarios.CreateAsync(u, "Clave-De-Prueba-123")).Succeeded.Should().BeTrue();
            (await Usuarios.AddToRoleAsync(u, rol)).Succeeded.Should().BeTrue();
            return u;
        }

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
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Parroquia"] = bd.Cadena }).Build();
        var reloj = new RelojFalso();

        var servicios = new ServiceCollection();
        servicios.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        servicios.AddDataProtection();
        servicios.AddHttpContextAccessor();
        servicios.AddSingleton<IConfiguration>(config);
        servicios.AddInfraestructura(config);
        servicios.AddSingleton<IUsuarioActual>(new UsuarioFalso { Id = null, Nombre = "sistema" });
        servicios.AddSingleton<TimeProvider>(reloj);   // el último registro gana

        var proveedor = servicios.BuildServiceProvider();
        using (var scope = proveedor.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var rol in Roles.Todos)
                (await roles.CreateAsync(new IdentityRole(rol))).Succeeded.Should().BeTrue();
        }
        return new Entorno(proveedor, reloj);
    }

    [Fact]
    public async Task Crear_con_vigencia_la_guarda_y_la_deja_en_bitacora()
    {
        await using var e = await NuevoEntornoAsync();

        var r = await e.Servicio.CrearAsync("temporal1", "Cap Temporal", Roles.Capturista, new DateOnly(2027, 3, 31));

        r.Ok.Should().BeTrue(r.Error);
        (await e.Usuarios.FindByNameAsync("temporal1"))!.VigenteHasta.Should().Be(new DateOnly(2027, 3, 31));
        var fila = (await e.Servicio.ListarAsync()).Single();
        fila.VigenteHasta.Should().Be(new DateOnly(2027, 3, 31));
        fila.Vencida.Should().BeFalse();
        (await e.Db.BitacoraAccesos.AsNoTracking().SingleAsync(b => b.Evento == EventoAcceso.UsuarioCreado))
            .Detalle.Should().Contain("acceso hasta=31/03/2027");
    }

    [Fact]
    public async Task Crear_rechaza_una_fecha_pasada_y_la_del_Administrador()
    {
        await using var e = await NuevoEntornoAsync();

        var pasada = await e.Servicio.CrearAsync("pasada", "Nombre", Roles.Capturista, Hoy.AddDays(-1));
        var admin = await e.Servicio.CrearAsync("adminv", "Nombre", Roles.Administrador, Hoy.AddDays(30));
        var hoy = await e.Servicio.CrearAsync("hoyvale", "Nombre", Roles.Capturista, Hoy);

        pasada.Ok.Should().BeFalse();
        pasada.Error.Should().Contain("anterior a hoy");
        admin.Ok.Should().BeFalse();
        admin.Error.Should().Contain("Administrador");
        hoy.Ok.Should().BeTrue(hoy.Error);
        (await e.Usuarios.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Editar_cambia_extiende_y_quita_la_vigencia_con_bitacora()
    {
        await using var e = await NuevoEntornoAsync();
        var u = await e.CrearAsync(Roles.Capturista);

        (await e.Servicio.EditarAsync(u.Id, "Prueba", Roles.Capturista, new DateOnly(2027, 1, 15))).Ok.Should().BeTrue();
        (await e.Servicio.EditarAsync(u.Id, "Prueba", Roles.Capturista, new DateOnly(2027, 6, 30))).Ok.Should().BeTrue();
        (await e.Servicio.EditarAsync(u.Id, "Prueba", Roles.Capturista, null)).Ok.Should().BeTrue();

        (await e.Usuarios.FindByIdAsync(u.Id))!.VigenteHasta.Should().BeNull();
        var detalles = await e.Db.BitacoraAccesos.AsNoTracking().Where(b => b.Evento == EventoAcceso.UsuarioModificado)
            .OrderBy(b => b.Id).Select(b => b.Detalle!).ToListAsync();
        detalles.Should().HaveCount(3);
        detalles[0].Should().Contain("acceso hasta: (sin) → 15/01/2027");
        detalles[1].Should().Contain("15/01/2027 → 30/06/2027");
        detalles[2].Should().Contain("30/06/2027 → (sin)");
    }

    [Fact]
    public async Task Editar_rechaza_fecha_pasada_pero_acepta_conservar_una_ya_vencida()
    {
        await using var e = await NuevoEntornoAsync();
        var vencida = await e.CrearAsync(Roles.Capturista, Hoy.AddDays(-10));

        // Cambiar solo el nombre con la misma fecha (ya vencida) no debe bloquearse.
        (await e.Servicio.EditarAsync(vencida.Id, "Nombre Nuevo", Roles.Capturista, Hoy.AddDays(-10))).Ok.Should().BeTrue();
        // Pero no se puede poner otra fecha pasada.
        var r = await e.Servicio.EditarAsync(vencida.Id, "Nombre Nuevo", Roles.Capturista, Hoy.AddDays(-2));
        r.Ok.Should().BeFalse();
        (await e.Servicio.ObtenerAsync(vencida.Id))!.Vencida.Should().BeTrue();
    }

    [Fact]
    public async Task Un_usuario_con_vigencia_no_puede_pasar_a_Administrador_sin_quitarsela()
    {
        await using var e = await NuevoEntornoAsync();
        var u = await e.CrearAsync(Roles.Capturista, Hoy.AddDays(30));

        (await e.Servicio.EditarAsync(u.Id, "Prueba", Roles.Administrador, Hoy.AddDays(30))).Ok.Should().BeFalse();
        (await e.Servicio.EditarAsync(u.Id, "Prueba", Roles.Administrador, null)).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task La_sesion_abierta_se_invalida_al_vencer_o_desactivar_sin_tocar_el_sello()
    {
        await using var e = await NuevoEntornoAsync();
        var u = await e.CrearAsync(Roles.Capturista, Hoy);
        var sp = e.Alcance.ServiceProvider;
        var signIn = sp.GetRequiredService<SignInManager<Usuario>>();
        var principal = await sp.GetRequiredService<IUserClaimsPrincipalFactory<Usuario>>().CreateAsync(u);

        (await signIn.ValidateSecurityStampAsync(principal)).Should().NotBeNull("el último día todavía entra");

        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(1);   // 8/11 en Guatemala: venció
        (await signIn.ValidateSecurityStampAsync(principal)).Should().BeNull();

        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(-1);
        var fresco = (await e.Usuarios.FindByIdAsync(u.Id))!;
        fresco.Activo = false;
        (await e.Usuarios.UpdateAsync(fresco)).Succeeded.Should().BeTrue();
        (await signIn.ValidateSecurityStampAsync(principal)).Should().BeNull("desactivado");
    }
}
