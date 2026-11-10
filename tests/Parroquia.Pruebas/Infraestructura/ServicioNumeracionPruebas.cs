using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Numeracion;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Numeracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Infraestructura;

/// <summary>Datos sintéticos (CLAUDE.md regla 3). Una base nueva por prueba.</summary>
public class ServicioNumeracionPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private const string Clave = "comunion";

    private static DatosAperturaLibro Apertura(
        string numero = "81", int folio = 1, int partida = 1, int porFolio = 3, int folios = 2,
        ModoPartida modo = ModoPartida.Correlativa, int yaEnFolio = 0) =>
        new(Clave, numero, folio, partida, porFolio, folios, modo, yaEnFolio);

    private static async Task AbrirAsync(BaseTemporal bd, DatosAperturaLibro d)
    {
        await using var ctx = bd.Crear();
        (await new ServicioNumeracion(ctx, bd.Reloj).AbrirAsync(d)).Ok.Should().BeTrue();
    }

    /// <summary>Lo que hace un ServicioX al dar de alta un asiento nuevo: numerar y guardar en la misma transacción.</summary>
    private static async Task<NumeroAsignado?> AsentarAsync(ParroquiaDbContext ctx, BaseTemporal bd, string nombre)
    {
        await using var tx = await ctx.Database.BeginTransactionAsync();
        var c = new Comunion
        {
            Nombres = nombre,
            Apellidos = "Prueba",
            FechaComunion = FechaParcial.Crear(2026, 11, 10, new DateOnly(2026, 11, 10)),
        };
        var numero = await new ServicioNumeracion(ctx, bd.Reloj).AsignarAsync(Clave, c);
        if (numero is null) return null;

        c.ActualizarDerivados();
        ctx.Comuniones.Add(c);
        await ctx.SaveChangesAsync();
        await tx.CommitAsync();
        return numero;
    }

    [Fact]
    public async Task Entrega_libro_folio_y_partida_en_orden_y_los_deja_en_el_registro()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura());
        await using var ctx = bd.Crear();

        var numeros = new List<NumeroAsignado>();
        for (var i = 0; i < 4; i++) numeros.Add((await AsentarAsync(ctx, bd, $"Asiento {i}"))!);

        numeros.Select(n => (n.Libro, n.Folio, n.Partida)).Should().Equal(
            ("81", "1", "1"), ("81", "1", "2"), ("81", "1", "3"), ("81", "2", "4"));

        await using var lectura = bd.Crear();
        var guardados = await lectura.Comuniones.OrderBy(c => c.Id).ToListAsync();
        guardados.Select(c => (c.Libro, c.Folio, c.Partida)).Should().Equal(numeros.Select(n => (n.Libro, n.Folio, n.Partida)));
        guardados.Should().OnlyContain(c => c.LibroDigitalId == numeros[0].LibroDigitalId && c.EsAsientoDelSistema);
        guardados[3].PartidaOrden.Should().Be(ClaveOrdenNatural.De("4"));
    }

    [Fact]
    public async Task Dos_capturistas_a_la_vez_nunca_reciben_el_mismo_numero()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura(porFolio: 10, folios: 5)); // 50 asientos

        async Task<List<NumeroAsignado>> Capturista(string quien)
        {
            await using var ctx = bd.Crear();
            var propios = new List<NumeroAsignado>();
            for (var i = 0; i < 10; i++) propios.Add((await AsentarAsync(ctx, bd, $"{quien} {i}"))!);
            return propios;
        }

        var resultados = await Task.WhenAll(Capturista("A"), Capturista("B"), Capturista("C"));

        var partidas = resultados.SelectMany(r => r).Select(n => int.Parse(n.Partida)).Order().ToList();
        partidas.Should().Equal(Enumerable.Range(1, 30)); // sin repetidos y sin huecos
    }

    [Fact]
    public async Task Al_llenarse_cierra_el_libro_y_abre_el_siguiente_con_la_partida_correlativa()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura(porFolio: 2, folios: 1)); // libro de 2 asientos
        await using var ctx = bd.Crear();

        var a = await AsentarAsync(ctx, bd, "Uno");
        var b = await AsentarAsync(ctx, bd, "Dos");
        var c = await AsentarAsync(ctx, bd, "Tres");

        (b!.Libro, b.Folio, b.Partida).Should().Be(("81", "1", "2"));
        (c!.Libro, c.Folio, c.Partida).Should().Be(("82", "1", "3"));

        await using var lectura = bd.Crear();
        var libros = await lectura.LibrosDigitales.OrderBy(l => l.Id).ToListAsync();
        libros.Should().HaveCount(2);
        libros[0].Abierto.Should().BeFalse();
        libros[0].CerradoEn.Should().NotBeNull();
        libros[1].Abierto.Should().BeTrue();
        libros[1].Entregados.Should().Be(1);
    }

    [Fact]
    public async Task Sin_libro_abierto_no_numera_ni_guarda()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        (await AsentarAsync(ctx, bd, "Sin libro")).Should().BeNull();

        (await ctx.Comuniones.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Exige_una_transaccion_abierta()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura());
        await using var ctx = bd.Crear();

        var asignar = () => new ServicioNumeracion(ctx, bd.Reloj).AsignarAsync(Clave, new Comunion());

        await asignar.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Anular_un_asiento_no_libera_su_numero()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura());
        await using var ctx = bd.Crear();
        await AsentarAsync(ctx, bd, "Uno");
        await AsentarAsync(ctx, bd, "Error de captura");

        var anulado = await ctx.Comuniones.SingleAsync(c => c.Nombres == "Error de captura");
        anulado.Anulado = true;
        anulado.MotivoAnulacion = "Error de captura";
        await ctx.SaveChangesAsync();

        var siguiente = await AsentarAsync(ctx, bd, "Tres");

        siguiente!.Partida.Should().Be("3");
        var proximo = await new ServicioNumeracion(ctx, bd.Reloj).ProximoAsync(Clave);
        proximo!.Partida.Should().Be("4");
    }

    [Fact]
    public async Task Proximo_no_reserva_el_numero()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura(folio: 2, partida: 302, yaEnFolio: 1, folios: 3));
        await using var ctx = bd.Crear();
        var s = new ServicioNumeracion(ctx, bd.Reloj);

        var uno = await s.ProximoAsync(Clave);
        var dos = await s.ProximoAsync(Clave);

        uno.Should().Be(dos);
        (uno!.Libro, uno.Folio, uno.Partida, uno.Restantes).Should().Be(("81", "2", "302", 5));
        (await ctx.Comuniones.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task No_abre_dos_libros_a_la_vez_ni_repite_un_numero()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura());
        await using var ctx = bd.Crear();
        var s = new ServicioNumeracion(ctx, bd.Reloj);

        var otroAbierto = await s.AbrirAsync(Apertura("82"));
        otroAbierto.Ok.Should().BeFalse();
        otroAbierto.Error.Should().Contain("abierto");

        (await s.CerrarAsync(Clave)).Ok.Should().BeTrue();
        var repetido = await s.AbrirAsync(Apertura("81"));
        repetido.Ok.Should().BeFalse();
        repetido.Error.Should().Contain("81");

        (await s.AbrirAsync(Apertura("82"))).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Rechaza_configuracion_invalida_con_mensaje()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = new ServicioNumeracion(ctx, bd.Reloj);

        (await s.AbrirAsync(Apertura(numero: " "))).Ok.Should().BeFalse();
        (await s.AbrirAsync(Apertura(porFolio: 0))).Ok.Should().BeFalse();
        (await ctx.LibrosDigitales.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Detecta_si_un_libro_es_digital()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura());
        await using var ctx = bd.Crear();
        var s = new ServicioNumeracion(ctx, bd.Reloj);

        (await s.EsLibroDigitalAsync(Clave, " 81 ")).Should().BeTrue();
        (await s.EsLibroDigitalAsync(Clave, "80")).Should().BeFalse();
        (await s.EsLibroDigitalAsync("bautismo", "81")).Should().BeFalse();
    }

    [Fact]
    public async Task Audita_apertura_y_cierre_pero_no_cada_avance_del_contador()
    {
        var bd = await servidor.NuevaBaseAsync();
        await AbrirAsync(bd, Apertura());
        await using var ctx = bd.Crear();
        await AsentarAsync(ctx, bd, "Uno");
        await AsentarAsync(ctx, bd, "Dos");
        await new ServicioNumeracion(ctx, bd.Reloj).CerrarAsync(Clave);

        await using var lectura = bd.Crear();
        var libroId = (await lectura.LibrosDigitales.SingleAsync()).Id;
        var filas = await lectura.Auditorias
            .Where(a => a.Entidad == nameof(Parroquia.Dominio.Numeracion.LibroDigital) && a.EntidadId == libroId)
            .OrderBy(a => a.Id).ToListAsync();

        filas.Should().HaveCount(2); // alta + cierre; los dos asientos no ensucian el historial del libro
        filas[0].Accion.Should().Be(AccionAuditoria.Alta);
        var campos = JsonDocument.Parse(filas[0].CambiosJson).RootElement.EnumerateObject().Select(p => p.Name).ToList();
        campos.Should().Contain(["TipoSacramento", "Numero", "PartidasPorFolio"]);
        campos.Should().NotContain(["UltimaPartida", "Entregados", "FolioActual", "PartidasEnFolio"]);
    }

    [Fact]
    public async Task No_abre_un_libro_digital_con_el_numero_de_un_libro_transcrito()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var c = new Comunion
        {
            Libro = "81", Folio = "1", Partida = "1", Nombres = "Ana", Apellidos = "Prueba",
            FechaComunion = FechaParcial.Crear(2004, 10, 2, new DateOnly(2026, 11, 10)),
        };
        c.ActualizarDerivados();
        ctx.Comuniones.Add(c);
        await ctx.SaveChangesAsync();
        var s = new ServicioNumeracion(ctx, bd.Reloj);

        var r = await s.AbrirAsync(Apertura("81"));

        r.Ok.Should().BeFalse();
        r.Error.Should().Contain("transcritos");
        (await s.AbrirAsync(Apertura("82"))).Ok.Should().BeTrue();
    }
}
