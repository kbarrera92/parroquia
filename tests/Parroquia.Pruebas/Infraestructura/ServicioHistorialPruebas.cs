using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Historial;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Infraestructura;

public class ServicioHistorialPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private static readonly DateTimeOffset Base = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero); // 09:00 en Guatemala

    private static Auditoria Aud(string usuario, string entidad, DateTimeOffset fecha, long entidadId = 1) => new()
    {
        Entidad = entidad, EntidadId = entidadId, Accion = AccionAuditoria.Alta,
        UsuarioNombre = usuario, Fecha = fecha, CambiosJson = "{}",
    };

    private static BitacoraAcceso Bit(string usuario, EventoAcceso evento, DateTimeOffset fecha, string? entidad = null) => new()
    {
        UsuarioNombre = usuario, Evento = evento, Fecha = fecha, Entidad = entidad,
    };

    private async Task<BaseTemporal> ConAuditoriasAsync(params Auditoria[] filas)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        ctx.Auditorias.AddRange(filas);
        await ctx.SaveChangesAsync();
        return bd;
    }

    [Fact]
    public async Task Ordena_lo_mas_reciente_primero()
    {
        var bd = await ConAuditoriasAsync(
            Aud("ana", "Ministro", Base), Aud("ana", "Ministro", Base.AddHours(2)), Aud("ana", "Ministro", Base.AddHours(1)));
        await using var ctx = bd.Crear();

        var r = await new ServicioHistorial(ctx).AuditoriaAsync(new FiltroHistorial(), 1);

        r.Items.Select(a => a.Fecha).Should().BeInDescendingOrder();
        r.Total.Should().Be(3);
    }

    [Fact]
    public async Task Filtra_por_usuario_sin_importar_mayusculas_ni_parte_del_nombre()
    {
        var bd = await ConAuditoriasAsync(
            Aud("Capturista_Uno", "Ministro", Base), Aud("secretaria1", "Ministro", Base), Aud("capturista_dos", "Ministro", Base));
        await using var ctx = bd.Crear();

        var r = await new ServicioHistorial(ctx).AuditoriaAsync(new FiltroHistorial(Usuario: "CAPTURISTA"), 1);

        r.Items.Select(a => a.UsuarioNombre).Should().BeEquivalentTo("Capturista_Uno", "capturista_dos");
    }

    [Theory]
    [InlineData("admin_prueba", new[] { "admin_prueba" })]   // _ literal, no comodín
    [InlineData("min_pr", new[] { "admin_prueba" })]
    [InlineData("adminXprueba", new string[0])]
    [InlineData("%", new string[0])]                         // % literal
    [InlineData("a\\", new string[0])]                       // \ literal
    public async Task Los_comodines_de_LIKE_en_el_filtro_de_usuario_se_toman_literalmente(string filtro, string[] esperados)
    {
        var bd = await ConAuditoriasAsync(
            Aud("admin_prueba", "X", Base), Aud("otro", "X", Base));
        await using var ctx = bd.Crear();

        var r = await new ServicioHistorial(ctx).AuditoriaAsync(new FiltroHistorial(Usuario: filtro), 1);

        r.Items.Select(a => a.UsuarioNombre).Should().BeEquivalentTo(esperados);
    }

    [Fact]
    public async Task Filtra_por_entidad_exacta()
    {
        var bd = await ConAuditoriasAsync(Aud("ana", "Ministro", Base), Aud("ana", "Tratamiento", Base));
        await using var ctx = bd.Crear();

        var r = await new ServicioHistorial(ctx).AuditoriaAsync(new FiltroHistorial(Entidad: "Tratamiento"), 1);

        r.Items.Should().ContainSingle().Which.Entidad.Should().Be("Tratamiento");
    }

    [Fact]
    public async Task Rango_de_fechas_usa_el_dia_de_Guatemala_e_incluye_los_extremos()
    {
        var bd = await ConAuditoriasAsync(
            Aud("ana", "X", new DateTimeOffset(2026, 11, 6, 5, 59, 0, TimeSpan.Zero), 1),   // 5-nov 23:59 GT: fuera
            Aud("ana", "X", new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero), 2),    // 6-nov 00:00 GT: dentro
            Aud("ana", "X", new DateTimeOffset(2026, 11, 8, 5, 59, 0, TimeSpan.Zero), 3),   // 7-nov 23:59 GT: dentro
            Aud("ana", "X", new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero), 4));   // 8-nov 00:00 GT: fuera
        await using var ctx = bd.Crear();

        var r = await new ServicioHistorial(ctx).AuditoriaAsync(
            new FiltroHistorial(Desde: new DateOnly(2026, 11, 6), Hasta: new DateOnly(2026, 11, 7)), 1);

        r.Items.Select(a => a.EntidadId).Should().BeEquivalentTo([2L, 3L]);
    }

    [Fact]
    public async Task Solo_desde_o_solo_hasta_funcionan()
    {
        var bd = await ConAuditoriasAsync(
            Aud("ana", "X", new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero), 1),
            Aud("ana", "X", new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero), 2));
        await using var ctx = bd.Crear();
        var s = new ServicioHistorial(ctx);

        (await s.AuditoriaAsync(new FiltroHistorial(Desde: new DateOnly(2026, 3, 1)), 1)).Items
            .Select(a => a.EntidadId).Should().Equal(2L);
        (await s.AuditoriaAsync(new FiltroHistorial(Hasta: new DateOnly(2026, 3, 1)), 1)).Items
            .Select(a => a.EntidadId).Should().Equal(1L);
    }

    [Fact]
    public async Task Pagina_los_resultados_y_ajusta_paginas_fuera_de_rango()
    {
        var bd = await ConAuditoriasAsync(
            [.. Enumerable.Range(0, 25).Select(i => Aud("ana", "X", Base.AddMinutes(i), i))]);
        await using var ctx = bd.Crear();
        var s = new ServicioHistorial(ctx);

        var p1 = await s.AuditoriaAsync(new FiltroHistorial(), 1, tamano: 10);
        var p3 = await s.AuditoriaAsync(new FiltroHistorial(), 3, tamano: 10);

        p1.Items.Should().HaveCount(10);
        p1.Total.Should().Be(25);
        p1.TotalPaginas.Should().Be(3);
        p3.Items.Should().HaveCount(5);
        p1.Items.Select(a => a.EntidadId).Intersect(p3.Items.Select(a => a.EntidadId)).Should().BeEmpty();

        (await s.AuditoriaAsync(new FiltroHistorial(), 99, tamano: 10)).PaginaActual.Should().Be(3);
        (await s.AuditoriaAsync(new FiltroHistorial(), 0, tamano: 10)).PaginaActual.Should().Be(1);
    }

    [Fact]
    public async Task Sin_resultados_devuelve_una_pagina_vacia()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await new ServicioHistorial(ctx).AuditoriaAsync(new FiltroHistorial(), 5);

        r.Items.Should().BeEmpty();
        r.Total.Should().Be(0);
        r.PaginaActual.Should().Be(1);
        r.TotalPaginas.Should().Be(1);
    }

    [Fact]
    public async Task Bitacora_filtra_por_usuario_entidad_y_fechas()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using (var ctx = bd.Crear())
        {
            ctx.BitacoraAccesos.AddRange(
                Bit("ana", EventoAcceso.InicioSesion, Base),
                Bit("ana", EventoAcceso.VerRegistro, Base.AddHours(1), "Comunion"),
                Bit("beto", EventoAcceso.VerRegistro, Base.AddHours(2), "Comunion"),
                Bit("ana", EventoAcceso.CierreSesion, Base.AddDays(-30)));
            await ctx.SaveChangesAsync();
        }
        await using var lectura = bd.Crear();
        var s = new ServicioHistorial(lectura);

        (await s.BitacoraAsync(new FiltroHistorial(Usuario: "ana"), 1)).Total.Should().Be(3);
        (await s.BitacoraAsync(new FiltroHistorial(Entidad: "Comunion"), 1)).Total.Should().Be(2);
        (await s.BitacoraAsync(new FiltroHistorial(Usuario: "ana", Desde: new DateOnly(2026, 11, 1)), 1))
            .Items.Select(b => b.Evento).Should().Equal(EventoAcceso.VerRegistro, EventoAcceso.InicioSesion);
    }
}
