using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Parroquia.Aplicacion.Operacion;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura;
using Parroquia.Infraestructura.Identidad;
using Parroquia.Web.Filtros;
using Parroquia.Web.Operacion;
using Parroquia.Web.Seguridad;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Como servicio de Windows el directorio de trabajo es System32: todo lo que se escribe va a la carpeta de datos (ver docs/operacion.md).
builder.Host.UseWindowsService(o => o.ServiceName = "ParroquiaWeb");

var carpetaDatos = new CarpetaDatos(
    builder.Configuration["Parroquia:CarpetaDatos"]
    ?? (builder.Environment.IsDevelopment()
        ? Path.Combine(builder.Environment.ContentRootPath, "datos-dev")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Parroquia")));
builder.Services.AddSingleton(carpetaDatos);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.File(Path.Combine(carpetaDatos.Logs, "parroquia-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60));

builder.Services.AddInfraestructura(builder.Configuration);

// Las cookies de sesión y los tokens antiforgery dependen de estas claves: guardarlas en una carpeta fija (y en el respaldo)
// evita cerrar todas las sesiones al reinstalar o actualizar. En desarrollo se usa el almacén por defecto.
if (!builder.Environment.IsDevelopment())
{
    Directory.CreateDirectory(carpetaDatos.Claves);
    var claves = builder.Services.AddDataProtection()
        .SetApplicationName("Parroquia")
        .PersistKeysToFileSystem(new DirectoryInfo(carpetaDatos.Claves));
    if (OperatingSystem.IsWindows())
        claves.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

// Con los endpoints de Kestrel definidos por configuración, ASP.NET no averigua el puerto HTTPS y la redirección del puerto 80
// se omitiría en silencio (el login viajaría en claro). Fuera de desarrollo se fija explícitamente.
if (!builder.Environment.IsDevelopment())
    builder.Services.AddHttpsRedirection(o => o.HttpsPort = builder.Configuration.GetValue("Parroquia:PuertoHttps", 443));

builder.Services.AddHealthChecks().AddCheck<SaludBaseDatos>("base-de-datos");

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
app.MapHealthChecks("/salud").AllowAnonymous(); // lo usan los scripts de instalación/actualización; responde solo Healthy/Unhealthy
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
