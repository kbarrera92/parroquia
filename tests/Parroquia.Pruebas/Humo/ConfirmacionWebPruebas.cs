using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Configuracion;

namespace Parroquia.Pruebas.Humo;

/// <summary>Flujo web de Confirmación con datos sintéticos únicos por prueba (la base se comparte entre pruebas).</summary>
[Collection(AppCollection.Nombre)]
public class ConfirmacionWebPruebas(AppFixture app)
{
    private static string Unico() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Lista de pares (no diccionario) porque "Input.Padrinos" se repite, una vez por fila.</summary>
    private static List<KeyValuePair<string, string>> Campos(
        string libro, string apellidos = "Prueba", string[]? padrinos = null, string fecha = "17/10/2004",
        string accion = "guardar", string version = "0")
    {
        var campos = new List<KeyValuePair<string, string>>
        {
            new("Input.Libro", libro), new("Input.Folio", "388"), new("Input.Partida", "12"),
            new("Input.Nombres", "Ana María"), new("Input.Apellidos", apellidos),
            new("Input.FechaConfirmacion", fecha),
            new("Input.Ministro", "Mons. Obispo Prueba"), new("Input.NotasMarginales", ""),
            new("Input.Revisar", "false"), new("Input.MotivoRevision", ""),
            new("Input.Version", version), new("accion", accion),
        };
        foreach (var p in padrinos ?? ["Luis Mena", "Rosa Díaz"]) campos.Add(new("Input.Padrinos", p));
        return campos;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient c, string urlPost, List<KeyValuePair<string, string>> campos, string? paginaConToken = null)
    {
        campos.Add(new("__RequestVerificationToken", await AppFixture.TokenAsync(c, paginaConToken ?? urlPost)));
        return await c.PostAsync(urlPost, new FormUrlEncodedContent(campos));
    }

    private static async Task<long> CrearAsync(HttpClient c, List<KeyValuePair<string, string>> campos)
    {
        var r = await PostAsync(c, "/Confirmacion/Editar", campos);
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return IdDe(r);
    }

    private static long IdDe(HttpResponseMessage r) =>
        long.Parse(Regex.Match(r.Headers.Location!.ToString(), @"Detalle/(\d+)").Groups[1].Value);

    private static async Task<string> HtmlAsync(HttpClient c, string url) =>
        WebUtility.HtmlDecode(await (await c.GetAsync(url)).Content.ReadAsStringAsync());

    private static string VersionDe(string html) =>
        Regex.Match(html, "Input\\.Version\"[^>]*value=\"(\\d+)\"").Groups[1].Value;

    [Fact]
    public async Task Anonimo_va_al_login()
    {
        var r = await app.Cliente().GetAsync("/Confirmacion");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Fact]
    public async Task Capturista_crea_con_padrinos_y_el_registro_aparece_en_busqueda_y_detalle()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Zeta" + Unico();

        var id = await CrearAsync(c, Campos("L" + Unico(), apellido, padrinos: ["Luis Mena", "", "Rosa Díaz", "Ana Paz"]));

        var detalle = await HtmlAsync(c, $"/Confirmacion/Detalle/{id}");
        detalle.Should().Contain("Ana María " + apellido).And.Contain("17 de octubre de 2004")
            .And.Contain("Luis Mena").And.Contain("Rosa Díaz").And.Contain("Ana Paz").And.Contain("Se guardó el registro");
        // Orden del formulario conservado y fila vacía ignorada.
        detalle.IndexOf("Luis Mena", StringComparison.Ordinal).Should().BeLessThan(detalle.IndexOf("Rosa Díaz", StringComparison.Ordinal));
        var lista = Regex.Match(detalle, "<ol class=\"mb-0 ps-3\">(.*?)</ol>", RegexOptions.Singleline).Groups[1].Value;
        Regex.Matches(lista, "<li>").Count.Should().Be(3);

        (await HtmlAsync(c, $"/Confirmacion?texto={apellido.ToLowerInvariant()}")).Should().Contain(apellido).And.Contain("1 registros");
    }

    [Fact]
    public async Task El_formulario_nuevo_trae_filas_de_padrino_y_el_boton_agregar_apunta_al_fragmento()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var html = await HtmlAsync(c, "/Confirmacion/Editar");

        Regex.Matches(html, "name=\"Input\\.Padrinos\"").Count.Should().Be(2);
        html.Should().Contain("hx-get=\"/Confirmacion/Editar?handler=FilaPadrino\"").And.Contain("hx-target=\"#padrinos\"")
            .And.Contain("data-quitar-fila").And.Contain("/js/filas-dinamicas."); // el nombre lleva huella (assets estáticos)
    }

    [Fact]
    public async Task El_fragmento_FilaPadrino_devuelve_solo_una_fila_vacia()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var fila = await HtmlAsync(c, "/Confirmacion/Editar?handler=FilaPadrino");

        fila.Should().Contain("name=\"Input.Padrinos\"").And.Contain("data-quitar-fila").And.NotContain("<html");
        Regex.Matches(fila, "name=\"Input\\.Padrinos\"").Count.Should().Be(1);
    }

    [Fact]
    public async Task Secretaria_no_puede_pedir_filas_de_padrino()
    {
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var r = await s.GetAsync("/Confirmacion/Editar?handler=FilaPadrino");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Fact]
    public async Task Editar_quitando_un_padrino_actualiza_el_registro_y_el_historial_lo_muestra()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, "Padr" + u, padrinos: ["Luis Mena", "Rosa Díaz", "Ana Paz"]));

        var formulario = await HtmlAsync(c, $"/Confirmacion/Editar/{id}");
        Regex.Matches(formulario, "name=\"Input\\.Padrinos\"").Count.Should().Be(3);
        var r = await PostAsync(c, $"/Confirmacion/Editar/{id}",
            Campos("L" + u, "Padr" + u, padrinos: ["Luis Mena", "Ana Paz"], version: VersionDe(formulario)));

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var detalle = await HtmlAsync(c, $"/Confirmacion/Detalle/{id}");
        detalle.Should().NotContain("Rosa Díaz</li>").And.Contain("Modificación").And.Contain("Padrinos:")
            .And.Contain("Luis Mena; Rosa Díaz; Ana Paz → Luis Mena; Ana Paz");
    }

    [Fact]
    public async Task Datos_invalidos_se_muestran_en_espanol_y_conservan_los_padrinos_digitados()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Falta" + Unico();

        var r = await PostAsync(c, "/Confirmacion/Editar",
            Campos("", apellido, padrinos: ["Padrino Uno" + apellido], fecha: "hoy"), "/Confirmacion/Editar");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        html.Should().Contain("Escriba el libro.").And.Contain("no es válida").And.Contain("Padrino Uno" + apellido);
        (await HtmlAsync(c, $"/Confirmacion?texto={apellido}")).Should().Contain("No hay registros");
    }

    [Fact]
    public async Task Guardar_y_nuevo_conserva_el_libro_y_sugiere_folio_y_partida_siguientes()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var libro = "L" + Unico();

        var r = await PostAsync(c, "/Confirmacion/Editar", Campos(libro, "Serie" + Unico(), accion: "nuevo"));

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Confirmacion/Editar").And.Contain($"libro={libro}")
            .And.Contain("folio=389").And.Contain("partida=13");
    }

    [Fact]
    public async Task Duplicado_avisa_sin_guardar_y_se_confirma_con_guardar_de_todos_modos()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var libro = "L" + Unico();
        var u = Unico();
        await CrearAsync(c, Campos(libro, "Primero" + u));

        var aviso = await PostAsync(c, "/Confirmacion/Editar", Campos(libro, "Segundo" + u, fecha: "1/1/2005"));

        aviso.StatusCode.Should().Be(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await aviso.Content.ReadAsStringAsync())
            .Should().Contain("Ya existe un registro parecido").And.Contain("Primero" + u);
        (await HtmlAsync(c, $"/Confirmacion?texto=segundo{u}")).Should().Contain("No hay registros");

        var confirmado = await PostAsync(c, "/Confirmacion/Editar", Campos(libro, "Segundo" + u, fecha: "1/1/2005", accion: "guardar-forzar"));
        confirmado.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Secretaria_ve_y_busca_pero_no_crea_ni_edita()
    {
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(capturista, Campos("L" + u, "Lectura" + u));

        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var lista = await HtmlAsync(s, $"/Confirmacion?texto=lectura{u}");
        lista.Should().Contain("Lectura" + u).And.NotContain("Nuevo registro");
        (await HtmlAsync(s, $"/Confirmacion/Detalle/{id}")).Should().NotContain("/Confirmacion/Editar/").And.NotContain("Anular este registro");
        (await s.GetAsync("/Confirmacion/Editar")).Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        (await s.GetAsync($"/Confirmacion/Editar/{id}")).Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        (await PostAsync(s, "/Confirmacion/Editar", Campos("L" + Unico()), "/Cuenta/Logout")).Headers.Location!.ToString()
            .Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Fact]
    public async Task Anular_oculta_el_registro_y_solo_el_administrador_lo_ve_y_revierte()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, "Anulable" + u));
        var url = $"/Confirmacion/Detalle/{id}";

        (await PostAsync(c, url + "?handler=Anular", [new("motivo", "Registro duplicado")], url)).StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await c.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var admin = await app.ClienteAdminAsync();
        (await HtmlAsync(admin, url)).Should().Contain("Registro anulado").And.Contain("Revertir anulación");
        (await PostAsync(c, url + "?handler=Revertir", [], "/Cuenta/Logout")).Headers.Location!.ToString()
            .Should().Contain("/Cuenta/AccesoDenegado");
        (await PostAsync(admin, url + "?handler=Revertir", [], url)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await c.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Los_nombres_de_padrinos_se_escapan_y_no_se_ejecutan_como_html()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, "Esc" + u, padrinos: [$"<b>negrita{u}</b>"]));

        var crudo = await (await c.GetAsync($"/Confirmacion/Detalle/{id}")).Content.ReadAsStringAsync();

        crudo.Should().NotContain($"<b>negrita{u}</b>").And.Contain($"&lt;b&gt;negrita{u}&lt;/b&gt;");
    }

    [Fact]
    public async Task La_busqueda_de_inicio_y_el_menu_descubren_el_modulo()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        await CrearAsync(c, Campos("L" + u, "Global" + u));

        var inicio = await HtmlAsync(c, $"/?texto=global{u}");

        inicio.Should().Contain("Global" + u).And.Contain("Confirmación").And.Contain("/Confirmacion");
    }

    [Fact]
    public async Task Parametros_invalidos_en_la_URL_no_rompen_el_listado()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        (await c.GetAsync("/Confirmacion?desde=abc&hasta=-5&pagina=xyz&pendientes=quizas")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- Constancia (página genérica /Constancias/{clave}/{id}) ---

    private async Task AsegurarParrocoAsync()
    {
        using var scope = app.Services.CreateScope();
        var parrocos = scope.ServiceProvider.GetRequiredService<ServicioParroco>();
        if (await parrocos.ObtenerActualAsync() is null)
            (await parrocos.RegistrarNuevoAsync("P. Prueba Ejemplo", "Párroco", new DateOnly(2000, 1, 1))).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task Secretaria_imprime_la_constancia_de_confirmacion_con_padrinos_y_queda_en_la_bitacora()
    {
        await AsegurarParrocoAsync();
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(capturista, Campos("L" + u, "Const" + u, padrinos: ["Gildardo Alvarado", "Sonia de Alvarado"]));
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var vista = await HtmlAsync(s, $"/Constancias/confirmacion/{id}");
        vista.Should().Contain("CERTIFICACIÓN DE CONFIRMACIÓN").And.Contain($"ANA MARÍA CONST{u.ToUpperInvariant()}")
            .And.Contain("Sus padrinos fueron:").And.Contain("17 de octubre de 2004")
            .And.Contain("Gildardo Alvarado y Sonia de Alvarado").And.Contain("libro de Confirmación No. L" + u);

        var r = await PostAsync(s, $"/Constancias/confirmacion/{id}", []);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(await r.Content.ReadAsByteArrayAsync(), 0, 5).Should().Be("%PDF-");

        (await HtmlAsync(s, $"/Confirmacion/Detalle/{id}")).Should().Contain("Constancias impresas").And.Contain("Reimprimir constancia");
        var admin = await app.ClienteAdminAsync();
        (await HtmlAsync(admin, "/Admin/Historial?pestana=bitacora&entidad=Confirmacion"))
            .Should().Contain("Imprimir constancia").And.Contain($"Confirmacion #{id}");
    }

    [Fact]
    public async Task Constancia_con_datos_faltantes_pide_confirmacion()
    {
        await AsegurarParrocoAsync();
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var id = await CrearAsync(capturista, Campos("L" + Unico(), "Aviso" + Unico(), padrinos: []));
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var vista = await HtmlAsync(s, $"/Constancias/confirmacion/{id}");

        vista.Should().Contain("Revise antes de imprimir").And.Contain("Faltan los padrinos")
            .And.Contain("name=\"confirmar\"");
        var sin = await PostAsync(s, $"/Constancias/confirmacion/{id}", []);
        sin.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var con = await PostAsync(s, $"/Constancias/confirmacion/{id}", [new("confirmar", "true")]);
        con.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task Capturista_no_imprime_la_constancia_de_confirmacion()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var id = await CrearAsync(c, Campos("L" + Unico(), "Sin" + Unico()));

        var r = await c.GetAsync($"/Constancias/confirmacion/{id}");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        (await HtmlAsync(c, $"/Confirmacion/Detalle/{id}")).Should().NotContain("Imprimir constancia");
    }
}
