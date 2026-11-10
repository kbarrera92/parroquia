using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Humo;

/// <summary>La base web se comparte: estas pruebas usan Matrimonio y lo dejan sin libro abierto al terminar.</summary>
[Collection(AppCollection.Nombre)]
public class LibrosDigitalesWebPruebas(AppFixture app)
{
    private const string Pagina = "/Admin/LibrosDigitales";

    [Fact]
    public async Task Admin_abre_y_cierra_un_libro_digital()
    {
        var c = await app.ClienteAdminAsync();
        await AppFixture.PostFormAsync(c, Pagina, $"{Pagina}?handler=Cerrar", new() { ["clave"] = "matrimonio" }); // por si quedó uno abierto
        var numero = $"T{Guid.NewGuid():N}"[..10];

        var abrir = await AppFixture.PostFormAsync(c, Pagina, $"{Pagina}?handler=Abrir", new()
        {
            ["clave"] = "matrimonio", ["numero"] = numero, ["folioInicial"] = "1", ["partidaInicial"] = "1",
            ["partidasPorFolio"] = "4", ["foliosPorLibro"] = "10", ["partidasYaEnFolio"] = "0",
        });
        abrir.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await (await c.GetAsync(Pagina)).Content.ReadAsStringAsync();
        try
        {
            html.Should().Contain(numero).And.Contain("Se abri"); // "ó" sale codificada en el HTML
        }
        finally
        {
            var cerrar = await AppFixture.PostFormAsync(c, Pagina, $"{Pagina}?handler=Cerrar", new() { ["clave"] = "matrimonio" });
            cerrar.StatusCode.Should().Be(HttpStatusCode.Redirect);
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        var libro = await db.LibrosDigitales.AsNoTracking().SingleAsync(l => l.Numero == numero);
        libro.Abierto.Should().BeFalse();
    }

    [Fact]
    public async Task Configuracion_invalida_muestra_el_error_y_no_abre_nada()
    {
        var c = await app.ClienteAdminAsync();
        await AppFixture.PostFormAsync(c, Pagina, $"{Pagina}?handler=Cerrar", new() { ["clave"] = "matrimonio" }); // por si quedó uno abierto
        var numero = $"X{Guid.NewGuid():N}"[..10];

        await AppFixture.PostFormAsync(c, Pagina, $"{Pagina}?handler=Abrir", new()
        {
            ["clave"] = "matrimonio", ["numero"] = numero, ["folioInicial"] = "1", ["partidaInicial"] = "1",
            ["partidasPorFolio"] = "0", ["foliosPorLibro"] = "10", ["partidasYaEnFolio"] = "0",
        });

        var html = await (await c.GetAsync(Pagina)).Content.ReadAsStringAsync();
        html.Should().Contain("al menos 1 partida");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        (await db.LibrosDigitales.AnyAsync(l => l.Numero == numero)).Should().BeFalse();
    }

    [Fact]
    public async Task Sacramento_desconocido_da_404()
    {
        var c = await app.ClienteAdminAsync();

        var r = await AppFixture.PostFormAsync(c, Pagina, $"{Pagina}?handler=Cerrar", new() { ["clave"] = "no-existe" });

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Capturista")]
    [InlineData("Secretaria")]
    public async Task Solo_el_administrador_entra(string rol)
    {
        var c = await app.ClienteConRolAsync(rol);

        var r = await c.GetAsync(Pagina);

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }
}
