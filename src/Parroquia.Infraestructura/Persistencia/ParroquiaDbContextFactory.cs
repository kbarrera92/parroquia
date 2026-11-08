using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Parroquia.Infraestructura.Persistencia;

/// <summary>
/// Solo para <c>dotnet ef</c>: evita arrancar la app completa (y su sembrador) al crear migraciones.
/// Usa la variable <c>ConnectionStrings__Parroquia</c> si existe; si no, la BD de desarrollo.
/// </summary>
public class ParroquiaDbContextFactory : IDesignTimeDbContextFactory<ParroquiaDbContext>
{
    public ParroquiaDbContext CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("ConnectionStrings__Parroquia")
            ?? "Host=localhost;Port=5432;Database=parroquia;Username=parroquia;Password=parroquia_dev";

        var opciones = new DbContextOptionsBuilder<ParroquiaDbContext>()
            .UseNpgsql(cadena, n => n.MigrationsHistoryTable("migraciones_historial"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ParroquiaDbContext(opciones);
    }
}
