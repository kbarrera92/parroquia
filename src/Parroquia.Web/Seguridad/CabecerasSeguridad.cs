namespace Parroquia.Web.Seguridad;

/// <summary>
/// Cabeceras de seguridad básicas (OWASP ASVS V14): sin scripts en línea, sin marcos, sin sniffing de tipos.
/// <c>style-src</c> admite estilos en línea porque la vista previa de la constancia usa atributos <c>style</c>;
/// los scripts, que son lo peligroso, solo se aceptan desde el propio sitio.
/// </summary>
public static class CabecerasSeguridad
{
    private const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public static IApplicationBuilder UseCabecerasSeguridad(this IApplicationBuilder app) =>
        app.Use(async (contexto, siguiente) =>
        {
            var cabeceras = contexto.Response.Headers;
            cabeceras["X-Content-Type-Options"] = "nosniff";
            cabeceras["X-Frame-Options"] = "DENY";
            cabeceras["Referrer-Policy"] = "no-referrer";
            cabeceras["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // El tipo de contenido se conoce al empezar la respuesta. El visor de PDF del navegador no debe heredar la CSP de las páginas.
            contexto.Response.OnStarting(() =>
            {
                var esPdf = contexto.Response.ContentType?.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase) == true;
                if (!esPdf) contexto.Response.Headers["Content-Security-Policy"] = Csp;
                return Task.CompletedTask;
            });

            await siguiente();
        });
}
