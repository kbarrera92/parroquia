using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura;
using Parroquia.Infraestructura.Identidad;
using Parroquia.Web.Filtros;
using Parroquia.Web.Seguridad;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.File("logs/parroquia-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60));

builder.Services.AddInfraestructura(builder.Configuration);

// Fuera de desarrollo la cookie solo viaja por HTTPS (la app se publica con HTTPS interno, ver docs/04 ADR-08).
var politicaCookie = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Cuenta/Login";
    o.LogoutPath = "/Cuenta/Logout";
    o.AccessDeniedPath = "/Cuenta/AccesoDenegado";
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = politicaCookie;
});
builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = politicaCookie);

// Contra el rociado de contraseñas (una IP probando muchos usuarios), además del bloqueo por cuenta de Identity.
// Solo cuenta el envío del formulario de login (POST). El límite se lee al atender la petición: configurable y fácil de probar.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", contexto =>
    {
        if (!HttpMethods.IsPost(contexto.Request.Method))
            return RateLimitPartition.GetNoLimiter("lectura");

        var limite = contexto.RequestServices.GetRequiredService<IConfiguration>().GetValue("Seguridad:LoginPorMinuto", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    o.OnRejected = async (contexto, ct) =>
    {
        var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera) ? (int)Math.Ceiling(espera.TotalSeconds) : 60;
        contexto.HttpContext.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
        contexto.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await contexto.HttpContext.Response.WriteAsync("Demasiados intentos de inicio de sesión desde este equipo. Espere un minuto e intente de nuevo.", ct);
    };
});

builder.Services.AddAuthorization(o =>
{
    foreach (var permiso in Permisos.Todos)
        o.AddPolicy(permiso, p => p.RequireClaim(Permisos.TipoClaim, permiso));

    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddRazorPages()
    .AddMvcOptions(o => o.Filters.Add<FiltroCambioObligatorio>());

var cultura = new CultureInfo("es-GT");
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture(cultura);
    o.SupportedCultures = [cultura];
    o.SupportedUICultures = [cultura];
});

var app = builder.Build();

await SembradorIdentidad.InicializarAsync(app.Services);

app.UseCabecerasSeguridad();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Los recursos estáticos (CSS/JS) deben ser públicos: el login los necesita antes de tener sesión.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
