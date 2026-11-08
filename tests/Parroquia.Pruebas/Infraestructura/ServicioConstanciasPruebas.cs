using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Configuracion;
using Parroquia.Infraestructura.Constancias;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Pruebas.Infraestructura;

/// <summary>Emisión de constancias con datos sintéticos. "Hoy" del reloj falso es el 7-nov-2026.</summary>
public class ServicioConstanciasPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private static readonly DateOnly Hoy = new(2026, 11, 7);

    private sealed record Entorno(
        BaseTemporal Bd, ParroquiaDbContext Ctx, ServicioConstancias Constancias, ServicioComunion Comunion, ServicioParroco Parroco);

    private async Task<Entorno> NuevoAsync(bool conParroco = true)
    {
        var bd = await servidor.NuevaBaseAsync();
        var ctx = bd.Crear();
        await SembradorConfiguracion.InicializarAsync(ctx);

        var parroco = new ServicioParroco(ctx, bd.Reloj);
        if (conParroco) (await parroco.RegistrarNuevoAsync("P. Neil Contreras", "Párroco", new DateOnly(2020, 1, 1))).Ok.Should().BeTrue();

        var comunion = new ServicioComunion(ctx, bd.Reloj);
        var constancias = new ServicioConstancias(
            ctx, new ServicioConfiguracionParroquia(ctx), parroco, [new ModuloComunion(comunion)],
            new GeneradorPdfConstancia(), bd.Usuario,
            new ServicioBitacoraAcceso(ctx, bd.Usuario, new HttpContextAccessor(), bd.Reloj), bd.Reloj);
        return new Entorno(bd, ctx, constancias, comunion, parroco);
    }

    private static async Task<long> CrearRegistroAsync(
        ServicioComunion s, string nombres = "Alejandra Maria", string apellidos = "Penagos Samayoa",
        string fecha = "2/10/2004", bool revisar = false, string? motivo = null)
    {
        var r = await s.GuardarAsync(new DatosComunionFormulario(
            "3", "60", Guid.NewGuid().ToString("N")[..6], nombres, apellidos, fecha, null, null, revisar, motivo));
        r.Estado.Should().Be(EstadoGuardado.Ok);
        return r.Id;
    }

    [Fact]
    public async Task Emitir_entrega_un_pdf_y_deja_la_constancia_y_la_bitacora()
    {
        var e = await NuevoAsync();
        var id = await CrearRegistroAsync(e.Comunion);

        var r = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id));

        r.Estado.Should().Be(EstadoConstancia.Ok);
        System.Text.Encoding.ASCII.GetString(r.Pdf!, 0, 5).Should().Be("%PDF-");
        r.NombreArchivo.Should().Be($"constancia-comunion-{id}.pdf");

        await using var lectura = e.Bd.Crear();
        var emitida = await lectura.ConstanciasEmitidas.SingleAsync();
        emitida.TipoSacramento.Should().Be("comunion");
        emitida.RegistroId.Should().Be(id);
        emitida.EmitidaPor.Should().Be("capturista1");
        emitida.UsuarioId.Should().Be("u-1");
        emitida.EmitidaEn.Should().Be(e.Bd.Reloj.Ahora);
        emitida.FechaImpresa.Should().Be(Hoy);
        emitida.FechaAjustadaPor.Should().BeNull();
        emitida.Firmante.Should().Be("P. Neil Contreras");
        emitida.ConAdvertencias.Should().BeFalse();
        emitida.SolicitudId.Should().BeNull();
        emitida.HashPdf.Should().Be(Convert.ToHexStringLower(SHA256.HashData(r.Pdf!)));

        var bitacora = await lectura.BitacoraAccesos.SingleAsync(b => b.Evento == EventoAcceso.ImprimirConstancia);
        bitacora.Entidad.Should().Be("Comunion");
        bitacora.EntidadId.Should().Be(id);
        bitacora.UsuarioNombre.Should().Be("capturista1");
    }

    [Fact]
    public async Task La_instantanea_guarda_el_texto_tal_como_se_imprimio()
    {
        var e = await NuevoAsync();
        var id = await CrearRegistroAsync(e.Comunion);
        await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id));

        await using var lectura = e.Bd.Crear();
        var json = (await lectura.ConstanciasEmitidas.SingleAsync()).InstantaneaJson;
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("Titulo").GetString().Should().Be("CERTIFICACIÓN DE COMUNIÓN");
        json.Should().Contain("ALEJANDRA MARIA PENAGOS SAMAYOA").And.Contain("2 de octubre de 2004")
            .And.Contain("7 de noviembre de 2026");
        doc.RootElement.GetProperty("TituloFirmante").GetString().Should().Be("Párroco.");
        json.Should().NotContain("Logo");
    }

    [Fact]
    public async Task Sin_parroco_vigente_no_se_puede_emitir_y_no_deja_rastro()
    {
        var e = await NuevoAsync(conParroco: false);
        var id = await CrearRegistroAsync(e.Comunion);

        var r = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id));

        r.Estado.Should().Be(EstadoConstancia.Invalida);
        r.Error.Should().Contain("párroco");
        r.Pdf.Should().BeNull();
        (await e.Ctx.ConstanciasEmitidas.CountAsync()).Should().Be(0);
        (await e.Ctx.BitacoraAccesos.CountAsync(b => b.Evento == EventoAcceso.ImprimirConstancia)).Should().Be(0);
    }

    [Fact]
    public async Task Registro_con_avisos_exige_confirmacion_y_queda_marcado()
    {
        var e = await NuevoAsync();
        var id = await CrearRegistroAsync(e.Comunion, fecha: "2004", revisar: true, motivo: "Fecha dudosa");

        var vista = await e.Constancias.PrepararAsync("comunion", id, null, false);
        vista.Vista!.Advertencias.Should().HaveCount(2); // fecha incompleta + Revisar

        var sin = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id));
        sin.Estado.Should().Be(EstadoConstancia.Invalida);
        (await e.Ctx.ConstanciasEmitidas.CountAsync()).Should().Be(0);

        var con = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id, ConfirmaAdvertencias: true));
        con.Estado.Should().Be(EstadoConstancia.Ok);
        (await e.Ctx.ConstanciasEmitidas.SingleAsync()).ConAdvertencias.Should().BeTrue();
    }

    [Fact]
    public async Task Solo_con_permiso_se_puede_cambiar_la_fecha_y_queda_quien_la_ajusto()
    {
        var e = await NuevoAsync();
        var id = await CrearRegistroAsync(e.Comunion);
        var ayer = Hoy.AddDays(-1);

        var sinPermiso = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id, ayer));
        sinPermiso.Estado.Should().Be(EstadoConstancia.Invalida);
        sinPermiso.Error.Should().Contain("Administrador");

        var mismaFechaSinPermiso = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id, Hoy));
        mismaFechaSinPermiso.Estado.Should().Be(EstadoConstancia.Ok);

        e.Bd.Usuario.Nombre = "admin1";
        var conPermiso = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id, ayer, PuedeCambiarFecha: true));
        conPermiso.Estado.Should().Be(EstadoConstancia.Ok);

        await using var lectura = e.Bd.Crear();
        var filas = await lectura.ConstanciasEmitidas.OrderBy(c => c.Id).ToListAsync();
        filas.Should().HaveCount(2);
        filas[0].FechaAjustadaPor.Should().BeNull();
        filas[1].FechaImpresa.Should().Be(ayer);
        filas[1].FechaAjustadaPor.Should().Be("admin1");
        filas[1].InstantaneaJson.Should().Contain("6 de noviembre de 2026");
        (await lectura.BitacoraAccesos.Where(b => b.Evento == EventoAcceso.ImprimirConstancia).Select(b => b.Detalle).ToListAsync())
            .Should().Contain(d => d!.Contains("fecha ajustada"));
    }

    [Fact]
    public async Task La_fecha_de_emision_no_puede_ser_futura_ni_absurda()
    {
        var e = await NuevoAsync();
        var id = await CrearRegistroAsync(e.Comunion);

        foreach (var fecha in new[] { Hoy.AddDays(1), new DateOnly(1800, 1, 1) })
        {
            var r = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id, fecha, PuedeCambiarFecha: true));
            r.Estado.Should().Be(EstadoConstancia.Invalida);
        }
        (await e.Ctx.ConstanciasEmitidas.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Cada_reimpresion_lleva_al_parroco_vigente_en_ese_momento_y_no_toca_las_anteriores()
    {
        var e = await NuevoAsync();
        var id = await CrearRegistroAsync(e.Comunion);
        await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id));

        (await e.Parroco.RegistrarNuevoAsync("P. Nuevo Párroco", "Párroco", new DateOnly(2026, 11, 5))).Ok.Should().BeTrue();
        e.Bd.Reloj.Ahora = e.Bd.Reloj.Ahora.AddMinutes(5);
        var segunda = await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id));

        segunda.Estado.Should().Be(EstadoConstancia.Ok);
        var emitidas = await e.Constancias.ListarEmitidasAsync("comunion", id);
        emitidas.Select(c => c.Firmante).Should().Equal("P. Nuevo Párroco", "P. Neil Contreras");
        (await e.Constancias.ContarEmitidasAsync("comunion", id)).Should().Be(2);
    }

    [Fact]
    public async Task Registro_anulado_o_inexistente_y_modulo_desconocido_no_existen_para_imprimir()
    {
        var e = await NuevoAsync();
        var id = await CrearRegistroAsync(e.Comunion);
        (await e.Comunion.AnularAsync(id, "Duplicado")).Ok.Should().BeTrue();

        (await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", id))).Estado.Should().Be(EstadoConstancia.NoExiste);
        (await e.Constancias.EmitirAsync(new SolicitudEmision("comunion", 999_999))).Estado.Should().Be(EstadoConstancia.NoExiste);
        (await e.Constancias.EmitirAsync(new SolicitudEmision("bautismo", id))).Estado.Should().Be(EstadoConstancia.NoExiste);
        (await e.Constancias.PrepararAsync("bautismo", id, null, false)).Estado.Should().Be(EstadoConstancia.NoExiste);
        (await e.Ctx.ConstanciasEmitidas.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Preparar_usa_la_configuracion_y_el_parroco_actuales_sin_guardar_nada()
    {
        var e = await NuevoAsync();
        var id = await CrearRegistroAsync(e.Comunion);

        var r = await e.Constancias.PrepararAsync("comunion", id, null, false);

        r.Estado.Should().Be(EstadoConstancia.Ok);
        var texto = r.Vista!.Documento.ATextoPlano();
        texto.Should().Contain("6a. Avenida y 7a. Calle zona 1 -:- Teléfono: (502) 7872-0051")
            .And.Contain("Mazatenango, 7 de noviembre de 2026").And.Contain("P. Neil Contreras");
        r.Vista.FechaEmision.Should().Be(Hoy);
        r.Vista.EmisionesPrevias.Should().Be(0);
        (await e.Ctx.ConstanciasEmitidas.CountAsync()).Should().Be(0);
        (await e.Ctx.BitacoraAccesos.CountAsync(b => b.Evento == EventoAcceso.ImprimirConstancia)).Should().Be(0);
    }
}
