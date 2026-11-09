using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Catalogos;
using Parroquia.Infraestructura.Configuracion;
using Parroquia.Infraestructura.Constancias;
using Parroquia.Infraestructura.Historial;
using Parroquia.Infraestructura.Identidad;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;
using Parroquia.Infraestructura.Vinculos;

namespace Parroquia.Infraestructura;

public static class DependencyInjection
{
    public const string NombreCadenaConexion = "Parroquia";

    /// <summary>Registra un módulo sacramental para que menú y búsqueda global lo descubran.</summary>
    public static IServiceCollection AddModuloSacramental<T>(this IServiceCollection services)
        where T : class, IModuloSacramental =>
        services.AddScoped<IModuloSacramental, T>();

    public static IServiceCollection AddInfraestructura(this IServiceCollection services, IConfiguration config)
    {
        var cadena = config.GetConnectionString(NombreCadenaConexion)
            ?? throw new InvalidOperationException($"Falta la cadena de conexión '{NombreCadenaConexion}'.");

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUsuarioActual, UsuarioActualHttp>();
        services.AddScoped<AuditoriaInterceptor>();
        services.AddScoped<IBitacoraAcceso, ServicioBitacoraAcceso>();
        services.AddScoped(typeof(ServicioCatalogo<>));
        services.AddScoped<ServicioParroco>();
        services.AddScoped<ServicioConfiguracionParroquia>();
        services.AddScoped<ServicioHistorial>();
        services.AddScoped<BusquedaGlobal>();
        services.AddScoped<ServicioVinculos>();
        services.AddSingleton<GeneradorPdfConstancia>();
        services.AddScoped<ServicioConstancias>();

        // Un sacramento = un módulo: su servicio y su registro como IModuloSacramental.
        services.AddScoped<ServicioBautismo>();
        services.AddModuloSacramental<ModuloBautismo>();
        services.AddScoped<ServicioConfirmacion>();
        services.AddModuloSacramental<ModuloConfirmacion>();
        services.AddScoped<ServicioMatrimonio>();
        services.AddModuloSacramental<ModuloMatrimonio>();
        services.AddScoped<ServicioComunion>();
        services.AddModuloSacramental<ModuloComunion>();

        services.AddDbContext<ParroquiaDbContext>((sp, o) => o
            .UseNpgsql(cadena, npgsql => npgsql.MigrationsHistoryTable("migraciones_historial"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditoriaInterceptor>()));

        services.AddIdentity<Usuario, IdentityRole>(o =>
            {
                // NIST SP 800-63B / OWASP ASVS: longitud y lista de comunes; sin reglas de composición ni caducidad.
                o.Password.RequiredLength = PoliticaContrasena.LongitudMinima;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequiredUniqueChars = 1;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = false;
                // "Confirmada" = activa (ConfirmacionUsuarioActivo): un usuario desactivado no inicia sesión.
                o.SignIn.RequireConfirmedAccount = true;
            })
            .AddEntityFrameworkStores<ParroquiaDbContext>()
            .AddClaimsPrincipalFactory<FabricaClaimsUsuario>()
            .AddUserConfirmation<ConfirmacionUsuarioActivo>()
            .AddErrorDescriber<DescriptorErroresIdentidad>()
            .AddPasswordValidator<ValidadorContrasenaComun>()
            .AddDefaultTokenProviders();

        // PBKDF2-HMAC-SHA512 (formato v3 de Identity) con 210 000 iteraciones (OWASP Password Storage);
        // las contraseñas antiguas se re-hashean solas al iniciar sesión.
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);

        // Revalida el sello de seguridad cada minuto: cambiar contraseña, rol o estado cierra las sesiones abiertas.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
        services.AddScoped<ServicioUsuarios>();

        return services;
    }
}
