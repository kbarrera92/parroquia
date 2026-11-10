using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Identidad;

public sealed record FilaUsuario(
    string Id, string Usuario, string NombreCompleto, string Rol, bool Activo,
    bool Bloqueado, bool DebeCambiarPassword, DateTimeOffset CreadoEn, DateTimeOffset? UltimoInicio);

/// <summary>Resultado de crear/restablecer: la contraseña temporal solo existe aquí, para mostrarla una vez.</summary>
public readonly record struct ResultadoConTemporal(bool Ok, string? Error = null, string? Temporal = null)
{
    public static ResultadoConTemporal Falla(string error) => new(false, error);
}

/// <summary>
/// Administración de cuentas (permiso <c>Usuarios.Gestionar</c>) y cambio de la contraseña propia.
/// Un rol por usuario. Todo queda en <c>BitacoraAcceso</c> (actor, usuario afectado y diferencias), nunca contraseñas.
/// </summary>
public sealed partial class ServicioUsuarios(
    UserManager<Usuario> usuarios, ParroquiaDbContext db, IBitacoraAcceso bitacora,
    IUsuarioActual actual, TimeProvider reloj)
{
    public const int LongitudNombreMaxima = 200;

    [GeneratedRegex("^[a-z0-9._-]{3,50}$")]
    private static partial Regex PatronUsuario();

    public async Task<IReadOnlyList<FilaUsuario>> ListarAsync(string? rol = null, bool? activo = null, CancellationToken ct = default)
    {
        var consulta =
            from u in db.Users.AsNoTracking()
            join ur in db.UserRoles on u.Id equals ur.UserId into roles
            from ur in roles.DefaultIfEmpty()
            join r in db.Roles on ur.RoleId equals r.Id into nombres
            from r in nombres.DefaultIfEmpty()
            select new { U = u, Rol = r != null ? r.Name! : "" };

        if (!string.IsNullOrWhiteSpace(rol)) consulta = consulta.Where(x => x.Rol == rol);
        if (activo is { } a) consulta = consulta.Where(x => x.U.Activo == a);

        var filas = await consulta.OrderBy(x => x.U.UserName).ToListAsync(ct);
        var ids = filas.Select(x => x.U.Id).ToList();

        var ultimos = await db.BitacoraAccesos.AsNoTracking()
            .Where(b => b.Evento == EventoAcceso.InicioSesion && b.UsuarioId != null && ids.Contains(b.UsuarioId))
            .GroupBy(b => b.UsuarioId!)
            .Select(g => new { Id = g.Key, Fecha = g.Max(b => b.Fecha) })
            .ToDictionaryAsync(x => x.Id, x => x.Fecha, ct);

        var ahora = reloj.GetUtcNow();
        return
        [
            .. filas.Select(x => new FilaUsuario(
                x.U.Id, x.U.UserName ?? "", x.U.NombreCompleto, x.Rol, x.U.Activo,
                x.U.LockoutEnd is { } fin && fin > ahora, x.U.DebeCambiarPassword, x.U.CreadoEn,
                ultimos.TryGetValue(x.U.Id, out var f) ? f : null)),
        ];
    }

    public async Task<FilaUsuario?> ObtenerAsync(string id, CancellationToken ct = default) =>
        (await ListarAsync(ct: ct)).FirstOrDefault(f => f.Id == id);

    public async Task<ResultadoConTemporal> CrearAsync(string? usuario, string? nombreCompleto, string? rol, CancellationToken ct = default)
    {
        var nombreUsuario = (usuario ?? "").Trim().ToLowerInvariant();
        var nombre = (nombreCompleto ?? "").Trim();

        if (!PatronUsuario().IsMatch(nombreUsuario))
            return ResultadoConTemporal.Falla("El usuario debe tener de 3 a 50 caracteres: letras sin tilde, números, punto, guion o guion bajo.");
        if (nombre.Length == 0) return ResultadoConTemporal.Falla("Escriba el nombre completo.");
        if (nombre.Length > LongitudNombreMaxima) return ResultadoConTemporal.Falla($"El nombre no puede pasar de {LongitudNombreMaxima} caracteres.");
        if (rol is null || !Roles.Todos.Contains(rol)) return ResultadoConTemporal.Falla("Elija un rol válido.");
        if (await usuarios.FindByNameAsync(nombreUsuario) is not null)
            return ResultadoConTemporal.Falla($"El usuario «{nombreUsuario}» ya existe.");

        var nuevo = new Usuario
        {
            UserName = nombreUsuario, NombreCompleto = nombre, Activo = true, LockoutEnabled = true,
            DebeCambiarPassword = true, CreadoEn = reloj.GetUtcNow(),
        };

        var temporal = GeneradorContrasenaTemporal.Generar();
        var creado = await usuarios.CreateAsync(nuevo, temporal);
        if (!creado.Succeeded) return ResultadoConTemporal.Falla(Errores(creado));

        var conRol = await usuarios.AddToRoleAsync(nuevo, rol);
        if (!conRol.Succeeded)
        {
            await usuarios.DeleteAsync(nuevo);   // cuenta recién creada sin rol: no debe quedar a medias
            return ResultadoConTemporal.Falla(Errores(conRol));
        }

        await bitacora.RegistrarAsync(EventoAcceso.UsuarioCreado, detalle: $"usuario={nombreUsuario}; rol={rol}", ct: ct);
        return new ResultadoConTemporal(true, Temporal: temporal);
    }

    public async Task<ResultadoOperacion> EditarAsync(string id, string? nombreCompleto, string? rol, CancellationToken ct = default)
    {
        var u = await usuarios.FindByIdAsync(id);
        if (u is null) return ResultadoOperacion.Falla("El usuario no existe.");

        var nombre = (nombreCompleto ?? "").Trim();
        if (nombre.Length == 0) return ResultadoOperacion.Falla("Escriba el nombre completo.");
        if (nombre.Length > LongitudNombreMaxima) return ResultadoOperacion.Falla($"El nombre no puede pasar de {LongitudNombreMaxima} caracteres.");
        if (rol is null || !Roles.Todos.Contains(rol)) return ResultadoOperacion.Falla("Elija un rol válido.");

        var rolActual = (await usuarios.GetRolesAsync(u)).FirstOrDefault() ?? "";
        var cambiaRol = rolActual != rol;

        if (cambiaRol)
        {
            if (u.Id == actual.Id)
                return ResultadoOperacion.Falla("No puede cambiar su propio rol. Pídaselo a otro Administrador.");
            if (rolActual == Roles.Administrador && u.Activo && await EsUltimoAdministradorActivoAsync(u))
                return ResultadoOperacion.Falla("Debe quedar al menos un Administrador activo.");
        }

        var cambios = new List<string>();
        if (u.NombreCompleto != nombre)
        {
            cambios.Add($"nombre: {u.NombreCompleto} → {nombre}");
            u.NombreCompleto = nombre;
            var guardado = await usuarios.UpdateAsync(u);
            if (!guardado.Succeeded) return ResultadoOperacion.Falla(Errores(guardado));
        }

        if (cambiaRol)
        {
            var quitar = await usuarios.RemoveFromRolesAsync(u, await usuarios.GetRolesAsync(u));
            if (!quitar.Succeeded) return ResultadoOperacion.Falla(Errores(quitar));
            var poner = await usuarios.AddToRoleAsync(u, rol);
            if (!poner.Succeeded) return ResultadoOperacion.Falla(Errores(poner));
            await usuarios.UpdateSecurityStampAsync(u);   // la sesión abierta conserva los permisos viejos hasta aquí
            cambios.Add($"rol: {rolActual} → {rol}");
        }

        if (cambios.Count > 0)
            await bitacora.RegistrarAsync(EventoAcceso.UsuarioModificado, detalle: $"usuario={u.UserName}; {string.Join("; ", cambios)}", ct: ct);
        return ResultadoOperacion.Exito;
    }

    public async Task<ResultadoOperacion> CambiarEstadoAsync(string id, bool activo, CancellationToken ct = default)
    {
        var u = await usuarios.FindByIdAsync(id);
        if (u is null) return ResultadoOperacion.Falla("El usuario no existe.");
        if (u.Activo == activo) return ResultadoOperacion.Exito;

        if (!activo)
        {
            if (u.Id == actual.Id)
                return ResultadoOperacion.Falla("No puede desactivar su propia cuenta.");
            if (await usuarios.IsInRoleAsync(u, Roles.Administrador) && await EsUltimoAdministradorActivoAsync(u))
                return ResultadoOperacion.Falla("Debe quedar al menos un Administrador activo.");
        }

        u.Activo = activo;
        var guardado = await usuarios.UpdateAsync(u);
        if (!guardado.Succeeded) return ResultadoOperacion.Falla(Errores(guardado));
        await usuarios.UpdateSecurityStampAsync(u);   // cierra las sesiones abiertas al desactivar

        await bitacora.RegistrarAsync(
            activo ? EventoAcceso.UsuarioActivado : EventoAcceso.UsuarioDesactivado, detalle: $"usuario={u.UserName}", ct: ct);
        return ResultadoOperacion.Exito;
    }

    public async Task<ResultadoConTemporal> RestablecerPasswordAsync(string id, CancellationToken ct = default)
    {
        var u = await usuarios.FindByIdAsync(id);
        if (u is null) return ResultadoConTemporal.Falla("El usuario no existe.");
        if (u.Id == actual.Id)
            return ResultadoConTemporal.Falla("Para cambiar su propia contraseña use «Cambiar contraseña».");

        var temporal = GeneradorContrasenaTemporal.Generar();
        var token = await usuarios.GeneratePasswordResetTokenAsync(u);
        var reset = await usuarios.ResetPasswordAsync(u, token, temporal);   // también renueva el sello de seguridad
        if (!reset.Succeeded) return ResultadoConTemporal.Falla(Errores(reset));

        u.DebeCambiarPassword = true;
        var guardado = await usuarios.UpdateAsync(u);
        if (!guardado.Succeeded) return ResultadoConTemporal.Falla(Errores(guardado));
        await usuarios.SetLockoutEndDateAsync(u, null);
        await usuarios.ResetAccessFailedCountAsync(u);

        await bitacora.RegistrarAsync(EventoAcceso.PasswordRestablecida, detalle: $"usuario={u.UserName}", ct: ct);
        return new ResultadoConTemporal(true, Temporal: temporal);
    }

    public async Task<ResultadoOperacion> DesbloquearAsync(string id, CancellationToken ct = default)
    {
        var u = await usuarios.FindByIdAsync(id);
        if (u is null) return ResultadoOperacion.Falla("El usuario no existe.");

        await usuarios.SetLockoutEndDateAsync(u, null);
        await usuarios.ResetAccessFailedCountAsync(u);
        await bitacora.RegistrarAsync(EventoAcceso.CuentaDesbloqueada, detalle: $"usuario={u.UserName}", ct: ct);
        return ResultadoOperacion.Exito;
    }

    /// <summary>El propio usuario cambia su contraseña (pide la actual). Cierra sus otras sesiones y quita el cambio obligatorio.</summary>
    public async Task<ResultadoOperacion> CambiarPropiaAsync(Usuario usuario, string actualPassword, string nueva, CancellationToken ct = default)
    {
        if (actualPassword == nueva)
            return ResultadoOperacion.Falla("La nueva contraseña debe ser distinta de la actual.");

        var resultado = await usuarios.ChangePasswordAsync(usuario, actualPassword, nueva);
        if (!resultado.Succeeded)
            return ResultadoOperacion.Falla(Errores(resultado));

        if (usuario.DebeCambiarPassword)
        {
            usuario.DebeCambiarPassword = false;
            await usuarios.UpdateAsync(usuario);
        }

        await bitacora.RegistrarAsync(EventoAcceso.PasswordCambiada, usuario.UserName, usuario.Id, ct: ct);
        return ResultadoOperacion.Exito;
    }

    private async Task<bool> EsUltimoAdministradorActivoAsync(Usuario objetivo) =>
        !(await usuarios.GetUsersInRoleAsync(Roles.Administrador)).Any(a => a.Activo && a.Id != objetivo.Id);

    private static string Errores(IdentityResult r) => string.Join(" ", r.Errors.Select(e => e.Description));
}
