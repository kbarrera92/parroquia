using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Vinculos;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;
using Parroquia.Infraestructura.Vinculos;

namespace Parroquia.Pruebas.Infraestructura;

/// <summary>Datos sintéticos (CLAUDE.md regla 3). Una base nueva por prueba.</summary>
public class ServicioVinculosPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    private sealed record Entorno(
        ParroquiaDbContext Ctx, ServicioComunion Comunion, ServicioConfirmacion Confirmacion,
        ServicioMatrimonio Matrimonio, ServicioVinculos Vinculos);

    private async Task<(Entorno E, BaseTemporal Bd)> NuevoAsync()
    {
        var bd = await servidor.NuevaBaseAsync();
        var ctx = bd.Crear();
        var comunion = new ServicioComunion(ctx, bd.Reloj);
        var confirmacion = new ServicioConfirmacion(ctx, bd.Reloj);
        var matrimonio = new ServicioMatrimonio(ctx, bd.Reloj);
        IModuloSacramental[] modulos =
            [new ModuloComunion(comunion), new ModuloConfirmacion(confirmacion), new ModuloMatrimonio(matrimonio)];
        return (new Entorno(ctx, comunion, confirmacion, matrimonio, new ServicioVinculos(ctx, modulos)), bd);
    }

    private static async Task<ExtremoVinculo> ComunionAsync(Entorno e, string nombres = "Ana", string apellidos = "Prueba")
    {
        var r = await e.Comunion.GuardarAsync(
            new DatosComunionFormulario("1", "1", Guid.NewGuid().ToString("N")[..8], nombres, apellidos, "2004", null, null, false, null),
            confirmarDuplicado: true);
        r.Estado.Should().Be(EstadoGuardado.Ok);
        return new ExtremoVinculo(ModuloComunion.ClaveModulo, r.Id);
    }

    private static async Task<ExtremoVinculo> ConfirmacionAsync(Entorno e, string nombres = "Ana", string apellidos = "Prueba")
    {
        var r = await e.Confirmacion.GuardarAsync(
            new DatosConfirmacionFormulario("1", "1", Guid.NewGuid().ToString("N")[..8], nombres, apellidos, "2005", null, null, null, false, null),
            confirmarDuplicado: true);
        r.Estado.Should().Be(EstadoGuardado.Ok);
        return new ExtremoVinculo(ModuloConfirmacion.ClaveModulo, r.Id);
    }

    private static async Task<long> MatrimonioAsync(Entorno e)
    {
        var r = await e.Matrimonio.GuardarAsync(new DatosMatrimonioFormulario(
            "1", "1", Guid.NewGuid().ToString("N")[..8],
            new("don", "Carlos", "Esposo Prueba", "29", null, null, null, null),
            new("la Srita.", "Gloria", "Esposa Prueba", "22", null, null, null, null),
            "2007", null, null, null, null, false, null), confirmarDuplicado: true);
        r.Estado.Should().Be(EstadoGuardado.Ok);
        return r.Id;
    }

    [Fact]
    public async Task Vincular_es_simetrico_y_guarda_el_par_en_orden_canonico()
    {
        var (e, bd) = await NuevoAsync();
        var a = await ComunionAsync(e);
        var b = await ConfirmacionAsync(e);

        (await e.Vinculos.VincularAsync(b, a)).Ok.Should().BeTrue();

        await using var lectura = bd.Crear();
        var v = await lectura.Vinculos.SingleAsync();
        v.Origen.Should().Be(a); // "comunion" < "confirmacion"
        v.Destino.Should().Be(b);
        (await e.Vinculos.CadenaAsync(a)).Should().ContainSingle().Which.Extremo.Should().Be(b);
        (await e.Vinculos.CadenaAsync(b)).Should().ContainSingle().Which.Extremo.Should().Be(a);
    }

    [Fact]
    public async Task Rechaza_consigo_mismo_duplicado_y_registro_inexistente()
    {
        var (e, _) = await NuevoAsync();
        var a = await ComunionAsync(e);
        var b = await ConfirmacionAsync(e);

        (await e.Vinculos.VincularAsync(a, a)).Ok.Should().BeFalse();
        (await e.Vinculos.VincularAsync(a, b)).Ok.Should().BeTrue();
        (await e.Vinculos.VincularAsync(b, a)).Ok.Should().BeFalse();
        (await e.Vinculos.VincularAsync(a, new ExtremoVinculo("comunion", 999_999))).Ok.Should().BeFalse();
        (await e.Vinculos.VincularAsync(a, new ExtremoVinculo("noexiste", 1))).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Cadena_incluye_los_indirectos_con_por_medio_de()
    {
        var (e, _) = await NuevoAsync();
        var a = await ComunionAsync(e, "Ana", "Cadena");
        var b = await ConfirmacionAsync(e, "Ana", "Cadena");
        var c = await ComunionAsync(e, "Ana", "Cadena");
        (await e.Vinculos.VincularAsync(a, b)).Ok.Should().BeTrue();
        (await e.Vinculos.VincularAsync(b, c)).Ok.Should().BeTrue();

        var desdeA = await e.Vinculos.CadenaAsync(a);

        desdeA.Should().HaveCount(2);
        desdeA.Single(p => p.Extremo == b).Directo.Should().BeTrue();
        var indirecto = desdeA.Single(p => p.Extremo == c);
        indirecto.Directo.Should().BeFalse();
        indirecto.VinculoId.Should().BeNull();
        indirecto.PorMedioDe.Should().Contain("Cadena");

        // Un tercer vínculo que cierra el ciclo es rechazado.
        (await e.Vinculos.VincularAsync(a, c)).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Matrimonio_distingue_esposo_de_esposa()
    {
        var (e, _) = await NuevoAsync();
        var m = await MatrimonioAsync(e);
        var esposo = new ExtremoVinculo("matrimonio", m, "esposo");
        var esposa = new ExtremoVinculo("matrimonio", m, "esposa");
        var comunionDeEl = await ComunionAsync(e, "Carlos", "Esposo Prueba");

        (await e.Vinculos.VincularAsync(comunionDeEl, esposo)).Ok.Should().BeTrue();
        (await e.Vinculos.VincularAsync(comunionDeEl, new ExtremoVinculo("matrimonio", m, "otro"))).Ok.Should().BeFalse();

        var desdeEsposa = await e.Vinculos.CadenaAsync(esposa);
        desdeEsposa.Should().BeEmpty(); // la esposa no es la misma persona que el esposo

        var desdeComunion = await e.Vinculos.CadenaAsync(comunionDeEl);
        desdeComunion.Should().ContainSingle().Which.Resumen.Nombres.Should().Be("Carlos");
    }

    [Fact]
    public async Task Desvincular_anula_audita_y_revincular_reactiva_la_misma_fila()
    {
        var (e, bd) = await NuevoAsync();
        var a = await ComunionAsync(e);
        var b = await ConfirmacionAsync(e);
        await e.Vinculos.VincularAsync(a, b);
        var vinculoId = (await e.Vinculos.CadenaAsync(a)).Single().VinculoId!.Value;

        (await e.Vinculos.DesvincularAsync(vinculoId)).Ok.Should().BeTrue();

        await using (var lectura = bd.Crear())
        {
            (await lectura.Vinculos.AnyAsync()).Should().BeFalse(); // filtro global
            var fila = await lectura.Vinculos.IgnoreQueryFilters().SingleAsync();
            fila.Anulado.Should().BeTrue();
            fila.MotivoAnulacion.Should().Be("Desvinculado");
            (await lectura.Auditorias.AnyAsync(x => x.Entidad == nameof(Vinculo) && x.EntidadId == vinculoId
                && x.Accion == AccionAuditoria.Anulacion)).Should().BeTrue();
        }
        (await e.Vinculos.CadenaAsync(a)).Should().BeEmpty();

        (await e.Vinculos.VincularAsync(a, b)).Ok.Should().BeTrue();

        await using var final = bd.Crear();
        var unica = await final.Vinculos.IgnoreQueryFilters().SingleAsync();
        unica.Id.Should().Be(vinculoId);
        unica.Anulado.Should().BeFalse();
        (await final.Auditorias.AnyAsync(x => x.Entidad == nameof(Vinculo) && x.EntidadId == vinculoId
            && x.Accion == AccionAuditoria.RevertirAnulacion)).Should().BeTrue();
    }

    [Fact]
    public async Task Registro_anulado_no_aparece_en_la_cadena_ni_se_puede_vincular()
    {
        var (e, _) = await NuevoAsync();
        var a = await ComunionAsync(e);
        var b = await ConfirmacionAsync(e);
        await e.Vinculos.VincularAsync(a, b);

        (await e.Confirmacion.AnularAsync(b.Id, "Duplicado")).Ok.Should().BeTrue();

        (await e.Vinculos.CadenaAsync(a)).Should().BeEmpty();
        var c = await ComunionAsync(e);
        (await e.Vinculos.VincularAsync(c, b)).Ok.Should().BeFalse();
    }
}
