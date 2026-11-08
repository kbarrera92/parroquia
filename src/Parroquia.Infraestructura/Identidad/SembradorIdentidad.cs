using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Catalogos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>Aplica migraciones, sincroniza roles/permisos con la matriz y crea el administrador inicial.</summary>
public static class SembradorIdentidad
{
    public const string VariableUsuario = "PARROQUIA_ADMIN_USUARIO";
    public const string VariablePassword = "PARROQUIA_ADMIN_PASSWORD";

    public static async Task InicializarAsync(IServiceProvider servicios, CancellationToken ct = default)
    {
        using var scope = servicios.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<ParroquiaDbContext>();
        await db.Database.MigrateAsync(ct);
        await SembradorCatalogos.InicializarAsync(db, ct);

        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        await SincronizarRolesAsync(roles);

        var usuarios = sp.GetRequiredService<UserManager<Usuario>>();
        var config = sp.GetRequiredService<IConfiguration>();
        await CrearAdministradorAsync(usuarios, config);
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
