using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Filtros;

/// <summary>Quien tiene contraseña temporal solo puede cambiarla o cerrar sesión.</summary>
public sealed class FiltroCambioObligatorio : IAsyncPageFilter
{
    private static readonly string[] Permitidas = ["/Cuenta/CambiarPassword", "/Cuenta/Logout", "/Cuenta/Login", "/Error"];

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var debeCambiar = context.HttpContext.User.HasClaim(FabricaClaimsUsuario.TipoCambioObligatorio, "1");
        var ruta = context.ActionDescriptor.ViewEnginePath;

        if (debeCambiar && !Permitidas.Contains(ruta, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new RedirectToPageResult("/Cuenta/CambiarPassword");
            return;
        }

        await next();
    }
}
