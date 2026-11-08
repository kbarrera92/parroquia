using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Configuracion;
using Parroquia.Infraestructura.Configuracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Infraestructura;

public class ServicioParrocoPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    // El reloj falso marca 2026-11-07 15:00 UTC = 2026-11-07 09:00 en Guatemala (UTC-6, sin horario de verano).
    private static readonly DateOnly Hoy = new(2026, 11, 7);

    private static ServicioParroco Servicio(ParroquiaDbContext ctx, BaseTemporal bd) => new(ctx, bd.Reloj);

    [Fact]
    public async Task Sin_registros_no_hay_parroco_actual()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        (await Servicio(ctx, bd).ObtenerActualAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Primer_registro_queda_como_actual_con_titulo_por_defecto()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);

        var r = await s.RegistrarNuevoAsync("P. Nombre Uno", null, new DateOnly(2020, 1, 15));

        r.Ok.Should().BeTrue(r.Error);
        var actual = await s.ObtenerActualAsync();
        actual!.Nombre.Should().Be("P. Nombre Uno");
        actual.Titulo.Should().Be("Párroco");
        actual.EsActual.Should().BeTrue();
        actual.VigenteDesde.Should().Be(new DateOnly(2020, 1, 15));
        actual.VigenteHasta.Should().BeNull();
    }

    [Fact]
    public async Task Registrar_uno_nuevo_cierra_el_anterior_el_dia_previo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await s.RegistrarNuevoAsync("P. Anterior", "Párroco", new DateOnly(2020, 1, 15));

        var r = await s.RegistrarNuevoAsync("P. Nuevo", "Párroco", new DateOnly(2026, 2, 1));

        r.Ok.Should().BeTrue(r.Error);
        var historial = await s.HistorialAsync();
        historial.Select(p => p.Nombre).Should().Equal("P. Nuevo", "P. Anterior"); // más reciente primero
        historial.Count(p => p.EsActual).Should().Be(1);
        historial[0].EsActual.Should().BeTrue();
        historial[1].EsActual.Should().BeFalse();
        historial[1].VigenteHasta.Should().Be(new DateOnly(2026, 1, 31));
    }

    [Theory]
    [InlineData("2020-01-15")]   // igual a la vigencia actual
    [InlineData("2019-12-31")]   // anterior a la vigencia actual
    [InlineData("2026-11-08")]   // futura (hoy en Guatemala es 2026-11-07)
    public async Task Fecha_de_inicio_invalida_se_rechaza_y_no_cambia_nada(string desde)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await s.RegistrarNuevoAsync("P. Anterior", "Párroco", new DateOnly(2020, 1, 15));

        var r = await s.RegistrarNuevoAsync("P. Nuevo", "Párroco", DateOnly.Parse(desde));

        r.Ok.Should().BeFalse();
        r.Error.Should().NotBeNullOrWhiteSpace();
        var historial = await s.HistorialAsync();
        historial.Should().ContainSingle();
        historial[0].EsActual.Should().BeTrue();
        historial[0].VigenteHasta.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Nombre_vacio_se_rechaza(string? nombre)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).RegistrarNuevoAsync(nombre, "Párroco", Hoy);

        r.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Hoy_en_Guatemala_se_acepta_aunque_en_UTC_ya_sea_otro_dia()
    {
        var bd = await servidor.NuevaBaseAsync();
        bd.Reloj.Ahora = new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero); // 21:00 del 7-nov en Guatemala
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).RegistrarNuevoAsync("P. Nuevo", "Párroco", Hoy);

        r.Ok.Should().BeTrue(r.Error);
    }

    [Fact]
    public async Task Corregir_el_actual_cambia_nombre_y_titulo_y_queda_auditado()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await s.RegistrarNuevoAsync("P. Con Errata", "Párroco", new DateOnly(2020, 1, 15));

        var r = await s.CorregirActualAsync("P. Sin Errata", "Administrador parroquial");

        r.Ok.Should().BeTrue(r.Error);
        var actual = (await s.ObtenerActualAsync())!;
        actual.Nombre.Should().Be("P. Sin Errata");
        actual.Titulo.Should().Be("Administrador parroquial");

        await using var lectura = bd.Crear();
        var acciones = await lectura.Auditorias.Where(a => a.Entidad == nameof(ParrocoVigente) && a.EntidadId == actual.Id)
            .OrderBy(a => a.Id).Select(a => a.Accion).ToListAsync();
        acciones.Should().Equal(AccionAuditoria.Alta, AccionAuditoria.Modificacion);
    }

    [Fact]
    public async Task Corregir_sin_parroco_actual_falla()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).CorregirActualAsync("P. Nadie", "Párroco");

        r.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task La_base_de_datos_impide_dos_parrocos_actuales()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        ctx.Parrocos.Add(new ParrocoVigente { Nombre = "A", Titulo = "Párroco", VigenteDesde = new DateOnly(2020, 1, 1), EsActual = true });
        ctx.Parrocos.Add(new ParrocoVigente { Nombre = "B", Titulo = "Párroco", VigenteDesde = new DateOnly(2021, 1, 1), EsActual = true });

        var guardar = () => ctx.SaveChangesAsync();

        await guardar.Should().ThrowAsync<DbUpdateException>();
    }
}
