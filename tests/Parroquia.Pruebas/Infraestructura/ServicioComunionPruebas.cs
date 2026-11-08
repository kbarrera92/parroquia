using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Pruebas.Infraestructura;

/// <summary>Datos sintéticos (CLAUDE.md regla 3). Una base nueva por prueba.</summary>
public class ServicioComunionPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private static DatosComunionFormulario Datos(
        string libro = "3", string folio = "60", string partida = "12",
        string nombres = "Ana María", string apellidos = "Prueba Ejemplo", string fecha = "2/10/2004",
        string? ministro = null, string? notas = null, bool revisar = false, string? motivo = null) =>
        new(libro, folio, partida, nombres, apellidos, fecha, ministro, notas, revisar, motivo);

    private static ServicioComunion Servicio(ParroquiaDbContext ctx, BaseTemporal bd) => new(ctx, bd.Reloj);

    private static async Task<long> CrearAsync(ServicioComunion s, DatosComunionFormulario d)
    {
        var r = await s.GuardarAsync(d);
        r.Estado.Should().Be(EstadoGuardado.Ok);
        return r.Id;
    }

    [Fact]
    public async Task Alta_guarda_textos_limpios_tal_cual_calcula_derivadas_y_audita_sin_ellas()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var id = await CrearAsync(Servicio(ctx, bd), Datos(nombres: "  José   Ángel ", apellidos: "de la Peña", fecha: "2/10/2004"));

        await using var lectura = bd.Crear();
        var c = await lectura.Comuniones.SingleAsync(x => x.Id == id);
        c.Nombres.Should().Be("José Ángel");
        c.Apellidos.Should().Be("de la Peña");
        c.FechaComunion.ATexto().Should().Be("2 de octubre de 2004");
        c.BusquedaNormalizada.Should().Be("jose angel pena");
        c.CreadoPor.Should().Be("capturista1");

        var auditoria = await lectura.Auditorias.SingleAsync(a => a.Entidad == nameof(Comunion) && a.EntidadId == id);
        auditoria.Accion.Should().Be(AccionAuditoria.Alta);
        var campos = JsonDocument.Parse(auditoria.CambiosJson).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        campos.Should().Contain(["Libro", "Folio", "Partida", "Nombres", "Apellidos", "FechaComunion"]);
        campos.Should().NotContain(["LibroOrden", "FolioOrden", "PartidaOrden", "BusquedaNormalizada"]);
    }

    [Fact]
    public async Task Libro_folio_y_partida_son_obligatorios()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).GuardarAsync(Datos(libro: " ", folio: "", partida: ""));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Select(e => e.Campo).Should().BeEquivalentTo(["Libro", "Folio", "Partida"]);
        (await ctx.Comuniones.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Nombres_y_apellidos_son_obligatorios_salvo_Revisar_con_motivo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);

        var sin = await s.GuardarAsync(Datos(nombres: "", apellidos: ""));
        sin.Estado.Should().Be(EstadoGuardado.Invalido);
        sin.Errores!.Select(e => e.Campo).Should().BeEquivalentTo(["Nombres", "Apellidos"]);

        var sinMotivo = await s.GuardarAsync(Datos(nombres: "", apellidos: "", revisar: true));
        sinMotivo.Estado.Should().Be(EstadoGuardado.Invalido);
        sinMotivo.Errores!.Should().ContainSingle(e => e.Campo == "MotivoRevision");

        var conMotivo = await s.GuardarAsync(Datos(nombres: "", apellidos: "", revisar: true, motivo: "Tinta borrosa"));
        conMotivo.Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Theory]
    [InlineData("", "año")]
    [InlineData("sábado", "no es válida")]
    [InlineData("31/2/2004", "día no es válido")]
    [InlineData("2/10/2999", "año debe estar")]
    [InlineData("1400", "año debe estar")]
    public async Task Fecha_obligatoria_con_al_menos_el_anio_y_valida(string fecha, string fragmentoError)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).GuardarAsync(Datos(fecha: fecha));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Single(e => e.Campo == "Fecha").Mensaje.Should().Contain(fragmentoError);
    }

    [Theory]
    [InlineData("2004", "2004")]
    [InlineData("10/2004", "octubre de 2004")]
    public async Task Acepta_fechas_parciales(string entrada, string esperado)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);

        var id = await CrearAsync(s, Datos(fecha: entrada));

        (await s.ObtenerVistaAsync(id))!.Fecha.Should().Be(esperado);
    }

    [Fact]
    public async Task Ministro_del_catalogo_se_enlaza_y_otro_nombre_queda_como_texto()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        ctx.Ministros.Add(new Ministro { Nombre = "P. Álvaro Pérez" });
        await ctx.SaveChangesAsync();
        var s = Servicio(ctx, bd);

        var idCatalogo = await CrearAsync(s, Datos(partida: "1", ministro: "p. alvaro perez"));
        var idLibre = await CrearAsync(s, Datos(partida: "2", nombres: "Otra", apellidos: "Persona", ministro: "Mons. Invitado"));

        await using var lectura = bd.Crear();
        var a = await lectura.Comuniones.Include(c => c.Ministro).SingleAsync(c => c.Id == idCatalogo);
        a.MinistroId.Should().NotBeNull();
        a.MinistroTexto.Should().BeNull();
        a.NombreMinistro.Should().Be("P. Álvaro Pérez");

        var b = await lectura.Comuniones.SingleAsync(c => c.Id == idLibre);
        b.MinistroId.Should().BeNull();
        b.NombreMinistro.Should().Be("Mons. Invitado");
    }

    [Fact]
    public async Task Mismo_libro_folio_y_partida_advierte_sin_guardar_y_se_puede_confirmar()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var original = await CrearAsync(s, Datos(nombres: "Uno", apellidos: "Primero"));

        var otro = Datos(nombres: "Dos", apellidos: "Segundo", fecha: "1/1/2005");
        var aviso = await s.GuardarAsync(otro);

        aviso.Estado.Should().Be(EstadoGuardado.Duplicados);
        aviso.Duplicados!.Should().ContainSingle(d => d.Id == original);
        (await ctx.Comuniones.CountAsync()).Should().Be(1);

        var confirmado = await s.GuardarAsync(otro, confirmarDuplicado: true);
        confirmado.Estado.Should().Be(EstadoGuardado.Ok);
        (await ctx.Comuniones.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Misma_persona_y_fecha_en_otro_libro_advierte_pero_otra_fecha_no()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await CrearAsync(s, Datos(libro: "1", folio: "1", partida: "1"));

        var mismaFecha = await s.GuardarAsync(Datos(libro: "9", folio: "9", partida: "9", nombres: "ana maria", apellidos: "PRUEBA ejemplo"));
        var otraFecha = await s.GuardarAsync(Datos(libro: "9", folio: "9", partida: "9", fecha: "3/10/2004"));

        mismaFecha.Estado.Should().Be(EstadoGuardado.Duplicados);
        otraFecha.Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Fact]
    public async Task Editar_no_se_cuenta_a_si_mismo_como_duplicado_y_audita_el_cambio()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var id = await CrearAsync(s, Datos());
        var version = (await s.ObtenerVistaAsync(id))!.Version;

        var r = await s.GuardarAsync(Datos(apellidos: "Corregido"), id, version);

        r.Estado.Should().Be(EstadoGuardado.Ok);
        await using var lectura = bd.Crear();
        var historial = await lectura.Auditorias.Where(a => a.EntidadId == id && a.Entidad == nameof(Comunion))
            .OrderBy(a => a.Id).ToListAsync();
        historial.Select(a => a.Accion).Should().Equal(AccionAuditoria.Alta, AccionAuditoria.Modificacion);
        var cambios = JsonDocument.Parse(historial[1].CambiosJson).RootElement;
        cambios.EnumerateObject().Select(p => p.Name).Should().Equal("Apellidos");
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
        var tardio = await a.GuardarAsync(Datos(apellidos: "De A"), id, versionVista);

        tardio.Estado.Should().Be(EstadoGuardado.Conflicto);
        await using var lectura = bd.Crear();
        (await lectura.Comuniones.SingleAsync(c => c.Id == id)).Apellidos.Should().Be("De B");
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
        (await s.AnularAsync(id, "Otra vez")).Ok.Should().BeFalse();

        (await s.BuscarAsync(new FiltroComunion(), 1)).Total.Should().Be(0);
        (await s.BuscarAsync(new FiltroComunion(IncluirAnulados: true), 1)).Items.Should().ContainSingle(f => f.Anulado);
        (await s.ObtenerVistaAsync(id)).Should().BeNull();
        (await s.ObtenerVistaAsync(id, incluirAnulados: true))!.MotivoAnulacion.Should().Be("Registro duplicado");

        var edicion = await s.GuardarAsync(Datos(), id);
        edicion.Estado.Should().Be(EstadoGuardado.Anulado);

        (await s.RevertirAnulacionAsync(id)).Ok.Should().BeTrue();
        (await s.BuscarAsync(new FiltroComunion(), 1)).Total.Should().Be(1);

        await using var lectura = bd.Crear();
        var acciones = await lectura.Auditorias.Where(a => a.EntidadId == id && a.Entidad == nameof(Comunion))
            .OrderBy(a => a.Id).Select(a => a.Accion).ToListAsync();
        acciones.Should().Equal(AccionAuditoria.Alta, AccionAuditoria.Anulacion, AccionAuditoria.RevertirAnulacion);
    }

    [Fact]
    public async Task Busqueda_parcial_sin_tildes_ni_mayusculas_y_tolerante_a_particulas()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await CrearAsync(s, Datos(partida: "1", nombres: "Juan Carlos", apellidos: "Hidalgo Arévalo"));
        await CrearAsync(s, Datos(partida: "2", nombres: "María José", apellidos: "de la Cruz"));
        await CrearAsync(s, Datos(partida: "3", nombres: "Pedro", apellidos: "Otro"));

        (await s.BuscarAsync(new FiltroComunion("hidalgo arevalo"), 1)).Items.Select(f => f.Apellidos)
            .Should().Equal("Hidalgo Arévalo");
        (await s.BuscarAsync(new FiltroComunion("ARÉVA"), 1)).Total.Should().Be(1);
        (await s.BuscarAsync(new FiltroComunion("maria jose de la cruz"), 1)).Total.Should().Be(1);
        (await s.BuscarAsync(new FiltroComunion("cruz maria"), 1)).Total.Should().Be(1);
        (await s.BuscarAsync(new FiltroComunion("zzz"), 1)).Total.Should().Be(0);
        (await s.BuscarAsync(new FiltroComunion(), 1)).Total.Should().Be(3);
    }

    [Fact]
    public async Task Filtros_por_libro_folio_anios_y_revisar()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await CrearAsync(s, Datos(libro: "3", folio: "2 bis", partida: "1", nombres: "A", apellidos: "Uno", fecha: "1999"));
        await CrearAsync(s, Datos(libro: "4", folio: "10", partida: "1", nombres: "B", apellidos: "Dos", fecha: "5/2004"));
        await CrearAsync(s, Datos(libro: "4", folio: "11", partida: "1", nombres: "C", apellidos: "Tres", fecha: "2010", revisar: true));

        (await s.BuscarAsync(new FiltroComunion(Libro: "4"), 1)).Total.Should().Be(2);
        (await s.BuscarAsync(new FiltroComunion(Folio: "2 BIS"), 1)).Items.Single().Apellidos.Should().Be("Uno");
        (await s.BuscarAsync(new FiltroComunion(AnioDesde: 2000, AnioHasta: 2005), 1)).Items.Single().Apellidos.Should().Be("Dos");
        (await s.BuscarAsync(new FiltroComunion(AnioDesde: 2004), 1)).Total.Should().Be(2);
        (await s.BuscarAsync(new FiltroComunion(SoloRevisar: true), 1)).Items.Single().Apellidos.Should().Be("Tres");
    }

    [Fact]
    public async Task Listado_ordena_por_apellidos_y_luego_libro_folio_y_partida_en_orden_natural()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        foreach (var folio in new[] { "10", "2 bis", "2" })
            await CrearAsync(s, Datos(folio: folio, partida: "1", nombres: "Mismo", apellidos: "Apellido", fecha: $"{1990 + folio.Length}"));
        await CrearAsync(s, Datos(folio: "1", partida: "1", nombres: "Otro", apellidos: "Aaa"));

        var pagina = await s.BuscarAsync(new FiltroComunion(), 1);

        pagina.Items.Select(f => $"{f.Apellidos}/{f.Folio}").Should().Equal("Aaa/1", "Apellido/2", "Apellido/2 bis", "Apellido/10");
    }

    [Fact]
    public async Task Paginacion_ajusta_la_pagina_y_cuenta_el_total()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        for (var i = 1; i <= 5; i++)
            await CrearAsync(s, Datos(partida: i.ToString(), nombres: $"N{i}", apellidos: $"A{i}", fecha: $"{2000 + i}"));

        var segunda = await s.BuscarAsync(new FiltroComunion(), 2, tamano: 2);
        var fuera = await s.BuscarAsync(new FiltroComunion(), 99, tamano: 2);

        segunda.Total.Should().Be(5);
        segunda.TotalPaginas.Should().Be(3);
        segunda.Items.Select(f => f.Apellidos).Should().Equal("A3", "A4");
        fuera.PaginaActual.Should().Be(3);
        fuera.Items.Should().ContainSingle();
    }
}
