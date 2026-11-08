using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Pruebas.Humo;

[Collection(AppCollection.Nombre)]
public class ParrocosWebPruebas(AppFixture app)
{
    [Fact]
    public async Task Admin_registra_un_parroco_nuevo_y_el_anterior_queda_cerrado()
    {
        var c = await app.ClienteAdminAsync();
        var nombre1 = $"P. Uno {Guid.NewGuid():N}"[..20];
        var nombre2 = $"P. Dos {Guid.NewGuid():N}"[..20];

        // Fechas que respetan "estrictamente posterior" aunque otras pruebas hayan registrado antes:
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-6));
        var r1 = await AppFixture.PostFormAsync(c, "/Admin/Parrocos", "/Admin/Parrocos?handler=Nuevo",
            new() { ["nombre"] = nombre1, ["titulo"] = "Párroco", ["desde"] = hoy.AddDays(-1).ToString("yyyy-MM-dd") });
        var r2 = await AppFixture.PostFormAsync(c, "/Admin/Parrocos", "/Admin/Parrocos?handler=Nuevo",
            new() { ["nombre"] = nombre2, ["titulo"] = "Párroco", ["desde"] = hoy.ToString("yyyy-MM-dd") });

        r1.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r2.StatusCode.Should().Be(HttpStatusCode.Redirect);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        var actuales = await db.Parrocos.AsNoTracking().Where(p => p.EsActual).ToListAsync();
        actuales.Should().ContainSingle().Which.Nombre.Should().Be(nombre2);
        var anterior = await db.Parrocos.AsNoTracking().SingleAsync(p => p.Nombre == nombre1);
        anterior.EsActual.Should().BeFalse();
        anterior.VigenteHasta.Should().Be(hoy.AddDays(-1));

        var html = await (await c.GetAsync("/Admin/Parrocos")).Content.ReadAsStringAsync();
        html.Should().Contain(nombre1).And.Contain(nombre2);
    }

    [Fact]
    public async Task Fecha_invalida_muestra_error_y_no_registra()
    {
        var c = await app.ClienteAdminAsync();
        var nombre = $"P. Malo {Guid.NewGuid():N}"[..20];

        await AppFixture.PostFormAsync(c, "/Admin/Parrocos", "/Admin/Parrocos?handler=Nuevo",
            new() { ["nombre"] = nombre, ["titulo"] = "Párroco", ["desde"] = "2999-01-01" });

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParroquiaDbContext>();
        (await db.Parrocos.AnyAsync(p => p.Nombre == nombre)).Should().BeFalse();
        var html = await (await c.GetAsync("/Admin/Parrocos")).Content.ReadAsStringAsync();
        html.Should().Contain("futura");
    }
}
