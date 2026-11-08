using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Pruebas.Infraestructura;

/// <summary>Datos sintéticos (CLAUDE.md regla 3). Una base nueva por prueba.</summary>
public class ServicioMatrimonioPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private static DatosContrayenteFormulario Esposo(
        string nombres = "Carlos Genaro", string apellidos = "Alvarado de León", string? edad = "29",
        string? tratamiento = "don") =>
        new(tratamiento, nombres, apellidos, edad, "San Gabriel", "P. San Bartolome", "Samuel Alvarado", "Estefana de León");

    private static DatosContrayenteFormulario Esposa(
        string nombres = "Gloria Inés", string apellidos = "Ceballos Tupul", string? edad = "22",
        string? tratamiento = "la Srita.") =>
        new(tratamiento, nombres, apellidos, edad, "San Gabriel", "parroquia San Bartolome", "Bonifacio Ceballos", "Concepcion Tupul");

    private static DatosMatrimonioFormulario Datos(
        string libro = "18", string folio = "5", string partida = "15",
        DatosContrayenteFormulario? esposo = null, DatosContrayenteFormulario? esposa = null,
        string fecha = "21/04/2007", string? lugar = null, string?[]? testigos = null,
        string? ministro = null, string? notas = null, bool revisar = false, string? motivo = null) =>
        new(libro, folio, partida, esposo ?? Esposo(), esposa ?? Esposa(), fecha, lugar,
            testigos ?? ["Noe Lopez Garcia", "Alma Luna de Lopez"], ministro, notas, revisar, motivo);

    private static ServicioMatrimonio Servicio(ParroquiaDbContext ctx, BaseTemporal bd) => new(ctx, bd.Reloj);

    private static async Task<long> CrearAsync(ServicioMatrimonio s, DatosMatrimonioFormulario d)
    {
        var r = await s.GuardarAsync(d);
        r.Estado.Should().Be(EstadoGuardado.Ok);
        return r.Id;
    }

    [Fact]
    public async Task Alta_guarda_todo_calcula_derivadas_y_audita_campo_por_campo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var id = await CrearAsync(Servicio(ctx, bd), Datos(
            esposo: Esposo(nombres: "  José   Ángel ", apellidos: "de la Peña"), notas: " Se anuló en 2010 "));

        await using var lectura = bd.Crear();
        var m = await lectura.Matrimonios.SingleAsync(x => x.Id == id);
        m.Esposo.Nombres.Should().Be("José Ángel");
        m.Esposo.Apellidos.Should().Be("de la Peña");
        m.Esposo.Edad.Should().Be(29);
        m.Esposo.Tratamiento.Should().Be("don");
        m.Esposa.Nombres.Should().Be("Gloria Inés");
        m.FechaMatrimonio.ATexto().Should().Be("21 de abril de 2007");
        m.LugarCelebracion.Should().Be(Matrimonio.LugarPorDefecto);
        m.Testigos.Should().Equal("Noe Lopez Garcia", "Alma Luna de Lopez");
        m.NotasMarginales.Should().Be("Se anuló en 2010");
        m.BusquedaEsposo.Should().Be("jose angel pena");
        m.BusquedaEsposa.Should().Be("gloria ines ceballos tupul");
        m.BusquedaNormalizada.Should().Be("jose angel pena gloria ines ceballos tupul");

        var auditoria = await lectura.Auditorias.SingleAsync(a => a.Entidad == nameof(Matrimonio) && a.EntidadId == id);
        var cambios = JsonDocument.Parse(auditoria.CambiosJson).RootElement;
        cambios.GetProperty("Esposo.Nombres").GetProperty("nuevo").GetString().Should().Be("José Ángel");
        cambios.GetProperty("Esposo.Edad").GetProperty("nuevo").GetInt16().Should().Be(29);
        cambios.GetProperty("Esposa.Apellidos").GetProperty("nuevo").GetString().Should().Be("Ceballos Tupul");
        cambios.GetProperty("Testigos").GetProperty("nuevo").GetString().Should().Be("Noe Lopez Garcia; Alma Luna de Lopez");
        cambios.EnumerateObject().Select(p => p.Name).Should()
            .Contain(["Libro", "Folio", "Partida", "FechaMatrimonio"])
            .And.NotContain(["BusquedaNormalizada", "BusquedaEsposo", "BusquedaEsposa", "LibroOrden"]);
    }

    [Fact]
    public async Task Editar_un_solo_campo_del_contrayente_audita_solo_ese_campo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var id = await CrearAsync(s, Datos());
        var version = (await s.ObtenerVistaAsync(id))!.Version;

        // Mismos datos: ni historial nuevo ni error.
        (await s.GuardarAsync(Datos(), id, version)).Estado.Should().Be(EstadoGuardado.Ok);
        version = (await s.ObtenerVistaAsync(id))!.Version;
        (await s.GuardarAsync(Datos(esposa: Esposa(edad: "23")), id, version)).Estado.Should().Be(EstadoGuardado.Ok);

        await using var lectura = bd.Crear();
        var historial = await lectura.Auditorias.Where(a => a.EntidadId == id && a.Entidad == nameof(Matrimonio))
            .OrderBy(a => a.Id).ToListAsync();
        historial.Select(a => a.Accion).Should().Equal(AccionAuditoria.Alta, AccionAuditoria.Modificacion);
        var cambios = JsonDocument.Parse(historial[1].CambiosJson).RootElement;
        cambios.EnumerateObject().Select(p => p.Name).Should().Equal("Esposa.Edad");
        cambios.GetProperty("Esposa.Edad").GetProperty("anterior").GetInt16().Should().Be(22);
        cambios.GetProperty("Esposa.Edad").GetProperty("nuevo").GetInt16().Should().Be(23);
    }

    [Fact]
    public async Task Cambiar_el_nombre_recalcula_la_busqueda_del_contrayente_sin_ensuciar_el_historial()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var id = await CrearAsync(s, Datos());
        var version = (await s.ObtenerVistaAsync(id))!.Version;

        (await s.GuardarAsync(Datos(esposa: Esposa(apellidos: "Nueva Apellida")), id, version)).Estado.Should().Be(EstadoGuardado.Ok);

        await using var lectura = bd.Crear();
        var m = await lectura.Matrimonios.SingleAsync(x => x.Id == id);
        m.BusquedaEsposa.Should().Be("gloria ines nueva apellida");
        var ultimo = await lectura.Auditorias.Where(a => a.EntidadId == id && a.Entidad == nameof(Matrimonio))
            .OrderByDescending(a => a.Id).FirstAsync();
        JsonDocument.Parse(ultimo.CambiosJson).RootElement.EnumerateObject().Select(p => p.Name).Should()
            .Equal("Esposa.Apellidos");
    }

    [Fact]
    public async Task Libro_folio_partida_y_nombres_de_ambos_son_obligatorios()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).GuardarAsync(Datos(
            libro: " ", folio: "", partida: "", esposo: Esposo(nombres: "", apellidos: ""), esposa: Esposa(nombres: "", apellidos: " ")));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Select(e => e.Campo).Should().BeEquivalentTo(
            ["Libro", "Folio", "Partida", "Esposo.Nombres", "Esposo.Apellidos", "Esposa.Nombres", "Esposa.Apellidos"]);
        (await ctx.Matrimonios.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Revisar_con_motivo_admite_nombres_vacios()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var ilegible = Esposa(nombres: "", apellidos: "");

        (await s.GuardarAsync(Datos(esposa: ilegible, revisar: true))).Errores!
            .Should().ContainSingle(e => e.Campo == "MotivoRevision");
        (await s.GuardarAsync(Datos(esposa: ilegible, revisar: true, motivo: "Tinta borrosa")))
            .Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Theory]
    [InlineData("11")]
    [InlineData("121")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("29.5")]
    public async Task Edad_fuera_de_rango_o_no_numerica_se_rechaza(string edad)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).GuardarAsync(Datos(esposo: Esposo(edad: edad)));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Single(e => e.Campo == "Esposo.Edad").Mensaje.Should().Contain("entre 12 y 120");
    }

    [Theory]
    [InlineData("12")]
    [InlineData("120")]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Edad_en_el_limite_o_vacia_es_valida(string? edad)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        (await Servicio(ctx, bd).GuardarAsync(Datos(esposa: Esposa(edad: edad)))).Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Theory]
    [InlineData("", "año")]
    [InlineData("31/2/2007", "día no es válido")]
    [InlineData("hoy", "no es válida")]
    public async Task Fecha_obligatoria_con_al_menos_el_anio_y_valida(string fecha, string fragmento)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx, bd).GuardarAsync(Datos(fecha: fecha));

        r.Estado.Should().Be(EstadoGuardado.Invalido);
        r.Errores!.Single(e => e.Campo == "FechaMatrimonio").Mensaje.Should().Contain(fragmento);
    }

    [Theory]
    [InlineData("2007")]
    [InlineData("4/2007")]
    public async Task Acepta_fechas_parciales(string fecha)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        (await Servicio(ctx, bd).GuardarAsync(Datos(fecha: fecha))).Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Fact]
    public async Task Testigos_se_limpian_conservan_el_orden_y_son_opcionales_y_el_lugar_vacio_usa_el_de_la_parroquia()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);

        var id = await CrearAsync(s, Datos(partida: "1", testigos: ["  Noe   Lopez ", null, " ", "Alma Luna"], lugar: " "));
        var ninguno = await CrearAsync(s, Datos(partida: "2", esposo: Esposo("Otro", "Esposo"), testigos: [], lugar: "Capilla del Rosario"));

        var vista = (await s.ObtenerVistaAsync(id))!;
        vista.Testigos.Should().Equal("Noe Lopez", "Alma Luna");
        vista.LugarCelebracion.Should().Be(Matrimonio.LugarPorDefecto);
        var vacia = (await s.ObtenerVistaAsync(ninguno))!;
        vacia.Testigos.Should().BeEmpty();
        vacia.LugarCelebracion.Should().Be("Capilla del Rosario");
    }

    [Fact]
    public async Task Testigos_limita_cantidad_y_largo()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);

        var muchos = await s.GuardarAsync(Datos(testigos: [.. Enumerable.Range(1, ServicioMatrimonio.MaxTestigos + 1).Select(i => $"Testigo {i}")]));
        var largo = await s.GuardarAsync(Datos(testigos: [new string('x', 151)]));

        muchos.Errores!.Should().ContainSingle(e => e.Campo == "Testigos");
        largo.Errores!.Should().ContainSingle(e => e.Campo == "Testigos");
    }

    [Fact]
    public async Task Ministro_del_catalogo_se_enlaza_y_otro_nombre_queda_como_texto()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        ctx.Ministros.Add(new Ministro { Nombre = "P. Andrés Martínez" });
        await ctx.SaveChangesAsync();
        var s = Servicio(ctx, bd);

        var a = await CrearAsync(s, Datos(partida: "1", ministro: "p. andres martinez"));
        var b = await CrearAsync(s, Datos(partida: "2", esposo: Esposo("Otro", "Esposo"), ministro: "P. Invitado"));

        (await s.ObtenerVistaAsync(a))!.Ministro.Should().Be("P. Andrés Martínez");
        (await s.ObtenerVistaAsync(b))!.Ministro.Should().Be("P. Invitado");
    }

    [Fact]
    public async Task Mismo_libro_folio_y_partida_advierte_sin_guardar_y_se_puede_confirmar()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var original = await CrearAsync(s, Datos());

        var otro = Datos(esposo: Esposo("Uno", "Primero"), esposa: Esposa("Dos", "Segunda"), fecha: "1/1/2005");
        var aviso = await s.GuardarAsync(otro);

        aviso.Estado.Should().Be(EstadoGuardado.Duplicados);
        aviso.Duplicados!.Should().ContainSingle(d => d.Id == original)
            .Which.Nombre.Should().Be("Carlos Genaro Alvarado de León y Gloria Inés Ceballos Tupul");
        (await ctx.Matrimonios.CountAsync()).Should().Be(1);
        (await s.GuardarAsync(otro, confirmarDuplicado: true)).Estado.Should().Be(EstadoGuardado.Ok);
    }

    [Fact]
    public async Task Mismos_contrayentes_y_fecha_advierte_pero_otra_fecha_o_solo_uno_igual_no()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await CrearAsync(s, Datos(libro: "1", folio: "1", partida: "1"));

        var igual = await s.GuardarAsync(Datos(libro: "9", folio: "9", partida: "9",
            esposo: Esposo("carlos genaro", "ALVARADO de leon"), esposa: Esposa("gloria ines", "ceballos TUPUL")));
        var otraFecha = await s.GuardarAsync(Datos(libro: "9", folio: "9", partida: "9", fecha: "22/04/2007"));
        var otraEsposa = await s.GuardarAsync(Datos(libro: "8", folio: "8", partida: "8", esposa: Esposa("Otra", "Persona")));

        igual.Estado.Should().Be(EstadoGuardado.Duplicados);
        otraFecha.Estado.Should().Be(EstadoGuardado.Ok);
        otraEsposa.Estado.Should().Be(EstadoGuardado.Ok);
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

        (await b.GuardarAsync(Datos(esposo: Esposo(apellidos: "De B")), id, versionVista)).Estado.Should().Be(EstadoGuardado.Ok);
        (await a.GuardarAsync(Datos(esposo: Esposo(apellidos: "De A")), id, versionVista)).Estado.Should().Be(EstadoGuardado.Conflicto);

        await using var lectura = bd.Crear();
        (await lectura.Matrimonios.SingleAsync(x => x.Id == id)).Esposo.Apellidos.Should().Be("De B");
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

        (await s.BuscarAsync(new FiltroMatrimonio(), 1)).Total.Should().Be(0);
        (await s.BuscarAsync(new FiltroMatrimonio(IncluirAnulados: true), 1)).Items.Should().ContainSingle(f => f.Anulado);
        (await s.ObtenerVistaAsync(id)).Should().BeNull();
        (await s.DatosParaConstanciaAsync(id)).Should().BeNull();
        (await s.GuardarAsync(Datos(), id)).Estado.Should().Be(EstadoGuardado.Anulado);

        (await s.RevertirAnulacionAsync(id)).Ok.Should().BeTrue();
        (await s.BuscarAsync(new FiltroMatrimonio(), 1)).Total.Should().Be(1);

        await using var lectura = bd.Crear();
        (await lectura.Auditorias.Where(a => a.EntidadId == id && a.Entidad == nameof(Matrimonio))
            .OrderBy(a => a.Id).Select(a => a.Accion).ToListAsync())
            .Should().Equal(AccionAuditoria.Alta, AccionAuditoria.Anulacion, AccionAuditoria.RevertirAnulacion);
    }

    private static async Task SembrarAsync(ServicioMatrimonio s)
    {
        await CrearAsync(s, Datos(libro: "3", folio: "10", partida: "1", fecha: "1999",
            esposo: Esposo("Juan Carlos", "Hidalgo Arévalo"), esposa: Esposa("María José", "de la Cruz")));
        await CrearAsync(s, Datos(libro: "3", folio: "2 bis", partida: "1", fecha: "5/2004",
            esposo: Esposo("Pedro", "Cruz Mena"), esposa: Esposa("Rosa", "Hidalgo Paz")));
        await CrearAsync(s, Datos(libro: "4", folio: "2", partida: "1", fecha: "2010", revisar: true, motivo: "dudoso",
            esposo: Esposo("Luis", "Otro"), esposa: Esposa("Ana", "Distinta")));
    }

    [Fact]
    public async Task Busqueda_encuentra_por_cualquiera_de_los_dos_y_no_mezcla_contrayentes()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await SembrarAsync(s);

        // Por el esposo y por la esposa (sin tildes ni mayúsculas, parcial).
        (await s.BuscarAsync(new FiltroMatrimonio("hidalgo arevalo"), 1)).Items.Single().EsposoNombres.Should().Be("Juan Carlos");
        (await s.BuscarAsync(new FiltroMatrimonio("maria jose cruz"), 1)).Items.Single().EsposaApellidos.Should().Be("de la Cruz");
        // "hidalgo" está en el esposo de un registro y en la esposa de otro: salen los dos.
        (await s.BuscarAsync(new FiltroMatrimonio("hidalgo"), 1)).Total.Should().Be(2);
        // "juan paz": juan es del esposo del 1.º y paz de la esposa del 2.º. Mezclar contrayentes no debe encontrar nada.
        (await s.BuscarAsync(new FiltroMatrimonio("juan paz"), 1)).Total.Should().Be(0);
        // Filtros comunes.
        (await s.BuscarAsync(new FiltroMatrimonio(Libro: "3"), 1)).Total.Should().Be(2);
        (await s.BuscarAsync(new FiltroMatrimonio(Folio: "2 BIS"), 1)).Items.Single().EsposoApellidos.Should().Be("Cruz Mena");
        (await s.BuscarAsync(new FiltroMatrimonio(AnioDesde: 2000, AnioHasta: 2005), 1)).Items.Single().EsposoNombres.Should().Be("Pedro");
        (await s.BuscarAsync(new FiltroMatrimonio(SoloRevisar: true), 1)).Items.Single().EsposoApellidos.Should().Be("Otro");
        // Orden por apellidos del esposo.
        (await s.BuscarAsync(new FiltroMatrimonio(), 1)).Items.Select(f => f.EsposoApellidos)
            .Should().Equal("Cruz Mena", "Hidalgo Arévalo", "Otro");
    }

    [Fact]
    public async Task Busqueda_global_devuelve_una_fila_por_contrayente_que_coincide()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        await SembrarAsync(s);
        var modulo = new ModuloMatrimonio(s);

        var soloEsposa = await modulo.BuscarAsync(new(Texto: "maria jose cruz"), 50);
        soloEsposa.Items.Should().ContainSingle().Which.Should().Match<Parroquia.Aplicacion.Sacramentos.ResultadoBusquedaRegistro>(
            r => r.Nombres == "María José" && r.Apellidos == "de la Cruz" && r.Clave == "matrimonio");
        soloEsposa.Total.Should().Be(1);

        // "hidalgo" aparece como esposo en un registro y como esposa en otro: dos filas, una por persona.
        var ambos = await modulo.BuscarAsync(new(Texto: "hidalgo"), 50);
        ambos.Items.Select(r => (r.Nombres, r.Apellidos)).Should()
            .BeEquivalentTo([("Juan Carlos", "Hidalgo Arévalo"), ("Rosa", "Hidalgo Paz")]);
        ambos.Total.Should().Be(2);

        // Un apellido común a los dos contrayentes del mismo registro aporta dos filas con el mismo registro.
        await CrearAsync(s, Datos(libro: "7", folio: "1", partida: "1", esposo: Esposo("Mario", "Solis"), esposa: Esposa("Elena", "Solis")));
        var mismo = await modulo.BuscarAsync(new(Texto: "solis"), 50);
        mismo.Items.Should().HaveCount(2);
        mismo.Items.Select(r => r.Id).Distinct().Should().HaveCount(1);
        mismo.Items.Select(r => r.Nombres).Should().BeEquivalentTo(["Mario", "Elena"]);
        mismo.Total.Should().Be(2);
    }

    [Fact]
    public async Task Busqueda_global_con_limite_indica_que_hay_mas_registros()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        for (var i = 1; i <= 3; i++)
            await CrearAsync(s, Datos(partida: i.ToString(), fecha: $"{2000 + i}", esposo: Esposo("Mario", "Solis"), esposa: Esposa("Elena", "Solis")));
        var modulo = new ModuloMatrimonio(s);

        var r = await modulo.BuscarAsync(new(Texto: "solis"), maximo: 2);

        r.Items.Should().HaveCount(4);   // 2 registros × 2 contrayentes
        r.Total.Should().Be(6);          // 3 registros × 2 contrayentes: queda más por ver
        r.Total.Should().BeGreaterThan(r.Items.Count);
    }

    [Fact]
    public async Task Datos_para_constancia_incluye_ambos_contrayentes_testigos_libro_folio_y_partida()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx, bd);
        var id = await CrearAsync(s, Datos(ministro: "P. Andrés Martínez"));

        var d = (await s.DatosParaConstanciaAsync(id))!;

        d.Esposo.Nombres.Should().Be("Carlos Genaro");
        d.Esposo.Tratamiento.Should().Be("don");
        d.Esposa.Edad.Should().Be(22);
        d.FechaMatrimonio.ATexto().Should().Be("21 de abril de 2007");
        d.Testigos.Should().Equal("Noe Lopez Garcia", "Alma Luna de Lopez");
        d.Ministro.Should().Be("P. Andrés Martínez");
        (d.Libro, d.Folio, d.Partida).Should().Be(("18", "5", "15"));
    }
}
