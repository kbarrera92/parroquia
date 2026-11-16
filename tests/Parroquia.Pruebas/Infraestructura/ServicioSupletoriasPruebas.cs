using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Numeracion;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Dominio.Supletorias;
using Parroquia.Infraestructura.Numeracion;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;
using Parroquia.Infraestructura.Supletorias;

namespace Parroquia.Pruebas.Infraestructura;

/// <summary>Partidas supletorias, probadas con Comunión (el patrón de los módulos). Datos sintéticos; una base nueva por prueba.</summary>
public class ServicioSupletoriasPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private static DatosSupletoriaFormulario Sup(
        bool sistema = false, string? libro = "1", string? folio = "5", string? partida = "200",
        string? fecha = "2/10/2026", string? testigo = "Felipa Prueba Ejemplo", string? parentesco = "Tía") =>
        new(sistema, libro, folio, partida, fecha, testigo, parentesco);

    private static DatosComunionFormulario Datos(
        DatosSupletoriaFormulario? supletoria, string? libro = null, string? folio = null, string? partida = null,
        string nombres = "Ana María", string apellidos = "Prueba Ejemplo", string fecha = "24/8/1965",
        bool revisar = false, string? motivo = null) =>
        new(libro, folio, partida, nombres, apellidos, fecha, null, null, revisar, motivo, Supletoria: supletoria);

    private static async Task AbrirLibroSupletoriasAsync(BaseTemporal bd, string numero = "1", int porFolio = 2, int folios = 3)
    {
        await using var ctx = bd.Crear();
        var r = await new ServicioNumeracion(ctx, bd.Reloj).AbrirAsync(
            new(Supletoria.ClaveLibroDigital, numero, 1, 1, porFolio, folios, ModoPartida.Correlativa));
        r.Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Con_original_conocido_el_registro_conserva_su_libro_y_la_supletoria_su_propio_numero()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await new ServicioComunion(ctx, bd.Reloj).GuardarAsync(Datos(Sup(), "80", "12", "340"));

        r.Estado.Should().Be(EstadoGuardado.Ok);
        await using var lectura = bd.Crear();
        var c = await lectura.Comuniones.SingleAsync(x => x.Id == r.Id);
        (c.Libro, c.Folio, c.Partida).Should().Be(("80", "12", "340"));
        var s = await lectura.Supletorias.SingleAsync();
        (s.TipoSacramento, s.RegistroId).Should().Be(("comunion", r.Id));
        (s.Libro, s.Folio, s.Partida).Should().Be(("1", "5", "200"));
        s.FechaSupletoria.ATexto().Should().Be("2 de octubre de 2026");
        (s.Testigo, s.ParentescoTestigo).Should().Be(("Felipa Prueba Ejemplo", "Tía"));
        s.LibroDigitalId.Should().BeNull();
    }

    [Fact]
    public async Task Sin_original_el_registro_toma_el_numero_supletorio_con_prefijo_y_se_encuentra_en_la_busqueda()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var servicio = new ServicioComunion(ctx, bd.Reloj);

        var r = await servicio.GuardarAsync(Datos(Sup(libro: "2", folio: "7", partida: "313")));

        r.Estado.Should().Be(EstadoGuardado.Ok);
        var vista = await servicio.ObtenerVistaAsync(r.Id);
        (vista!.Libro, vista.Folio, vista.Partida).Should().Be(("Supletorio 2", "7", "313"));
        vista.Supletoria.Should().NotBeNull();
        (await servicio.BuscarAsync(new FiltroComunion(Texto: "prueba ejemplo"), 1))
            .Items.Should().ContainSingle(f => f.Id == r.Id && f.Libro == "Supletorio 2");
    }

    [Fact]
    public async Task Numerada_por_el_sistema_entrega_partidas_correlativas_del_mismo_libro_de_supletorias()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirLibroSupletoriasAsync(bd, porFolio: 2);
        await using var ctx = bd.Crear();
        var servicio = new ServicioComunion(ctx, bd.Reloj);

        for (var i = 0; i < 3; i++)
            (await servicio.GuardarAsync(Datos(Sup(sistema: true), nombres: $"Persona {i}"))).Estado.Should().Be(EstadoGuardado.Ok);

        await using var lectura = bd.Crear();
        var supletorias = await lectura.Supletorias.OrderBy(s => s.Id).ToListAsync();
        supletorias.Select(s => (s.Libro, s.Folio, s.Partida)).Should().Equal(("1", "1", "1"), ("1", "1", "2"), ("1", "2", "3"));
        supletorias.Should().OnlyContain(s => s.EsAsientoDelSistema);
        var comuniones = await lectura.Comuniones.OrderBy(c => c.Id).ToListAsync();
        comuniones.Select(c => c.Libro).Should().OnlyContain(l => l == "Supletorio 1");
        comuniones.Should().OnlyContain(c => !c.EsAsientoDelSistema);
    }

    [Fact]
    public async Task Numerada_por_el_sistema_sin_libro_abierto_falla_sin_guardar_nada()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await new ServicioComunion(ctx, bd.Reloj).GuardarAsync(Datos(Sup(sistema: true)));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Single().Mensaje.Should().Contain("libro digital de supletorias");
        await using var lectura = bd.Crear();
        (await lectura.Comuniones.AnyAsync()).Should().BeFalse();
        (await lectura.Supletorias.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Un_numero_supletorio_transcrito_repetido_se_rechaza_aunque_la_primera_este_anulada()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var servicio = new ServicioComunion(ctx, bd.Reloj);
        var primera = await servicio.GuardarAsync(Datos(Sup()));
        (await servicio.AnularAsync(primera.Id, "Error de captura")).Ok.Should().BeTrue();

        var otra = await servicio.GuardarAsync(Datos(Sup(), nombres: "Otra", apellidos: "Persona"));

        otra.Estado.Should().Be(EstadoGuardado.Invalido);
        otra.Errores!.Single().Campo.Should().Be("Supletoria.Partida");
        await using var lectura = bd.Crear();
        (await lectura.Comuniones.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task No_se_transcribe_en_un_libro_supletorio_que_numera_el_sistema()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirLibroSupletoriasAsync(bd, numero: "9");
        await using var ctx = bd.Crear();

        var r = await new ServicioComunion(ctx, bd.Reloj).GuardarAsync(Datos(Sup(libro: "9")));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Single().Campo.Should().Be("Supletoria.Libro");
    }

    [Fact]
    public async Task Original_incompleto_se_rechaza_y_el_nombre_de_libro_Supletorio_esta_reservado()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var servicio = new ServicioComunion(ctx, bd.Reloj);

        var incompleto = await servicio.GuardarAsync(Datos(Sup(), libro: "80"));
        var reservado = await servicio.GuardarAsync(Datos(supletoria: null, libro: "Supletorio 1", folio: "1", partida: "1"));
        var reservadoConSupletoria = await servicio.GuardarAsync(Datos(Sup(), libro: "supletorio 4", folio: "1", partida: "1"));

        incompleto.Estado.Should().Be(EstadoGuardado.Invalido);
        reservado.Estado.Should().Be(EstadoGuardado.Invalido);
        reservadoConSupletoria.Estado.Should().Be(EstadoGuardado.Invalido);
        await using var lectura = bd.Crear();
        (await lectura.Comuniones.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Fecha_y_testigo_son_obligatorios_salvo_registro_Revisar_con_motivo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var servicio = new ServicioComunion(ctx, bd.Reloj);

        var sinTestigo = await servicio.GuardarAsync(Datos(Sup(testigo: null, fecha: "")));
        var revisar = await servicio.GuardarAsync(Datos(Sup(testigo: null), revisar: true, motivo: "El testigo no firmó"));

        sinTestigo.Estado.Should().Be(EstadoGuardado.Invalido);
        sinTestigo.Errores!.Select(e => e.Campo).Should().Contain(["Supletoria.Testigo", "Supletoria.Fecha"]);
        revisar.Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Fact]
    public async Task Anular_el_registro_anula_su_supletoria_y_revertir_la_reactiva_sin_reutilizar_el_numero()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirLibroSupletoriasAsync(bd);
        await using var ctx = bd.Crear();
        var servicio = new ServicioComunion(ctx, bd.Reloj);
        var r = await servicio.GuardarAsync(Datos(Sup(sistema: true)));

        (await servicio.AnularAsync(r.Id, "Duplicado")).Ok.Should().BeTrue();

        await using (var lectura = bd.Crear())
        {
            (await lectura.Supletorias.AnyAsync()).Should().BeFalse(); // el filtro global oculta las anuladas
            var s = await lectura.Supletorias.IgnoreQueryFilters().SingleAsync();
            (s.Anulado, s.MotivoAnulacion).Should().Be((true, "Duplicado"));
        }

        var siguiente = await servicio.GuardarAsync(Datos(Sup(sistema: true), nombres: "Siguiente"));
        await using (var lectura = bd.Crear())
        {
            var s2 = await lectura.Supletorias.SingleAsync(); // solo la nueva está activa
            s2.Partida.Should().Be("2"); // el número anulado no se reutiliza
            s2.RegistroId.Should().Be(siguiente.Id);
        }

        await using var ctx2 = bd.Crear();
        (await new ServicioComunion(ctx2, bd.Reloj).RevertirAnulacionAsync(r.Id)).Ok.Should().BeTrue();
        await using var final = bd.Crear();
        (await final.Supletorias.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Editar_actualiza_fecha_y_testigo_pero_no_cambia_el_numero_supletorio_ni_el_libro_del_registro()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var servicio = new ServicioComunion(ctx, bd.Reloj);
        var alta = await servicio.GuardarAsync(Datos(Sup(libro: "3", folio: "9", partida: "400")));
        var vista = (await servicio.ObtenerVistaAsync(alta.Id))!;

        // La página reenvía el libro del registro («Supletorio 3») y los datos de la supletoria editados.
        var nueva = Sup(libro: "3", folio: "9", partida: "400", fecha: "5/10/2026", testigo: "Testigo Nuevo", parentesco: "Madrina");
        var datos = Datos(nueva, vista.Libro, vista.Folio, vista.Partida);
        await using var ctx2 = bd.Crear();
        var r = await new ServicioComunion(ctx2, bd.Reloj).GuardarAsync(datos, alta.Id, vista.Version);

        r.Estado.Should().Be(EstadoGuardado.Ok);
        await using var lectura = bd.Crear();
        var s = await lectura.Supletorias.SingleAsync();
        (s.Testigo, s.ParentescoTestigo, s.FechaSupletoria.ATexto()).Should().Be(("Testigo Nuevo", "Madrina", "5 de octubre de 2026"));
        (await lectura.Comuniones.SingleAsync()).Libro.Should().Be("Supletorio 3");

        // Cambiar el libro/folio/partida de un registro sin original se rechaza.
        await using var ctx3 = bd.Crear();
        var v2 = (await new ServicioComunion(ctx3, bd.Reloj).ObtenerVistaAsync(alta.Id))!;
        var cambio = await new ServicioComunion(ctx3, bd.Reloj).GuardarAsync(Datos(nueva, "80", "1", "1"), alta.Id, v2.Version);
        cambio.Estado.Should().Be(EstadoGuardado.Invalido);
    }

    [Fact]
    public async Task La_supletoria_queda_en_la_auditoria_con_sus_campos_y_sin_derivadas()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        await new ServicioComunion(ctx, bd.Reloj).GuardarAsync(Datos(Sup()));

        await using var lectura = bd.Crear();
        var a = await lectura.Auditorias.SingleAsync(x => x.Entidad == nameof(Supletoria));
        a.Accion.Should().Be(AccionAuditoria.Alta);
        var campos = JsonDocument.Parse(a.CambiosJson).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        campos.Should().Contain(["TipoSacramento", "RegistroId", "Libro", "Folio", "Partida", "FechaSupletoria", "Testigo", "ParentescoTestigo"]);
        campos.Should().NotContain(["LibroOrden", "FolioOrden", "PartidaOrden"]);
    }

    [Fact]
    public async Task Dos_capturistas_a_la_vez_nunca_reciben_la_misma_partida_supletoria()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirLibroSupletoriasAsync(bd, porFolio: 10, folios: 5);

        async Task Capturista(string quien)
        {
            await using var ctx = bd.Crear();
            var servicio = new ServicioComunion(ctx, bd.Reloj);
            for (var i = 0; i < 8; i++)
                (await servicio.GuardarAsync(Datos(Sup(sistema: true), nombres: $"{quien} {i}"))).Estado.Should().Be(EstadoGuardado.Ok);
        }

        await Task.WhenAll(Capturista("A"), Capturista("B"), Capturista("C"));

        await using var lectura = bd.Crear();
        var partidas = (await lectura.Supletorias.Select(s => s.Partida).ToListAsync()).Select(int.Parse).Order().ToList();
        partidas.Should().Equal(Enumerable.Range(1, 24));
    }

    [Fact]
    public async Task El_listado_del_libro_filtra_por_sacramento_libro_y_orden_natural()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var servicio = new ServicioComunion(ctx, bd.Reloj);
        await servicio.GuardarAsync(Datos(Sup(folio: "10", partida: "2"), nombres: "Diez"));
        await servicio.GuardarAsync(Datos(Sup(folio: "9", partida: "1"), nombres: "Nueve"));
        await servicio.GuardarAsync(Datos(Sup(libro: "2", folio: "1", partida: "1"), nombres: "Libro dos"));

        var todas = await new ServicioSupletorias(ctx, bd.Reloj).BuscarAsync(new FiltroSupletorias(), 1);
        var libro1 = await new ServicioSupletorias(ctx, bd.Reloj).BuscarAsync(new FiltroSupletorias(Libro: "1"), 1);
        var otroSacramento = await new ServicioSupletorias(ctx, bd.Reloj).BuscarAsync(new FiltroSupletorias(Sacramento: "bautismo"), 1);

        todas.Items.Select(f => (f.Libro, f.Folio)).Should().Equal(("1", "9"), ("1", "10"), ("2", "1"));
        libro1.Total.Should().Be(2);
        otroSacramento.Total.Should().Be(0);
    }
}
