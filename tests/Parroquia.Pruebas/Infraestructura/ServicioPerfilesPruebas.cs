using System.Security.Claims;
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

/// <summary>Perfiles editables: matriz de permisos por rol guardada en la base (docs/04, ADR-05).</summary>
public class ServicioPerfilesPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private sealed class Entorno(ServiceProvider proveedor) : IAsyncDisposable
    {
        public ServiceProvider Proveedor { get; } = proveedor;
        public IServiceScope Alcance { get; } = proveedor.CreateScope();
        public ServicioPerfiles Servicio => Alcance.ServiceProvider.GetRequiredService<ServicioPerfiles>();
        public RoleManager<IdentityRole> Roles => Alcance.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        public UserManager<Usuario> Usuarios => Alcance.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        public ParroquiaDbContext Db => Alcance.ServiceProvider.GetRequiredService<ParroquiaDbContext>();

        public async Task<HashSet<string>> PermisosAsync(string rol)
        {
            var r = await Roles.FindByNameAsync(rol);
            return (await Roles.GetClaimsAsync(r!)).Where(c => c.Type == Permisos.TipoClaim).Select(c => c.Value).ToHashSet();
        }

        public async ValueTask DisposeAsync()
        {
            Alcance.Dispose();
            await Proveedor.DisposeAsync();
        }
    }

    /// <summary>Base nueva con la sincronización real de roles del arranque (crea los roles con la matriz inicial).</summary>
    private async Task<Entorno> NuevoEntornoAsync()
    {
        var bd = await servidor.NuevaBaseAsync();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Parroquia"] = bd.Cadena,
            })
            .Build();

        var servicios = new ServiceCollection();
        servicios.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        servicios.AddDataProtection();
        servicios.AddSingleton<IConfiguration>(config);
        servicios.AddInfraestructura(config);
        servicios.AddSingleton<IUsuarioActual>(new UsuarioFalso { Id = null, Nombre = "sistema" });

        var proveedor = servicios.BuildServiceProvider();
        using (var scope = proveedor.CreateScope())
            await SembradorIdentidad.SincronizarRolesAsync(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
        return new Entorno(proveedor);
    }

    [Fact]
    public async Task El_arranque_siembra_la_matriz_inicial_en_roles_nuevos()
    {
        await using var e = await NuevoEntornoAsync();

        (await e.PermisosAsync(Roles.Capturista)).Should().BeEquivalentTo(MatrizPermisos.De(Roles.Capturista));
        (await e.PermisosAsync(Roles.Secretaria)).Should().BeEquivalentTo(MatrizPermisos.De(Roles.Secretaria));
        (await e.PermisosAsync(Roles.Administrador)).Should().BeEquivalentTo(Permisos.Todos);
    }

    [Fact]
    public async Task Listar_devuelve_los_tres_perfiles_y_solo_el_administrador_no_es_editable()
    {
        await using var e = await NuevoEntornoAsync();

        var perfiles = await e.Servicio.ListarAsync();

        perfiles.Select(p => p.Rol).Should().Equal(Roles.Todos);
        perfiles.Single(p => p.Rol == Roles.Administrador).Editable.Should().BeFalse();
        perfiles.Where(p => p.Rol != Roles.Administrador).Should().OnlyContain(p => p.Editable);
        perfiles.Single(p => p.Rol == Roles.Capturista).Permisos.Should().BeEquivalentTo(MatrizPermisos.De(Roles.Capturista));
    }

    [Fact]
    public async Task Guardar_agrega_y_quita_permisos_y_deja_bitacora_con_la_diferencia()
    {
        await using var e = await NuevoEntornoAsync();
        var nuevos = MatrizPermisos.De(Roles.Secretaria).Where(p => p != Permisos.RegistrosAnular)
            .Append(Permisos.RegistrosTranscribir).ToList();

        var r = await e.Servicio.GuardarAsync(Roles.Secretaria, nuevos);

        r.Ok.Should().BeTrue(r.Error);
        (await e.PermisosAsync(Roles.Secretaria)).Should().BeEquivalentTo(nuevos);
        var b = await e.Db.BitacoraAccesos.AsNoTracking().Where(x => x.Evento == EventoAcceso.PermisosModificados).ToListAsync();
        b.Should().ContainSingle().Which.Detalle.Should()
            .Contain("rol=Secretaria").And.Contain("+Registros.Transcribir").And.Contain("-Registros.Anular");
    }

    [Fact]
    public async Task Guardar_sin_cambios_no_escribe_bitacora()
    {
        await using var e = await NuevoEntornoAsync();

        var r = await e.Servicio.GuardarAsync(Roles.Capturista, MatrizPermisos.De(Roles.Capturista));

        r.Ok.Should().BeTrue();
        (await e.Db.BitacoraAccesos.CountAsync(x => x.Evento == EventoAcceso.PermisosModificados)).Should().Be(0);
    }

    [Theory]
    [InlineData(Roles.Administrador, Permisos.RegistrosVer)]   // el Administrador no se edita
    [InlineData("Superusuario", Permisos.RegistrosVer)]        // rol inexistente
    [InlineData(Roles.Capturista, "Permiso.Inventado")]        // permiso desconocido
    [InlineData(Roles.Capturista, Permisos.UsuariosGestionar)] // reservado: nadie se da privilegios de administración
    public async Task Guardar_rechaza_roles_y_permisos_invalidos_sin_tocar_nada(string rol, string permiso)
    {
        await using var e = await NuevoEntornoAsync();
        var antes = await e.PermisosAsync(Roles.Capturista);

        var r = await e.Servicio.GuardarAsync(rol, [permiso]);

        r.Ok.Should().BeFalse();
        r.Error.Should().NotBeNullOrWhiteSpace();
        (await e.PermisosAsync(Roles.Capturista)).Should().BeEquivalentTo(antes);
        (await e.PermisosAsync(Roles.Administrador)).Should().BeEquivalentTo(Permisos.Todos);
        (await e.Db.BitacoraAccesos.CountAsync(x => x.Evento == EventoAcceso.PermisosModificados)).Should().Be(0);
    }

    [Fact]
    public async Task Guardar_acepta_un_perfil_sin_ningun_permiso()
    {
        await using var e = await NuevoEntornoAsync();

        var r = await e.Servicio.GuardarAsync(Roles.Capturista, []);

        r.Ok.Should().BeTrue(r.Error);
        (await e.PermisosAsync(Roles.Capturista)).Should().BeEmpty();
    }

    [Fact]
    public async Task Cambiar_los_permisos_actualiza_el_sello_de_los_usuarios_del_perfil_para_que_la_sesion_se_renueve()
    {
        await using var e = await NuevoEntornoAsync();
        var u = new Usuario { UserName = "cap_" + Guid.NewGuid().ToString("N")[..8], NombreCompleto = "Prueba", Activo = true, LockoutEnabled = true };
        (await e.Usuarios.CreateAsync(u, "Clave-De-Prueba-123")).Succeeded.Should().BeTrue();
        (await e.Usuarios.AddToRoleAsync(u, Roles.Capturista)).Succeeded.Should().BeTrue();
        var otro = new Usuario { UserName = "sec_" + Guid.NewGuid().ToString("N")[..8], NombreCompleto = "Otra", Activo = true, LockoutEnabled = true };
        (await e.Usuarios.CreateAsync(otro, "Clave-De-Prueba-123")).Succeeded.Should().BeTrue();
        (await e.Usuarios.AddToRoleAsync(otro, Roles.Secretaria)).Succeeded.Should().BeTrue();
        var selloCap = await e.Usuarios.GetSecurityStampAsync(u);
        var selloSec = await e.Usuarios.GetSecurityStampAsync(otro);

        (await e.Servicio.GuardarAsync(Roles.Capturista, [Permisos.RegistrosVer])).Ok.Should().BeTrue();

        (await e.Usuarios.GetSecurityStampAsync((await e.Usuarios.FindByIdAsync(u.Id))!)).Should().NotBe(selloCap);
        (await e.Usuarios.GetSecurityStampAsync((await e.Usuarios.FindByIdAsync(otro.Id))!)).Should().Be(selloSec);
    }

    [Fact]
    public async Task Reiniciar_no_pisa_los_cambios_del_administrador_pero_repone_todo_al_administrador_y_quita_lo_desconocido()
    {
        await using var e = await NuevoEntornoAsync();
        (await e.Servicio.GuardarAsync(Roles.Capturista, [Permisos.RegistrosVer, Permisos.RegistrosAnular])).Ok.Should().BeTrue();

        // Manipulación directa: al Administrador le falta un permiso y a la Secretaria le sobran uno reservado y uno viejo.
        var admin = (await e.Roles.FindByNameAsync(Roles.Administrador))!;
        await e.Roles.RemoveClaimAsync(admin, new Claim(Permisos.TipoClaim, Permisos.AuditoriaVer));
        var sec = (await e.Roles.FindByNameAsync(Roles.Secretaria))!;
        await e.Roles.AddClaimAsync(sec, new Claim(Permisos.TipoClaim, Permisos.UsuariosGestionar));
        await e.Roles.AddClaimAsync(sec, new Claim(Permisos.TipoClaim, "Registros.Crear"));

        using var otro = e.Proveedor.CreateScope();
        var roles = otro.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await SembradorIdentidad.SincronizarRolesAsync(roles);
        async Task<HashSet<string>> De(string rol) =>
            (await roles.GetClaimsAsync((await roles.FindByNameAsync(rol))!)).Where(c => c.Type == Permisos.TipoClaim).Select(c => c.Value).ToHashSet();

        (await De(Roles.Capturista)).Should().BeEquivalentTo([Permisos.RegistrosVer, Permisos.RegistrosAnular]);
        (await De(Roles.Administrador)).Should().BeEquivalentTo(Permisos.Todos);
        (await De(Roles.Secretaria)).Should().BeEquivalentTo(MatrizPermisos.De(Roles.Secretaria));
    }
}
