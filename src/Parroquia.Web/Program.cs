using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura;
using Parroquia.Infraestructura.Identidad;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.File("logs/parroquia-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 60));

builder.Services.AddInfraestructura(builder.Configuration);

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Cuenta/Login";
    o.LogoutPath = "/Cuenta/Logout";
    o.AccessDeniedPath = "/Cuenta/AccesoDenegado";
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    o.SlidingExpiration = true;
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddAuthorization(o =>
{
    foreach (var permiso in Permisos.Todos)
        o.AddPolicy(permiso, p => p.RequireClaim(Permisos.TipoClaim, permiso));

    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddRazorPages();

var cultura = new CultureInfo("es-GT");
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture(cultura);
    o.SupportedCultures = [cultura];
    o.SupportedUICultures = [cultura];
});

var app = builder.Build();

await SembradorIdentidad.InicializarAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRequestLocalization();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Los recursos estáticos (CSS/JS) deben ser públicos: el login los necesita antes de tener sesión.
app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
