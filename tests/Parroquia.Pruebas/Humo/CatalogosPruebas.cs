using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class CatalogosPruebas(AppFixture app)
{
    public record Fila(long Id, string Valor, bool Activo);

    private async Task<List<Fila>> FilasAsync(string catalogo)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        return catalogo == "Ministros"
            ? await db.Ministros.AsNoTracking().Select(m => new Fila(m.Id, m.Nombre, m.Activo)).ToListAsync()
            : await db.Tratamientos.AsNoTracking().Select(t => new Fila(t.Id, t.Texto, t.Activo)).ToListAsync();
    }

    private async Task<List<Auditoria>> AuditoriaAsync(string catalogo, long id)
    {
        var entidad = catalogo == "Ministros" ? "Ministro" : "Tratamiento";
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        return await db.Auditorias.AsNoTracking()
            .Where(a => a.Entidad == entidad && a.EntidadId == id).OrderBy(a => a.Id).ToListAsync();
    }

    private static string Unico(string baseNombre) => $"{baseNombre} {Guid.NewGuid():N}"[..30];

    [Theory]
    [InlineData("Ministros")]
    [InlineData("Tratamientos")]
    public async Task Anonimo_va_al_login(string catalogo)
    {
        var r = await app.Cliente().GetAsync($"/Admin/{catalogo}");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Theory]
    [InlineData("Ministros", Roles.Capturista)]
    [InlineData("Ministros", Roles.Secretaria)]
    [InlineData("Tratamientos", Roles.Capturista)]
    [InlineData("Tratamientos", Roles.Secretaria)]
    public async Task Sin_permiso_de_catalogos_se_deniega_el_acceso(string catalogo, string rol)
    {
        var c = await app.ClienteConRolAsync(rol);

        var r = await c.GetAsync($"/Admin/{catalogo}");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Theory]
    [InlineData("Ministros")]
    [InlineData("Tratamientos")]
    public async Task Sin_permiso_no_se_puede_crear_por_post_directo(string catalogo)
    {
        var c = await app.ClienteConRolAsync(Roles.Secretaria);
        var nombre = Unico("Intruso");
        // El token se toma de una página pública con formulario (login) en otro cliente anónimo no sirve:
        // se usa el del propio cliente en una página permitida.
        var r = await AppFixture.PostFormAsync(c, "/Cuenta/Logout", $"/Admin/{catalogo}?handler=Crear",
            new() { ["valor"] = nombre });

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        (await FilasAsync(catalogo)).Should().NotContain(f => f.Valor == nombre);
    }

    [Theory]
    [InlineData("Ministros")]
    [InlineData("Tratamientos")]
    public async Task Admin_ve_la_pagina(string catalogo)
    {
        var c = await app.ClienteAdminAsync();

        var r = await c.GetAsync($"/Admin/{catalogo}");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain(catalogo);
    }

    [Theory]
    [InlineData("Ministros")]
    [InlineData("Tratamientos")]
    public async Task Admin_crea_un_item_y_queda_auditado_con_su_usuario(string catalogo)
    {
        var c = await app.ClienteAdminAsync();
        var nombre = Unico("P. Prueba");

        var r = await AppFixture.PostFormAsync(c, $"/Admin/{catalogo}", $"/Admin/{catalogo}?handler=Crear",
            new() { ["valor"] = nombre });

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var fila = (await FilasAsync(catalogo)).Single(f => f.Valor == nombre);
        fila.Activo.Should().BeTrue();

        var auditoria = await AuditoriaAsync(catalogo, fila.Id);
        auditoria.Should().ContainSingle();
        auditoria[0].Accion.Should().Be(AccionAuditoria.Alta);
        auditoria[0].UsuarioNombre.Should().Be(AppFixture.UsuarioAdmin);

        var html = await (await c.GetAsync($"/Admin/{catalogo}")).Content.ReadAsStringAsync();
        html.Should().Contain(System.Net.WebUtility.HtmlEncode(nombre));
    }

    [Theory]
    [InlineData("Ministros")]
    [InlineData("Tratamientos")]
    public async Task Duplicado_ignorando_tildes_y_mayusculas_se_rechaza_con_mensaje(string catalogo)
    {
        var c = await app.ClienteAdminAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        await AppFixture.PostFormAsync(c, $"/Admin/{catalogo}", $"/Admin/{catalogo}?handler=Crear",
            new() { ["valor"] = $"José Ñandú {sufijo}" });

        var r = await AppFixture.PostFormAsync(c, $"/Admin/{catalogo}", $"/Admin/{catalogo}?handler=Crear",
            new() { ["valor"] = $"  JOSE   NANDU {sufijo} " });

        var html = await r.Content.ReadAsStringAsync();
        (r.StatusCode == HttpStatusCode.OK ? html : await (await c.GetAsync($"/Admin/{catalogo}")).Content.ReadAsStringAsync())
            .Should().Contain("Ya existe");
        (await FilasAsync(catalogo)).Count(f => f.Valor.EndsWith(sufijo)).Should().Be(1);
    }

    [Theory]
    [InlineData("Ministros")]
    [InlineData("Tratamientos")]
    public async Task Valor_vacio_se_rechaza(string catalogo)
    {
        var c = await app.ClienteAdminAsync();
        var antes = (await FilasAsync(catalogo)).Count;

        await AppFixture.PostFormAsync(c, $"/Admin/{catalogo}", $"/Admin/{catalogo}?handler=Crear",
            new() { ["valor"] = "   " });

        (await FilasAsync(catalogo)).Count.Should().Be(antes);
    }

    [Theory]
    [InlineData("Ministros")]
    [InlineData("Tratamientos")]
    public async Task Editar_y_desactivar_quedan_en_la_auditoria_y_nunca_se_borra(string catalogo)
    {
        var c = await app.ClienteAdminAsync();
        var original = Unico("Original");
        var nuevo = Unico("Renombrado");
        await AppFixture.PostFormAsync(c, $"/Admin/{catalogo}", $"/Admin/{catalogo}?handler=Crear",
            new() { ["valor"] = original });
        var id = (await FilasAsync(catalogo)).Single(f => f.Valor == original).Id;

        await AppFixture.PostFormAsync(c, $"/Admin/{catalogo}", $"/Admin/{catalogo}?handler=Editar",
            new() { ["id"] = id.ToString(), ["valor"] = nuevo });
        await AppFixture.PostFormAsync(c, $"/Admin/{catalogo}", $"/Admin/{catalogo}?handler=Activo",
            new() { ["id"] = id.ToString(), ["activo"] = "false" });

        var fila = (await FilasAsync(catalogo)).Single(f => f.Id == id);
        fila.Valor.Should().Be(nuevo);
        fila.Activo.Should().BeFalse();

        var auditoria = await AuditoriaAsync(catalogo, id);
        auditoria.Select(a => a.Accion).Should().Equal(
            AccionAuditoria.Alta, AccionAuditoria.Modificacion, AccionAuditoria.Modificacion);
        auditoria[2].CambiosJson.Should().Contain("Activo");

        await AppFixture.PostFormAsync(c, $"/Admin/{catalogo}", $"/Admin/{catalogo}?handler=Activo",
            new() { ["id"] = id.ToString(), ["activo"] = "true" });
        (await FilasAsync(catalogo)).Single(f => f.Id == id).Activo.Should().BeTrue();
    }

    [Fact]
    public async Task Tratamientos_por_defecto_estan_sembrados()
    {
        var valores = (await FilasAsync("Tratamientos")).Select(f => f.Valor);

        valores.Should().Contain(["don", "la Srita."]);
    }

    [Fact]
    public async Task Ministros_no_se_siembran()
    {
        // Pendiente de docs/06: la lista de sacerdotes la define la parroquia.
        // Otras pruebas crean ministros; aquí solo se verifica que ninguno venga del sembrador.
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        var deSistema = await db.Auditorias.CountAsync(a => a.Entidad == "Ministro" && a.UsuarioNombre == "sistema");

        deSistema.Should().Be(0);
    }
}
