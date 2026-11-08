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
        services.AddSingleton<GeneradorPdfConstancia>();
        services.AddScoped<ServicioConstancias>();

        // Un sacramento = un módulo: su servicio y su registro como IModuloSacramental.
        services.AddScoped<ServicioComunion>();
        services.AddModuloSacramental<ModuloComunion>();

        services.AddDbContext<ParroquiaDbContext>((sp, o) => o
            .UseNpgsql(cadena, npgsql => npgsql.MigrationsHistoryTable("migraciones_historial"))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditoriaInterceptor>()));

        services.AddIdentity<Usuario, IdentityRole>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<ParroquiaDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }
}
