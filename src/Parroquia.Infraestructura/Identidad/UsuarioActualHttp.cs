using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Infraestructura.Identidad;

public sealed class UsuarioActualHttp(IHttpContextAccessor accesor) : IUsuarioActual
{
    public const string NombreSistema = "sistema";

    private ClaimsPrincipal? Principal =>
        accesor.HttpContext?.User is { Identity.IsAuthenticated: true } p ? p : null;

    public string? Id => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string Nombre => Principal?.Identity?.Name ?? NombreSistema;
}
