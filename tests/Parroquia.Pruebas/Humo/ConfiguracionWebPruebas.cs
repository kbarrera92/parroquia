using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Pruebas.Infraestructura;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class ConfiguracionWebPruebas(AppFixture app)
{
    private async Task<T> ConDbAsync<T>(Func<ParroquiaDbContext, Task<T>> f)
    {
        using var scope = app.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>());
    }

    private static MultipartFormDataContent Formulario(string token, string nombre, string telefono,
        byte[]? logo = null, string tipo = "image/png", bool quitar = false)
    {
        var f = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(nombre), "Datos.Nombre" },
            { new StringContent("Calle de prueba 1"), "Datos.Direccion" },
            { new StringContent("Mazatenango, Suchitepéquez Guatemala C.A."), "Datos.Municipio" },
            { new StringContent(telefono), "Datos.Telefono" },
            { new StringContent("Mazatenango, Such."), "Datos.TextoLugarEmision" },
            { new StringContent("Mazatenango"), "Datos.CiudadAbreviada" },
            { new StringContent(quitar ? "true" : "false"), "QuitarLogo" },
        };
        if (logo is not null)
        {
            var parte = new ByteArrayContent(logo);
            parte.Headers.ContentType = new MediaTypeHeaderValue(tipo);
            f.Add(parte, "ArchivoLogo", "logo.png");
        }
        return f;
    }

    private static async Task<HttpResponseMessage> GuardarAsync(HttpClient c, string nombre, string telefono,
        byte[]? logo = null, string tipo = "image/png", bool quitar = false)
    {
        var token = await AppFixture.TokenAsync(c, "/Admin/Configuracion");
        return await c.PostAsync("/Admin/Configuracion", Formulario(token, nombre, telefono, logo, tipo, quitar));
    }

    [Theory]
    [InlineData("/Admin/Configuracion")]
    [InlineData("/Admin/Parrocos")]
    public async Task Anonimo_va_al_login(string ruta)
    {
        var r = await app.Cliente().GetAsync(ruta);

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Theory]
    [InlineData("/Admin/Configuracion", Roles.Capturista)]
    [InlineData("/Admin/Configuracion", Roles.Secretaria)]
    [InlineData("/Admin/Parrocos", Roles.Capturista)]
    [InlineData("/Admin/Parrocos", Roles.Secretaria)]
    public async Task Sin_permiso_de_configuracion_se_deniega(string ruta, string rol)
    {
        var c = await app.ClienteConRolAsync(rol);

        var r = await c.GetAsync(ruta);

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Fact]
    public async Task La_configuracion_viene_sembrada_con_el_membrete()
    {
        var c = await app.ClienteAdminAsync();

        var html = await (await c.GetAsync("/Admin/Configuracion")).Content.ReadAsStringAsync();

        html.Should().Contain("San Bartolomé").And.Contain("7872-0051");
        (await ConDbAsync(db => db.Configuraciones.CountAsync())).Should().Be(1);
    }

    [Fact]
    public async Task Admin_guarda_cambios_y_quedan_auditados()
    {
        var c = await app.ClienteAdminAsync();
        var telefono = $"(502) {Random.Shared.Next(1000, 9999)}-{Random.Shared.Next(1000, 9999)}";

        var r = await GuardarAsync(c, "Catedral — Parroquia San Bartolomé", telefono);

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var conf = await ConDbAsync(db => db.Configuraciones.AsNoTracking().SingleAsync());
        conf.Telefono.Should().Be(telefono);
        var ultima = await ConDbAsync(db => db.Auditorias.AsNoTracking()
            .Where(a => a.Entidad == "ConfiguracionParroquia" && a.EntidadId == conf.Id)
            .OrderByDescending(a => a.Id).FirstAsync());
        ultima.Accion.Should().Be(AccionAuditoria.Modificacion);
        ultima.UsuarioNombre.Should().Be(AppFixture.UsuarioAdmin);
        ultima.CambiosJson.Should().Contain("Telefono").And.Contain(telefono);
    }

    [Fact]
    public async Task Nombre_vacio_no_se_guarda_y_muestra_el_error()
    {
        var c = await app.ClienteAdminAsync();
        var antes = await ConDbAsync(db => db.Configuraciones.AsNoTracking().SingleAsync());

        var r = await GuardarAsync(c, "", antes.Telefono ?? "");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var despues = await ConDbAsync(db => db.Configuraciones.AsNoTracking().SingleAsync());
        despues.Nombre.Should().Be(antes.Nombre);
    }

    [Fact]
    public async Task Logo_subido_se_sirve_igual_y_se_puede_quitar()
    {
        var c = await app.ClienteAdminAsync();
        var conf = await ConDbAsync(db => db.Configuraciones.AsNoTracking().SingleAsync());

        var subir = await GuardarAsync(c, conf.Nombre, conf.Telefono ?? "", ServicioConfiguracionPruebas.Png1x1);
        subir.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var logo = await c.GetAsync("/Admin/Configuracion?handler=Logo");
        logo.StatusCode.Should().Be(HttpStatusCode.OK);
        logo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await logo.Content.ReadAsByteArrayAsync()).Should().Equal(ServicioConfiguracionPruebas.Png1x1);

        await GuardarAsync(c, conf.Nombre, conf.Telefono ?? "", quitar: true);
        (await c.GetAsync("/Admin/Configuracion?handler=Logo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Archivo_que_no_es_imagen_se_rechaza_y_no_se_guarda()
    {
        var c = await app.ClienteAdminAsync();
        var conf = await ConDbAsync(db => db.Configuraciones.AsNoTracking().SingleAsync());
        await GuardarAsync(c, conf.Nombre, conf.Telefono ?? "", quitar: true); // sin logo

        var r = await GuardarAsync(c, conf.Nombre, conf.Telefono ?? "", "no soy imagen"u8.ToArray());

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/Admin/Configuracion?handler=Logo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task El_logo_solo_lo_ve_quien_tiene_permiso()
    {
        var c = await app.ClienteConRolAsync(Roles.Secretaria);

        var r = await c.GetAsync("/Admin/Configuracion?handler=Logo");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }
}
