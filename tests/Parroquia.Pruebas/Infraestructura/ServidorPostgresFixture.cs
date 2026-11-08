using Microsoft.EntityFrameworkCore;
using Npgsql;
using Parroquia.Infraestructura.Persistencia;
using Testcontainers.PostgreSql;

namespace Parroquia.Pruebas.Infraestructura;

/// <summary>Un contenedor PostgreSQL por clase de pruebas; cada prueba obtiene su propia base de datos vacía.</summary>
public sealed class ServidorPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _servidor = new PostgreSqlBuilder("postgres:17").Build();

    public Task InitializeAsync() => _servidor.StartAsync();
    public async Task DisposeAsync() => await _servidor.DisposeAsync();

    public async Task<BaseTemporal> NuevaBaseAsync(UsuarioFalso? usuario = null, RelojFalso? reloj = null)
    {
        var nombre = "t_" + Guid.NewGuid().ToString("N");
        await using (var conn = new NpgsqlConnection(_servidor.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {nombre}", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var cadena = new NpgsqlConnectionStringBuilder(_servidor.GetConnectionString()) { Database = nombre }.ToString();
        var bd = new BaseTemporal(cadena, usuario ?? new UsuarioFalso(), reloj ?? new RelojFalso());
        await using var ctx = bd.Crear();
        await ctx.Database.EnsureCreatedAsync();
        return bd;
    }
}

public sealed class BaseTemporal(string cadena, UsuarioFalso usuario, RelojFalso reloj)
{
    public UsuarioFalso Usuario { get; } = usuario;
    public RelojFalso Reloj { get; } = reloj;

    public ParroquiaDbContext Crear()
    {
        var opciones = new DbContextOptionsBuilder<ParroquiaDbContext>()
            .UseNpgsql(cadena)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditoriaInterceptor(Usuario, Reloj))
            .Options;
        return new ParroquiaDbContext(opciones);
    }
}
