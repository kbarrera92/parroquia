using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Pruebas.Infraestructura;

/// <summary>Datos sintéticos (CLAUDE.md regla 3). Una base nueva por prueba.</summary>
public class ServicioConfirmacionPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private static DatosConfirmacionFormulario Datos(
        string libro = "1", string folio = "349", string partida = "12",
        string nombres = "Ana María", string apellidos = "Prueba Ejemplo",
        string fechaConfirmacion = "17/10/2004", string?[]? padrinos = null,
        string? ministro = null, string? notas = null,
        bool revisar = false, string? motivo = null) =>
        new(libro, folio, partida, nombres, apellidos, fechaConfirmacion,
            padrinos ?? ["Luis Mena", "Rosa Díaz"], ministro, notas, revisar, motivo);

    private static ServicioConfirmacion Servicio(ParroquiaDbContext ctx, BaseTemporal bd) => new(ctx, bd.Reloj);

    private static async Task<long> CrearAsync(ServicioConfirmacion s, DatosConfirmacionFormulario d)
    {
        var r = await s.GuardarAsync(d);
        r.Estado.Should().Be(EstadoGuardado.Ok);
        return r.Id;
    }

    [Fact]
    public async Task Alta_guarda_todo_calcula_derivadas_y_audita_padrinos_unidos()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var id = await CrearAsync(Servicio(ctx, bd),
            Datos(nombres: "  José   Ángel ", apellidos: "de la Peña", notas: " Obispo visitante "));

        await using var lectura = bd.Crear();
        var c = await lectura.Confirmaciones.SingleAsync(x => x.Id == id);
        c.Nombres.Should().Be("José Ángel");
        c.Apellidos.Should().Be("de la Peña");
        c.FechaConfirmacion.ATexto().Should().Be("17 de octubre de 2004");
        c.Padrinos.Should().Equal("Luis Mena", "Rosa Díaz");
        c.NotasMarginales.Should().Be("Obispo visitante");
        c.BusquedaNormalizada.Should().Be("jose angel pena");

        var auditoria = await lectura.Auditorias.SingleAsync(a => a.Entidad == nameof(Confirmacion) && a.EntidadId == id);
        var cambios = JsonDocument.Parse(auditoria.CambiosJson).RootElement;
        cambios.GetProperty("Padrinos").GetProperty("nuevo").GetString().Should().Be("Luis Mena; Rosa Díaz");
        cambios.EnumerateObject().Select(p => p.Name).Should()
            .Contain(["Libro", "Folio", "Partida", "FechaConfirmacion"])
            .And.NotContain(["BusquedaNormalizada", "LibroOrden"]);
    }

    [Fact]
    public async Task Libro_folio_partida_y_nombres_son_obligatorios()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).GuardarAsync(Datos(libro: " ", folio: "", partida: "", nombres: "", apellidos: ""));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Select(e => e.Campo).Should().BeEquivalentTo(["Libro", "Folio", "Partida", "Nombres", "Apellidos"]);
        (await ctx.Confirmaciones.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Revisar_con_motivo_admite_nombre_vacio()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);

        (await s.GuardarAsync(Datos(nombres: "", apellidos: "", revisar: true))).Errores!
            .Should().ContainSingle(e => e.Campo == "MotivoRevision");
        (await s.GuardarAsync(Datos(nombres: "", apellidos: "", revisar: true, motivo: "Tinta borrosa")))
            .Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Theory]
    [InlineData("", "año")]
    [InlineData("31/2/2004", "día no es válido")]
    [InlineData("hoy", "no es válida")]
    public async Task Fecha_obligatoria_con_al_menos_el_anio_y_valida(string fecha, string fragmento)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).GuardarAsync(Datos(fechaConfirmacion: fecha));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Single(e => e.Campo == "FechaConfirmacion").Mensaje.Should().Contain(fragmento);
    }

    [Theory]
    [InlineData("2004")]
    [InlineData("10/2004")]
    public async Task Acepta_fechas_parciales(string fecha)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        (await Servicio(ctx, bd).GuardarAsync(Datos(fechaConfirmacion: fecha))).Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Fact]
    public async Task Padrinos_se_limpian_sin_vacios_conservan_el_orden_y_son_opcionales()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);

        var id = await CrearAsync(s, Datos(partida: "1", padrinos: ["  Sonia   de Alvarado ", null, " ", "Gildardo Alvarado"]));
        var ninguno = await CrearAsync(s, Datos(partida: "2", nombres: "Otra", apellidos: "Persona", padrinos: []));

        (await s.ObtenerVistaAsync(id))!.Padrinos.Should().Equal("Sonia de Alvarado", "Gildardo Alvarado");
        (await s.ObtenerVistaAsync(ninguno))!.Padrinos.Should().BeEmpty();
    }

    [Fact]
    public async Task Padrinos_limita_cantidad_y_largo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);

        var muchos = await s.GuardarAsync(Datos(padrinos: [.. Enumerable.Range(1, ServicioConfirmacion.MaxPadrinos + 1).Select(i => $"Padrino {i}")]));
        var largo = await s.GuardarAsync(Datos(padrinos: [new string('x', 151)]));

        muchos.Errores!.Should().ContainSingle(e => e.Campo == "Padrinos");
        largo.Errores!.Should().ContainSingle(e => e.Campo == "Padrinos");
    }

    [Fact]
    public async Task Editar_padrinos_audita_el_cambio_y_sin_cambios_no_audita()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var id = await CrearAsync(s, Datos());
        var version = (await s.ObtenerVistaAsync(id))!.Version;

        // Mismos datos: ni historial nuevo ni error.
        (await s.GuardarAsync(Datos(), id, version)).Estado.Should().Be(EstadoGuardado.Ok);
        version = (await s.ObtenerVistaAsync(id))!.Version;
        (await s.GuardarAsync(Datos(padrinos: ["Luis Mena"]), id, version)).Estado.Should().Be(EstadoGuardado.Ok);

        await using var lectura = bd.Crear();
        var historial = await lectura.Auditorias.Where(a => a.EntidadId == id && a.Entidad == nameof(Confirmacion))
            .OrderBy(a => a.Id).ToListAsync();
        historial.Select(a => a.Accion).Should().Equal(AccionAuditoria.Alta, AccionAuditoria.Modificacion);
        var padrinos = JsonDocument.Parse(historial[1].CambiosJson).RootElement.GetProperty("Padrinos");
        padrinos.GetProperty("anterior").GetString().Should().Be("Luis Mena; Rosa Díaz");
        padrinos.GetProperty("nuevo").GetString().Should().Be("Luis Mena");
    }

    [Fact]
    public async Task Ministro_del_catalogo_se_enlaza_y_otro_nombre_queda_como_texto()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        ctx.Ministros.Add(new Ministro { Nombre = "Mons. Obispo Prueba" });
        await ctx.SaveChangesAsync();
        var s = Servicio(ctx, bd);

        var a = await CrearAsync(s, Datos(partida: "1", ministro: "mons. obispo prueba"));
        var b = await CrearAsync(s, Datos(partida: "2", nombres: "Otra", apellidos: "Persona", ministro: "Mons. Invitado"));

        (await s.ObtenerVistaAsync(a))!.Ministro.Should().Be("Mons. Obispo Prueba");
        (await s.ObtenerVistaAsync(b))!.Ministro.Should().Be("Mons. Invitado");
    }

    [Fact]
    public async Task Mismo_libro_folio_y_partida_advierte_sin_guardar_y_se_puede_confirmar()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var original = await CrearAsync(s, Datos(nombres: "Uno", apellidos: "Primero"));

        var otro = Datos(nombres: "Dos", apellidos: "Segundo", fechaConfirmacion: "1/1/2005");
        var aviso = await s.GuardarAsync(otro);

        aviso.Estado.Should().Be(EstadoGuardado.Duplicados);
        aviso.Duplicados!.Should().ContainSingle(d => d.Id == original);
        (await ctx.Confirmaciones.CountAsync()).Should().Be(1);
        (await s.GuardarAsync(otro, confirmarDuplicado: true)).Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Fact]
    public async Task Misma_persona_y_fecha_advierte_pero_otra_fecha_no()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await CrearAsync(s, Datos(libro: "1", folio: "1", partida: "1"));

        var igual = await s.GuardarAsync(Datos(libro: "9", folio: "9", partida: "9", nombres: "ana maria", apellidos: "PRUEBA ejemplo"));
        var otraFecha = await s.GuardarAsync(Datos(libro: "9", folio: "9", partida: "9", fechaConfirmacion: "18/10/2004"));

        igual.Estado.Should().Be(EstadoGuardado.Duplicados);
        otraFecha.Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Fact]
    public async Task Version_vieja_produce_conflicto_y_no_pisa_el_cambio_ajeno()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctxA = bd.Crear();
        await using var ctxB = bd.Crear();
        var a = Servicio(ctxA, bd);
        var b = Servicio(ctxB, bd);
        var id = await CrearAsync(a, Datos());
        var versionVista = (await a.ObtenerVistaAsync(id))!.Version;

        (await b.GuardarAsync(Datos(apellidos: "De B"), id, versionVista)).Estado.Should().Be(EstadoGuardado.Ok);
        (await a.GuardarAsync(Datos(apellidos: "De A"), id, versionVista)).Estado.Should().Be(EstadoGuardado.Conflicto);

        await using var lectura = bd.Crear();
        (await lectura.Confirmaciones.SingleAsync(x => x.Id == id)).Apellidos.Should().Be("De B");
    }

    [Fact]
    public async Task Anular_y_revertir_dejan_su_accion_en_la_auditoria_y_ocultan_del_listado()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var id = await CrearAsync(s, Datos());

        (await s.AnularAsync(id, " ")).Ok.Should().BeFalse();
        (await s.AnularAsync(id, "Registro duplicado")).Ok.Should().BeTrue();

        (await s.BuscarAsync(new FiltroConfirmacion(), 1)).Total.Should().Be(0);
        (await s.BuscarAsync(new FiltroConfirmacion(IncluirAnulados: true), 1)).Items.Should().ContainSingle(f => f.Anulado);
        (await s.ObtenerVistaAsync(id)).Should().BeNull();
        (await s.DatosParaConstanciaAsync(id)).Should().BeNull();
        (await s.GuardarAsync(Datos(), id)).Estado.Should().Be(EstadoGuardado.Anulado);

        (await s.RevertirAnulacionAsync(id)).Ok.Should().BeTrue();
        (await s.BuscarAsync(new FiltroConfirmacion(), 1)).Total.Should().Be(1);

        await using var lectura = bd.Crear();
        (await lectura.Auditorias.Where(a => a.EntidadId == id && a.Entidad == nameof(Confirmacion))
            .OrderBy(a => a.Id).Select(a => a.Accion).ToListAsync())
            .Should().Equal(AccionAuditoria.Alta, AccionAuditoria.Anulacion, AccionAuditoria.RevertirAnulacion);
    }

    [Fact]
    public async Task Busqueda_parcial_sin_tildes_filtros_y_orden_natural()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await CrearAsync(s, Datos(libro: "3", folio: "10", partida: "1", nombres: "Juan Carlos", apellidos: "Hidalgo Arévalo", fechaConfirmacion: "1999"));
        await CrearAsync(s, Datos(libro: "3", folio: "2 bis", partida: "1", nombres: "María José", apellidos: "de la Cruz", fechaConfirmacion: "5/2004"));
        await CrearAsync(s, Datos(libro: "4", folio: "2", partida: "1", nombres: "Pedro", apellidos: "Otro", fechaConfirmacion: "2010", revisar: true, motivo: "dudoso"));

        (await s.BuscarAsync(new FiltroConfirmacion("hidalgo arevalo"), 1)).Items.Single().Nombres.Should().Be("Juan Carlos");
        (await s.BuscarAsync(new FiltroConfirmacion("cruz maria"), 1)).Total.Should().Be(1);
        (await s.BuscarAsync(new FiltroConfirmacion(Libro: "3"), 1)).Total.Should().Be(2);
        (await s.BuscarAsync(new FiltroConfirmacion(Folio: "2 BIS"), 1)).Items.Single().Apellidos.Should().Be("de la Cruz");
        (await s.BuscarAsync(new FiltroConfirmacion(AnioDesde: 2000, AnioHasta: 2005), 1)).Items.Single().Apellidos.Should().Be("de la Cruz");
        (await s.BuscarAsync(new FiltroConfirmacion(SoloRevisar: true), 1)).Items.Single().Apellidos.Should().Be("Otro");
        (await s.BuscarAsync(new FiltroConfirmacion(), 1)).Items.Select(f => f.Apellidos)
            .Should().Equal("de la Cruz", "Hidalgo Arévalo", "Otro");
    }

    [Fact]
    public async Task Datos_para_constancia_incluye_padrinos_libro_y_folio()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var id = await CrearAsync(s, Datos());

        var d = (await s.DatosParaConstanciaAsync(id))!;

        d.Nombres.Should().Be("Ana María");
        d.FechaConfirmacion.ATexto().Should().Be("17 de octubre de 2004");
        d.Padrinos.Should().Equal("Luis Mena", "Rosa Díaz");
        d.Libro.Should().Be("1");
        d.Folio.Should().Be("349");
    }
}
