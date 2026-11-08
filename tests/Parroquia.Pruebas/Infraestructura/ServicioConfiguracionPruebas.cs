using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Configuracion;
using Parroquia.Infraestructura.Configuracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Infraestructura;

public class ServicioConfiguracionPruebas(ServidorPostgresFixture servidor) : IClassFixture<ServidorPostgresFixture>
{
    // PNG mínimo válido de 1x1 píxel.
    public static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00];

    private static DatosConfiguracion Datos(string nombre = "Parroquia de Prueba", string telefono = "(502) 0000-0000") =>
        new(nombre, "Calle de prueba 1", "Municipio de Prueba", telefono, "Pruebas, Dep.", "Pruebas");

    private static ServicioConfiguracionParroquia Servicio(ParroquiaDbContext ctx) => new(ctx);

    [Fact]
    public async Task Sin_fila_obtener_devuelve_null_y_guardar_la_crea_una_sola_vez()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx);
        (await s.ObtenerAsync()).Should().BeNull();

        (await s.GuardarAsync(Datos(), null, false)).Ok.Should().BeTrue();
        (await s.GuardarAsync(Datos("Otro nombre"), null, false)).Ok.Should().BeTrue();

        (await ctx.Configuraciones.CountAsync()).Should().Be(1);
        (await s.ObtenerAsync())!.Nombre.Should().Be("Otro nombre");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Nombre_obligatorio(string? nombre)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx).GuardarAsync(Datos() with { Nombre = nombre }, null, false);

        r.Ok.Should().BeFalse();
        r.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Limpia_espacios_de_los_textos()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx);

        await s.GuardarAsync(Datos("  Parroquia   de   Prueba "), null, false);

        (await s.ObtenerAsync())!.Nombre.Should().Be("Parroquia de Prueba");
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public async Task Acepta_logo_png_y_jpeg_validos(string tipo)
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var contenido = tipo == "image/png" ? Png1x1 : Jpeg;

        var r = await Servicio(ctx).GuardarAsync(Datos(), new ArchivoLogo(contenido, tipo), false);

        r.Ok.Should().BeTrue(r.Error);
        await using var lectura = bd.Crear();
        var c = (await Servicio(lectura).ObtenerAsync())!;
        c.Logo.Should().Equal(contenido);
        c.LogoTipoContenido.Should().Be(tipo);
    }

    [Fact]
    public async Task Rechaza_logo_de_tipo_no_permitido()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx).GuardarAsync(Datos(), new ArchivoLogo(Png1x1, "image/gif"), false);

        r.Ok.Should().BeFalse();
        (await Servicio(ctx).ObtenerAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Rechaza_logo_cuyo_contenido_no_es_la_imagen_declarada()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();

        var r = await Servicio(ctx).GuardarAsync(Datos(),
            new ArchivoLogo("esto no es una imagen"u8.ToArray(), "image/png"), false);

        r.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Rechaza_logo_de_mas_de_1_MB_y_acepta_exactamente_1_MB()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx);

        byte[] Grande(int n) { var b = new byte[n]; Png1x1.CopyTo(b, 0); return b; }

        (await s.GuardarAsync(Datos(), new ArchivoLogo(Grande(ServicioConfiguracionParroquia.LogoMaximoBytes + 1), "image/png"), false))
            .Ok.Should().BeFalse();
        (await s.GuardarAsync(Datos(), new ArchivoLogo(Grande(ServicioConfiguracionParroquia.LogoMaximoBytes), "image/png"), false))
            .Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Sin_archivo_nuevo_se_conserva_el_logo_y_quitarlo_lo_elimina()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx);
        await s.GuardarAsync(Datos(), new ArchivoLogo(Png1x1, "image/png"), false);

        await s.GuardarAsync(Datos("Nuevo nombre"), null, quitarLogo: false);
        (await s.ObtenerAsync())!.Logo.Should().Equal(Png1x1);

        await s.GuardarAsync(Datos("Nuevo nombre"), null, quitarLogo: true);
        var c = (await s.ObtenerAsync())!;
        c.Logo.Should().BeNull();
        c.LogoTipoContenido.Should().BeNull();
    }

    [Fact]
    public async Task Auditoria_registra_el_cambio_de_logo_sin_volcar_los_bytes()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx);
        await s.GuardarAsync(Datos(), null, false);
        var id = (await s.ObtenerAsync())!.Id;

        await s.GuardarAsync(Datos(), new ArchivoLogo(Png1x1, "image/png"), false);

        await using var lectura = bd.Crear();
        var cambio = await lectura.Auditorias
            .Where(a => a.Entidad == nameof(ConfiguracionParroquia) && a.EntidadId == id && a.Accion == AccionAuditoria.Modificacion)
            .SingleAsync();
        cambio.CambiosJson.Should().Contain("Logo");
        cambio.CambiosJson.Length.Should().BeLessThan(300);
        cambio.CambiosJson.Should().NotContain(Convert.ToBase64String(Png1x1));
    }

    [Fact]
    public async Task Guardar_los_mismos_datos_no_genera_auditoria_nueva()
    {
        var bd = await servidor.NuevaBaseAsync();
        await using var ctx = bd.Crear();
        var s = Servicio(ctx);
        await s.GuardarAsync(Datos(), new ArchivoLogo(Png1x1, "image/png"), false);
        var antes = await ctx.Auditorias.CountAsync();

        await s.GuardarAsync(Datos(), new ArchivoLogo(Png1x1, "image/png"), false); // mismo contenido

        (await ctx.Auditorias.CountAsync()).Should().Be(antes);
    }
}
