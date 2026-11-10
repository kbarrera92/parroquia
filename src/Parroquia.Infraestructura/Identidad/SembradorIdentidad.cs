using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Catalogos;
using Parroquia.Infraestructura.Configuracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>Aplica migraciones, sincroniza roles/permisos con la matriz y crea el administrador inicial.</summary>
public static class SembradorIdentidad
{
    public const string VariableUsuario = "PARROQUIA_ADMIN_USUARIO";
    public const string VariablePassword = "PARROQUIA_ADMIN_PASSWORD";

    /// <summary>"1" o "true" (junto con <see cref="VariablePassword"/>): restablece al administrador al arrancar. Quitarla después.</summary>
    public const string VariableRestablecer = "PARROQUIA_ADMIN_RESTABLECER";

    public static async Task InicializarAsync(IServiceProvider servicios, CancellationToken ct = default)
    {
        using var scope = servicios.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ParroquiaDbContext>();
        await db.Database.MigrateAsync(ct);
        await SembradorCatalogos.InicializarAsync(db, ct);
        await SembradorConfiguracion.InicializarAsync(db, ct);

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        await SincronizarRolesAsync(roles);

        var usuarios = sp.GetRequiredService<UserManager<Usuario>>();
        var config = sp.GetRequiredService<IConfiguration>();
        await CrearAdministradorAsync(usuarios, config);

        if (RestablecerSolicitado(config))
        {
            var password = config[VariablePassword];
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException($"{VariableRestablecer} requiere definir también {VariablePassword}.");

            var nombre = config[VariableUsuario];
            await RestablecerAdministradorAsync(servicios, string.IsNullOrWhiteSpace(nombre) ? "admin" : nombre, password, ct);
            sp.GetService<ILoggerFactory>()?.CreateLogger(nameof(SembradorIdentidad)).LogWarning(
                "Se restableció al administrador por {Variable}. Quite esa variable de entorno y reinicie el servicio.", VariableRestablecer);
        }
    }

    public static bool RestablecerSolicitado(IConfiguration config) =>
        config[VariableRestablecer] is { } v && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Recuperación de emergencia (requiere acceso al servidor): fija una contraseña nueva al administrador, lo activa,
    /// lo desbloquea, cierra sus sesiones y le exige cambiarla. Si el usuario no existe lo crea como Administrador.
    /// Respeta la política de contraseñas. No promueve a quien no es Administrador.
    /// </summary>
    public static async Task RestablecerAdministradorAsync(
        IServiceProvider servicios, string usuario, string password, CancellationToken ct = default)
    {
        using var scope = servicios.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var bitacora = scope.ServiceProvider.GetRequiredService<IBitacoraAcceso>();
        var reloj = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var admin = await usuarios.FindByNameAsync(usuario);
        if (admin is null)
        {
            admin = new Usuario
            {
                UserName = usuario, NombreCompleto = "Administrador", Activo = true, LockoutEnabled = true,
                DebeCambiarPassword = true, CreadoEn = reloj.GetUtcNow(),
            };
            Comprobar(await usuarios.CreateAsync(admin, password), "crear administrador (revise los requisitos de contraseña)");
            Comprobar(await usuarios.AddToRoleAsync(admin, Roles.Administrador), "asignar rol Administrador");
            await bitacora.RegistrarAsync(EventoAcceso.UsuarioCreado, usuario, admin.Id,
                detalle: $"usuario={usuario}; rol={Roles.Administrador}; restablecimiento de emergencia", ct: ct);
            return;
        }

        if (!await usuarios.IsInRoleAsync(admin, Roles.Administrador))
            throw new InvalidOperationException($"El usuario «{usuario}» existe pero no es Administrador; no se restablece.");

        Comprobar(
            await usuarios.ResetPasswordAsync(admin, await usuarios.GeneratePasswordResetTokenAsync(admin), password),
            "restablecer contraseña (revise los requisitos: mínimo 12 caracteres, no común)");

        admin.Activo = true;
        admin.DebeCambiarPassword = true;
        Comprobar(await usuarios.UpdateAsync(admin), "actualizar administrador");
        await usuarios.SetLockoutEndDateAsync(admin, null);
        await usuarios.ResetAccessFailedCountAsync(admin);

        await bitacora.RegistrarAsync(EventoAcceso.PasswordRestablecida, usuario, admin.Id,
            detalle: $"usuario={usuario}; restablecimiento de emergencia", ct: ct);
    }

    private static async Task SincronizarRolesAsync(RoleManager<IdentityRole> roles)
    {
        foreach (var nombre in Roles.Todos)
        {
            var rol = await roles.FindByNameAsync(nombre);
            if (rol is null)
            {
                rol = new IdentityRole(nombre);
                Comprobar(await roles.CreateAsync(rol), $"crear rol {nombre}");
            }

            var deseados = MatrizPermisos.De(nombre);
            var actuales = (await roles.GetClaimsAsync(rol)).Where(c => c.Type == Permisos.TipoClaim).ToList();

            foreach (var sobrante in actuales.Where(c => !deseados.Contains(c.Value)))
                Comprobar(await roles.RemoveClaimAsync(rol, sobrante), $"quitar permiso {sobrante.Value}");

            foreach (var permiso in deseados.Where(p => actuales.All(c => c.Value != p)))
                Comprobar(await roles.AddClaimAsync(rol, new Claim(Permisos.TipoClaim, permiso)), $"agregar permiso {permiso}");
        }
    }

    private static async Task CrearAdministradorAsync(UserManager<Usuario> usuarios, IConfiguration config)
    {
        if ((await usuarios.GetUsersInRoleAsync(Roles.Administrador)).Count > 0)
            return;

        var password = config[VariablePassword];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                $"No existe ningún Administrador. Defina la variable de entorno {VariablePassword} para crear el inicial.");

        var nombre = config[VariableUsuario];
        if (string.IsNullOrWhiteSpace(nombre)) nombre = "admin";

        var admin = new Usuario
        {
            UserName = nombre,
            Email = null,
            NombreCompleto = "Administrador",
            Activo = true,
            LockoutEnabled = true,
        };

        Comprobar(await usuarios.CreateAsync(admin, password), "crear administrador (revise los requisitos de contraseña)");
        Comprobar(await usuarios.AddToRoleAsync(admin, Roles.Administrador), "asignar rol Administrador");
    }

    private static void Comprobar(IdentityResult resultado, string accion)
    {
        if (!resultado.Succeeded)
            throw new InvalidOperationException(
                $"Error al {accion}: {string.Join("; ", resultado.Errors.Select(e => e.Description))}");
    }
}
