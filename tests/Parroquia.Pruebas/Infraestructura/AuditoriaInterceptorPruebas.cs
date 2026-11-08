using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Comun;
using Parroquia.Infraestructura.Persistencia;
using Testcontainers.PostgreSql;

namespace Parroquia.Pruebas.Infraestructura;

public class EntidadPrueba : EntidadAuditable, IAnulable
{
    public string Nombre { get; set; } = "";

    /// <summary>Columna derivada (como BusquedaNormalizada): se guarda pero no ensucia el historial.</summary>
    public string? Derivado { get; set; }
    public FechaParcial Fecha { get; set; } = FechaParcial.Vacia;

    /// <summary>Colección primitiva (text[]), como los padrinos de un bautismo.</summary>
    public List<string> Lista { get; set; } = [];
    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }
}

public sealed class ContextoPrueba(DbContextOptions<ContextoPrueba> options) : DbContext(options)
{
    public DbSet<EntidadPrueba> Entidades => Set<EntidadPrueba>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<EntidadPrueba>(e =>
        {
            e.ToTable("entidad_prueba");
            e.MapearFechaParcial(x => x.Fecha, "fecha");
            e.Property(x => x.Derivado).NoAuditar();
        });
        ConfiguracionAuditable.Aplicar(builder);
    }
}

public sealed class UsuarioFalso : IUsuarioActual
{
    public string? Id { get; set; } = "u-1";
    public string Nombre { get; set; } = "capturista1";
}

public sealed class RelojFalso : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Ahora;
}

public sealed class BaseDatosAuditoriaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        await using var ctx = Crear(new UsuarioFalso(), new RelojFalso());
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    public ContextoPrueba Crear(UsuarioFalso usuario, RelojFalso reloj)
    {
        var opciones = new DbContextOptionsBuilder<ContextoPrueba>()
            .UseNpgsql(_db.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditoriaInterceptor(usuario, reloj))
            .Options;
        return new ContextoPrueba(opciones);
    }
}

public class AuditoriaInterceptorPruebas(BaseDatosAuditoriaFixture bd) : IClassFixture<BaseDatosAuditoriaFixture>
{
    private readonly UsuarioFalso _usuario = new();
    private readonly RelojFalso _reloj = new();

    private ContextoPrueba Ctx() => bd.Crear(_usuario, _reloj);

    private static async Task<List<Auditoria>> HistorialAsync(ContextoPrueba ctx, long id) =>
        await ctx.Set<Auditoria>().Where(a => a.Entidad == nameof(EntidadPrueba) && a.EntidadId == id)
            .OrderBy(a => a.Id).ToListAsync();

    private static JsonElement Cambios(Auditoria a) => JsonDocument.Parse(a.CambiosJson).RootElement;

    [Fact]
    public async Task Alta_rellena_creado_y_escribe_auditoria_en_el_mismo_guardado()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana" };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        e.Id.Should().BeGreaterThan(0);
        e.CreadoPor.Should().Be("capturista1");
        e.CreadoEn.Should().Be(_reloj.Ahora);

        await using var lectura = Ctx();
        var h = await HistorialAsync(lectura, e.Id);
        h.Should().ContainSingle();
        h[0].Accion.Should().Be(AccionAuditoria.Alta);
        h[0].UsuarioNombre.Should().Be("capturista1");
        h[0].UsuarioId.Should().Be("u-1");
        h[0].Fecha.Should().Be(_reloj.Ahora);
        Cambios(h[0]).GetProperty("Nombre").GetProperty("nuevo").GetString().Should().Be("Ana");
    }

    [Fact]
    public async Task Modificacion_guarda_solo_los_campos_cambiados_con_anterior_y_nuevo()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana" };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        _usuario.Nombre = "capturista2";
        _reloj.Ahora = _reloj.Ahora.AddHours(1);
        e.Nombre = "Ana María";
        await ctx.SaveChangesAsync();

        e.ModificadoPor.Should().Be("capturista2");
        e.ModificadoEn.Should().Be(_reloj.Ahora);
        e.CreadoPor.Should().Be("capturista1");

        await using var lectura = Ctx();
        var h = await HistorialAsync(lectura, e.Id);
        h.Should().HaveCount(2);
        h[1].Accion.Should().Be(AccionAuditoria.Modificacion);
        h[1].UsuarioNombre.Should().Be("capturista2");
        var c = Cambios(h[1]);
        c.EnumerateObject().Select(p => p.Name).Should().Equal("Nombre");
        c.GetProperty("Nombre").GetProperty("anterior").GetString().Should().Be("Ana");
        c.GetProperty("Nombre").GetProperty("nuevo").GetString().Should().Be("Ana María");
    }

    [Fact]
    public async Task Guardar_sin_cambios_reales_no_audita()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana" };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        e.Nombre = "Ana"; // mismo valor
        await ctx.SaveChangesAsync();
        ctx.Entry(e).State = EntityState.Modified; // marcado a la fuerza, sin diferencias
        await ctx.SaveChangesAsync();

        await using var lectura = Ctx();
        (await HistorialAsync(lectura, e.Id)).Should().ContainSingle(); // solo el Alta
    }

    [Fact]
    public async Task Anular_y_revertir_tienen_su_propia_accion()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana" };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        e.Anulado = true;
        e.MotivoAnulacion = "Duplicado";
        await ctx.SaveChangesAsync();

        e.Anulado = false;
        e.MotivoAnulacion = null;
        await ctx.SaveChangesAsync();

        await using var lectura = Ctx();
        var h = await HistorialAsync(lectura, e.Id);
        h.Select(a => a.Accion).Should().Equal(
            AccionAuditoria.Alta, AccionAuditoria.Anulacion, AccionAuditoria.RevertirAnulacion);
        Cambios(h[1]).GetProperty("MotivoAnulacion").GetProperty("nuevo").GetString().Should().Be("Duplicado");
    }

    [Fact]
    public async Task Columnas_marcadas_NoAuditar_se_guardan_pero_no_aparecen_en_el_historial()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana", Derivado = "ana" };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        e.Nombre = "Ana María";
        e.Derivado = "ana maria";
        await ctx.SaveChangesAsync();

        await using var lectura = Ctx();
        (await lectura.Entidades.SingleAsync(x => x.Id == e.Id)).Derivado.Should().Be("ana maria");
        var h = await HistorialAsync(lectura, e.Id);
        h.Should().HaveCount(2);
        Cambios(h[0]).EnumerateObject().Select(p => p.Name).Should().NotContain("Derivado");
        Cambios(h[1]).EnumerateObject().Select(p => p.Name).Should().Equal("Nombre");
    }

    [Fact]
    public async Task Un_cambio_solo_en_columna_NoAuditar_se_guarda_sin_crear_auditoria()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana", Derivado = "a" };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        e.Derivado = "b";
        await ctx.SaveChangesAsync();

        await using var lectura = Ctx();
        (await lectura.Entidades.SingleAsync(x => x.Id == e.Id)).Derivado.Should().Be("b");
        (await HistorialAsync(lectura, e.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Borrar_fisicamente_esta_prohibido()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana" };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        ctx.Remove(e);
        var guardar = () => ctx.SaveChangesAsync();

        await guardar.Should().ThrowAsync<InvalidOperationException>().WithMessage("*se anulan*");
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(1978, null, null)]
    [InlineData(1978, 8, null)]
    [InlineData(1978, 8, 13)]
    public async Task FechaParcial_se_guarda_y_se_lee_igual_y_calcula_la_clave_de_orden(int? anio, int? mes, int? dia)
    {
        var fecha = anio is null
            ? FechaParcial.Vacia
            : FechaParcial.Crear((short)anio, (short?)mes, (short?)dia, new DateOnly(2026, 11, 7));

        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana", Fecha = fecha };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        await using var lectura = Ctx();
        var leida = await lectura.Entidades.SingleAsync(x => x.Id == e.Id);
        leida.Fecha.Should().NotBeNull();
        leida.Fecha.Should().Be(fecha);

        var clave = await lectura.Entidades.Where(x => x.Id == e.Id)
            .Select(x => EF.Property<int>(x, "fecha_clave_orden")).SingleAsync();
        clave.Should().Be(fecha.ClaveOrden);
    }

    [Fact]
    public async Task Cambio_de_fecha_se_audita_como_texto_de_libro()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana", Fecha = FechaParcial.Crear(1978, null, null, new DateOnly(2026, 11, 7)) };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        e.Fecha = FechaParcial.Crear(1978, 8, 13, new DateOnly(2026, 11, 7));
        await ctx.SaveChangesAsync();

        await using var lectura = Ctx();
        var h = await HistorialAsync(lectura, e.Id);
        h.Should().HaveCount(2);
        var c = Cambios(h[1]).GetProperty("Fecha");
        c.GetProperty("anterior").GetString().Should().Be("1978");
        c.GetProperty("nuevo").GetString().Should().Be("13 de agosto de 1978");
    }

    [Fact]
    public async Task Lista_de_texto_se_audita_como_texto_unido()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana", Lista = ["Luis Mena", "Rosa Díaz"] };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        // Cambio de orden y quitar uno: cada uno es una modificación real.
        e.Lista = ["Rosa Díaz", "Luis Mena"];
        await ctx.SaveChangesAsync();
        e.Lista = ["Rosa Díaz"];
        await ctx.SaveChangesAsync();

        await using var lectura = Ctx();
        (await lectura.Entidades.SingleAsync(x => x.Id == e.Id)).Lista.Should().Equal("Rosa Díaz");

        var h = await HistorialAsync(lectura, e.Id);
        h.Should().HaveCount(3);
        Cambios(h[0]).GetProperty("Lista").GetProperty("nuevo").GetString().Should().Be("Luis Mena; Rosa Díaz");
        var orden = Cambios(h[1]).GetProperty("Lista");
        orden.GetProperty("anterior").GetString().Should().Be("Luis Mena; Rosa Díaz");
        orden.GetProperty("nuevo").GetString().Should().Be("Rosa Díaz; Luis Mena");
        var quitar = Cambios(h[2]).GetProperty("Lista");
        quitar.GetProperty("anterior").GetString().Should().Be("Rosa Díaz; Luis Mena");
        quitar.GetProperty("nuevo").GetString().Should().Be("Rosa Díaz");
    }

    [Fact]
    public async Task Lista_igual_no_audita_y_lista_vacia_no_aparece_en_el_alta()
    {
        await using var ctx = Ctx();
        var e = new EntidadPrueba { Nombre = "Ana" };
        ctx.Add(e);
        await ctx.SaveChangesAsync();

        e.Lista = []; // otra lista, mismo contenido
        await ctx.SaveChangesAsync();
        e.Lista = ["A"];
        await ctx.SaveChangesAsync();
        e.Lista = ["A"]; // otra instancia, mismo contenido
        await ctx.SaveChangesAsync();

        await using var lectura = Ctx();
        var h = await HistorialAsync(lectura, e.Id);
        h.Should().HaveCount(2); // Alta + agregar "A"
        Cambios(h[0]).EnumerateObject().Select(p => p.Name).Should().NotContain("Lista");
        Cambios(h[1]).GetProperty("Lista").GetProperty("anterior").ValueKind.Should().Be(JsonValueKind.Null);
        Cambios(h[1]).GetProperty("Lista").GetProperty("nuevo").GetString().Should().Be("A");
    }

    [Fact]
    public async Task Edicion_concurrente_lanza_conflicto_de_concurrencia()
    {
        long id;
        await using (var ctx = Ctx())
        {
            var e = new EntidadPrueba { Nombre = "Ana" };
            ctx.Add(e);
            await ctx.SaveChangesAsync();
            id = e.Id;
        }

        await using var a = Ctx();
        await using var b = Ctx();
        var ea = await a.Entidades.SingleAsync(x => x.Id == id);
        var eb = await b.Entidades.SingleAsync(x => x.Id == id);

        ea.Nombre = "Primero";
        await a.SaveChangesAsync();

        eb.Nombre = "Segundo";
        var guardar = () => b.SaveChangesAsync();

        await guardar.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
