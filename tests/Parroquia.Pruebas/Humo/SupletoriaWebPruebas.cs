using System.Net;
using System.Text.RegularExpressions;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

/// <summary>Flujo web de la partida supletoria (módulo Comunión). Datos sintéticos únicos por prueba: la base se comparte.</summary>
[Collection(AppCollection.Nombre)]
public class SupletoriaWebPruebas(AppFixture app)
{
    private static string Unico() => Guid.NewGuid().ToString("N")[..8];

    private static Dictionary<string, string> Campos(
        string apellidos, string libroSup, string testigo = "Felipa Prueba", string fecha = "2/10/2026",
        string libro = "", string folio = "", string partida = "", string accion = "guardar") => new()
    {
        ["Input.Libro"] = libro,
        ["Input.Folio"] = folio,
        ["Input.Partida"] = partida,
        ["Input.Nombres"] = "Alma Verónica",
        ["Input.Apellidos"] = apellidos,
        ["Input.Fecha"] = "24/8/1965",
        ["Input.Ministro"] = "",
        ["Input.NotasMarginales"] = "",
        ["Input.Revisar"] = "false",
        ["Input.MotivoRevision"] = "",
        ["Input.Version"] = "0",
        ["Input.Supletoria.NumeraSistema"] = "false",
        ["Input.Supletoria.Libro"] = libroSup,
        ["Input.Supletoria.Folio"] = "5",
        ["Input.Supletoria.Partida"] = "200",
        ["Input.Supletoria.Fecha"] = fecha,
        ["Input.Supletoria.Testigo"] = testigo,
        ["Input.Supletoria.ParentescoTestigo"] = "Tía",
        ["accion"] = accion,
    };

    private static Task<HttpResponseMessage> PostAsync(HttpClient c, Dictionary<string, string> campos) =>
        AppFixture.PostFormAsync(c, "/Comunion/Editar?modo=supletoria", "/Comunion/Editar?modo=supletoria", campos);

    private static async Task<string> HtmlAsync(HttpClient c, string url) =>
        WebUtility.HtmlDecode(await (await c.GetAsync(url)).Content.ReadAsStringAsync());

    [Fact]
    public async Task El_formulario_ofrece_la_pestana_de_partida_supletoria_con_sus_campos()
    {
        var c = await app.ClienteAdminAsync();   // tiene ambos permisos de alta: ve la opción de numerar

        var html = await HtmlAsync(c, "/Comunion/Editar?modo=supletoria");

        html.Should().Contain("Partida supletoria").And.Contain("Input.Supletoria.Testigo")
            .And.Contain("Input.Supletoria.Fecha").And.Contain("Numerar con el sistema")
            .And.Contain("Ubicación original en el libro (opcional)");
        var normal = await HtmlAsync(c, "/Comunion/Editar");
        normal.Should().Contain("Partida supletoria").And.NotContain("Input.Supletoria.Testigo"); // solo la pestaña
    }

    [Fact]
    public async Task Capturista_asienta_una_supletoria_sin_original_y_el_registro_se_busca_como_cualquier_otro()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Zeta" + Unico();
        var libroSup = "S" + Unico();

        var r = await PostAsync(c, Campos(apellido, libroSup));

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var id = long.Parse(Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);
        var detalle = await HtmlAsync(c, $"/Comunion/Detalle/{id}");
        detalle.Should().Contain("Alma Verónica " + apellido)
            .And.Contain("Partida supletoria").And.Contain($"Libro {libroSup}, folio 5, partida 200")
            .And.Contain("Felipa Prueba (Tía)").And.Contain("2 de octubre de 2026")
            .And.Contain("Supletorio " + libroSup);

        var lista = await HtmlAsync(c, $"/Comunion?texto={apellido.ToLowerInvariant()}");
        lista.Should().Contain(apellido).And.Contain("Supletorio " + libroSup);
        var inicio = await HtmlAsync(c, $"/?texto={apellido.ToLowerInvariant()}");
        inicio.Should().Contain(apellido);
    }

    [Fact]
    public async Task Falta_el_testigo_y_la_fecha_muestra_errores_sin_guardar()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Eta" + Unico();

        var r = await PostAsync(c, Campos(apellido, "S" + Unico(), testigo: "", fecha: ""));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        html.Should().Contain("Escriba el nombre del testigo").And.Contain("al menos el año");
        (await HtmlAsync(c, $"/Comunion?texto={apellido.ToLowerInvariant()}")).Should().NotContain(apellido);
    }

    [Fact]
    public async Task Secretaria_solo_asienta_supletorias_numeradas_por_el_sistema()
    {
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var get = await HtmlAsync(s, "/Comunion/Editar?modo=supletoria");
        get.Should().Contain("Input.Supletoria.Testigo").And.NotContain("Numerar con el sistema");
        Regex.Match(get, @"name=""Input\.Supletoria\.NumeraSistema""[^>]*value=""([^""]*)""").Groups[1].Value.Should().Be("true");

        // Transcribir una supletoria de papel exige Registros.Transcribir.
        var apellido = "Theta" + Unico();
        var post = await AppFixture.PostFormAsync(s, "/Cuenta/Logout", "/Comunion/Editar?modo=supletoria", Campos(apellido, "S" + Unico()));
        post.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Fact]
    public async Task El_registro_con_supletoria_se_edita_y_anular_lo_saca_de_la_busqueda()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Iota" + Unico();
        var libroSup = "S" + Unico();
        var r = await PostAsync(c, Campos(apellido, libroSup));
        var id = long.Parse(Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);

        var editar = await HtmlAsync(c, $"/Comunion/Editar/{id}");
        editar.Should().Contain("Input.Supletoria.Testigo").And.Contain("Felipa Prueba").And.Contain("no se cambia");
        var version = Regex.Match(editar, "Input\\.Version\"[^>]*value=\"(\\d+)\"").Groups[1].Value;

        var campos = Campos(apellido, libroSup, testigo: "Testigo Nuevo", libro: "Supletorio " + libroSup, folio: "5", partida: "200");
        campos["Input.Version"] = version;
        var guardado = await AppFixture.PostFormAsync(c, $"/Comunion/Editar/{id}", $"/Comunion/Editar/{id}", campos);
        guardado.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await HtmlAsync(c, $"/Comunion/Detalle/{id}")).Should().Contain("Testigo Nuevo");

        var anular = await AppFixture.PostFormAsync(c, $"/Comunion/Detalle/{id}", $"/Comunion/Detalle/{id}?handler=Anular", new() { ["motivo"] = "Error de captura" });
        anular.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await HtmlAsync(c, $"/Comunion?texto={apellido.ToLowerInvariant()}")).Should().NotContain(apellido);
    }

    [Theory]
    [InlineData("Bautismo")]
    [InlineData("Confirmacion")]
    [InlineData("Matrimonio")]
    public async Task Los_otros_sacramentos_tambien_ofrecen_la_pestana_de_partida_supletoria(string modulo)
    {
        var c = await app.ClienteAdminAsync();

        var supletoria = await HtmlAsync(c, $"/{modulo}/Editar?modo=supletoria");
        var normal = await HtmlAsync(c, $"/{modulo}/Editar");

        supletoria.Should().Contain("Input.Supletoria.Testigo").And.Contain("Numerar con el sistema")
            .And.Contain("Ubicación original en el libro (opcional)");
        normal.Should().Contain("Partida supletoria").And.NotContain("Input.Supletoria.Testigo");
    }

    [Fact]
    public async Task El_libro_de_supletorias_lista_la_partida_con_enlace_al_registro_y_filtra_por_libro()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Kappa" + Unico();
        var libroSup = "S" + Unico();
        var r = await PostAsync(c, Campos(apellido, libroSup));
        var id = long.Parse(Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);

        var html = await HtmlAsync(c, $"/Supletorias?libro={libroSup}");

        html.Should().Contain(apellido).And.Contain("Primera Comunión").And.Contain("Felipa Prueba (Tía)")
            .And.Contain($"/Comunion/Detalle/{id}").And.Contain("2 de octubre de 2026").And.Contain("Nueva partida supletoria");
        (await HtmlAsync(c, "/Supletorias?libro=NoExiste" + Unico())).Should().Contain("No hay partidas supletorias con esos datos");
        (await HtmlAsync(c, "/")).Should().Contain("Libro de Supletorias");
    }

    [Fact]
    public async Task Secretaria_ve_el_libro_de_supletorias_y_puede_crear_las_numeradas()
    {
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var html = await HtmlAsync(s, "/Supletorias");

        html.Should().Contain("Libro de Supletorias").And.Contain("Nueva partida supletoria");
    }

    [Fact]
    public async Task Admin_abre_numera_con_el_sistema_y_cierra_el_libro_digital_de_supletorias()
    {
        const string pagina = "/Admin/LibrosDigitales";
        var admin = await app.ClienteAdminAsync();
        await AppFixture.PostFormAsync(admin, pagina, $"{pagina}?handler=Cerrar", new() { ["clave"] = "supletoria" }); // por si quedó uno abierto
        var numero = "T" + Unico();
        await AppFixture.PostFormAsync(admin, pagina, $"{pagina}?handler=Abrir", new()
        {
            ["clave"] = "supletoria", ["numero"] = numero, ["folioInicial"] = "1", ["partidaInicial"] = "1",
            ["partidasPorFolio"] = "2", ["foliosPorLibro"] = "50", ["partidasYaEnFolio"] = "0", ["modoPartida"] = "Correlativa",
        });

        try
        {
            (await HtmlAsync(admin, pagina)).Should().Contain("Libro de Supletorias").And.Contain(numero);

            var c = await app.ClienteConRolAsync(Roles.Secretaria);
            var apellido = "Lambda" + Unico();
            var campos = Campos(apellido, "");
            campos["Input.Supletoria.NumeraSistema"] = "true";
            var r = await PostAsync(c, campos);
            r.StatusCode.Should().Be(HttpStatusCode.Redirect);
            var id = long.Parse(Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);

            var detalle = await HtmlAsync(c, $"/Comunion/Detalle/{id}");
            detalle.Should().Contain("numerada por el sistema").And.Contain($"Libro {numero}, folio 1, partida 1");
        }
        finally
        {
            var cerrar = await AppFixture.PostFormAsync(admin, pagina, $"{pagina}?handler=Cerrar", new() { ["clave"] = "supletoria" });
            cerrar.StatusCode.Should().Be(HttpStatusCode.Redirect);
        }
    }
}
