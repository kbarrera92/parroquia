using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Web.Presentacion;

/// <summary>
/// Qué formas de alta puede usar el usuario: transcribir de un libro físico (<see cref="Permisos.RegistrosTranscribir"/>)
/// o asentar uno nuevo que numera el sistema (<see cref="Permisos.RegistrosAsentarNuevo"/>). Las usan las páginas
/// <c>Editar</c> de todos los libros y los listados; el servicio no conoce permisos.
/// </summary>
public sealed record PermisosAlta(bool PuedeTranscribir, bool PuedeAsentar)
{
    public const string ModoSistema = "sistema";
    public const string ModoSupletoria = "supletoria";

    /// <summary>Cualquiera de las dos: basta para ver el botón «Nuevo».</summary>
    public bool PuedeCrear => PuedeTranscribir || PuedeAsentar;

    public static async Task<PermisosAlta> DeAsync(IAuthorizationService autorizacion, ClaimsPrincipal usuario) =>
        new((await autorizacion.AuthorizeAsync(usuario, Permisos.RegistrosTranscribir)).Succeeded,
            (await autorizacion.AuthorizeAsync(usuario, Permisos.RegistrosAsentarNuevo)).Succeeded);

    /// <summary>Asiento nuevo (y supletoria numerada por el sistema) exigen AsentarNuevo; lo demás, Transcribir.</summary>
    public bool Permite(bool asientoNuevo, bool? supletoriaNumeraSistema = null) =>
        (asientoNuevo || supletoriaNumeraSistema == true) ? PuedeAsentar : PuedeTranscribir;

    /// <summary>Modo (<c>?modo=</c>) con el que abre el alta: el pedido si está permitido; si no, el primero que sí lo está.</summary>
    public string? ModoInicial(string? pedido)
    {
        var permitido = pedido switch
        {
            ModoSistema => PuedeAsentar,
            ModoSupletoria => PuedeCrear,
            _ => PuedeTranscribir,
        };
        if (permitido) return pedido;
        return PuedeTranscribir ? null : PuedeAsentar ? ModoSistema : null;
    }

    /// <summary>
    /// Valor inicial de «numera el sistema» en una supletoria nueva: el único permitido si solo hay uno; si hay ambos, transcrita
    /// (como el predeterminado de los libros).
    /// </summary>
    public bool SupletoriaNumeraPorDefecto => PuedeAsentar && !PuedeTranscribir;
}
