using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Infraestructura.Identidad;

/// <summary>Permisos que tiene hoy un perfil (rol). El Administrador siempre tiene todos y no se edita.</summary>
public sealed record PerfilPermisos(string Rol, bool Editable, IReadOnlySet<string> Permisos);

/// <summary>
/// Perfiles editables (permiso <c>Usuarios.Gestionar</c>): qué permisos tiene cada rol. Se guardan como claims del rol;
/// <see cref="MatrizPermisos"/> solo da los valores iniciales. Cada cambio queda en <c>BitacoraAcceso</c>.
/// </summary>
public sealed class ServicioPerfiles(
    RoleManager<IdentityRole> roles, UserManager<Usuario> usuarios, IBitacoraAcceso bitacora)
{
    public async Task<IReadOnlyList<PerfilPermisos>> ListarAsync(CancellationToken ct = default)
    {
        var lista = new List<PerfilPermisos>();
        foreach (var nombre in Roles.Todos)
        {
            var rol = await roles.FindByNameAsync(nombre);
            var actuales = rol is null ? [] : await PermisosDeAsync(rol);
            lista.Add(new PerfilPermisos(nombre, nombre != Roles.Administrador, actuales));
        }
        return lista;
    }

    public async Task<ResultadoOperacion> GuardarAsync(string? nombreRol, IEnumerable<string>? permisos, CancellationToken ct = default)
    {
        if (nombreRol == Roles.Administrador)
            return ResultadoOperacion.Falla("El Administrador siempre tiene todos los permisos y no se puede cambiar.");
        if (nombreRol is null || !Roles.Todos.Contains(nombreRol) || await roles.FindByNameAsync(nombreRol) is not { } rol)
            return ResultadoOperacion.Falla("El perfil no existe.");

        var deseados = (permisos ?? []).Select(p => p?.Trim() ?? "").Where(p => p.Length > 0).ToHashSet();
        if (deseados.FirstOrDefault(p => !Permisos.Todos.Contains(p)) is { } desconocido)
            return ResultadoOperacion.Falla($"El permiso «{desconocido}» no existe.");
        if (deseados.FirstOrDefault(p => Permisos.Reservados.Contains(p)) is { } reservado)
            return ResultadoOperacion.Falla($"El permiso «{reservado}» es solo del Administrador.");

        var actuales = await PermisosDeAsync(rol);
        var agregar = deseados.Where(p => !actuales.Contains(p)).Order().ToList();
        var quitar = actuales.Where(p => !deseados.Contains(p)).Order().ToList();
        if (agregar.Count == 0 && quitar.Count == 0) return ResultadoOperacion.Exito;

        foreach (var p in quitar)
        {
            var r = await roles.RemoveClaimAsync(rol, new Claim(Permisos.TipoClaim, p));
            if (!r.Succeeded) return ResultadoOperacion.Falla(Errores(r));
        }
        foreach (var p in agregar)
        {
            var r = await roles.AddClaimAsync(rol, new Claim(Permisos.TipoClaim, p));
            if (!r.Succeeded) return ResultadoOperacion.Falla(Errores(r));
        }

        // Las sesiones abiertas conservan los permisos viejos hasta que cambie el sello: se cierran en ≤ 1 minuto
        // y quien entre de nuevo recibe los permisos nuevos.
        foreach (var u in await usuarios.GetUsersInRoleAsync(nombreRol))
            await usuarios.UpdateSecurityStampAsync(u);

        var diferencias = agregar.Select(p => "+" + p).Concat(quitar.Select(p => "-" + p));
        await bitacora.RegistrarAsync(EventoAcceso.PermisosModificados,
            detalle: $"rol={nombreRol}; {string.Join("; ", diferencias)}", ct: ct);
        return ResultadoOperacion.Exito;
    }

    private async Task<HashSet<string>> PermisosDeAsync(IdentityRole rol) =>
        (await roles.GetClaimsAsync(rol)).Where(c => c.Type == Permisos.TipoClaim).Select(c => c.Value).ToHashSet();

    private static string Errores(IdentityResult r) => string.Join("; ", r.Errors.Select(e => e.Description));
}
