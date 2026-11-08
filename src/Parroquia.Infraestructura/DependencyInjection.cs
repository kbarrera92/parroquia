using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Infraestructura.Identidad;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura;

public static class DependencyInjection
{
    public const string NombreCadenaConexion = "Parroquia";

    public static IServiceCollection AddInfraestructura(this IServiceCollection services, IConfiguration config)
    {
        var cadena = config.GetConnectionString(NombreCadenaConexion)
            ?? throw new InvalidOperationException($"Falta la cadena de conexión '{NombreCadenaConexion}'.");

        services.AddDbContext<ParroquiaDbContext>(o => o
            .UseNpgsql(cadena, npgsql => npgsql.MigrationsHistoryTable("migraciones_historial"))
            .UseSnakeCaseNamingConvention());

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
