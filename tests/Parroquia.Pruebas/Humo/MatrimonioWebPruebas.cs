using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Configuracion;

namespace Parroquia.Pruebas.Humo;

/// <summary>Flujo web de Matrimonio con datos sintéticos únicos por prueba (la base se comparte entre pruebas).</summary>
[Collection(AppCollection.Nombre)]
public class MatrimonioWebPruebas(AppFixture app)
{
    private static string Unico() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Lista de pares (no diccionario) porque "Input.Testigos" se repite, una vez por fila.</summary>
    private static List<KeyValuePair<string, string>> Campos(
        string libro, string apellidoEsposo = "Prueba", string apellidoEsposa = "Ejemplo", string[]? testigos = null,
        string fecha = "21/04/2007", string edadEsposo = "29", string accion = "guardar", string version = "0")
    {
        var campos = new List<KeyValuePair<string, string>>
        {
            new("Input.Libro", libro), new("Input.Folio", "388"), new("Input.Partida", "12"),
            new("Input.FechaMatrimonio", fecha), new("Input.LugarCelebracion", "Iglesia de Mazatenango, Suchitepéquez"),
            new("Input.Esposo.Tratamiento", "don"), new("Input.Esposo.Nombres", "Carlos Genaro"),
            new("Input.Esposo.Apellidos", apellidoEsposo), new("Input.Esposo.Edad", edadEsposo),
            new("Input.Esposo.OriginarioDe", "San Gabriel"), new("Input.Esposo.FeligresDe", "P. San Bartolome"),
            new("Input.Esposo.Padre", "Samuel Alvarado"), new("Input.Esposo.Madre", "Estefana de León"),
            new("Input.Esposa.Tratamiento", "la Srita."), new("Input.Esposa.Nombres", "Gloria Inés"),
            new("Input.Esposa.Apellidos", apellidoEsposa), new("Input.Esposa.Edad", "22"),
            new("Input.Esposa.OriginarioDe", "San Gabriel"), new("Input.Esposa.FeligresDe", "parroquia San Bartolome"),
            new("Input.Esposa.Padre", "Bonifacio Ceballos"), new("Input.Esposa.Madre", "Concepcion Tupul"),
            new("Input.Ministro", "P. Andrés Martínez"), new("Input.NotasMarginales", ""),
            new("Input.Revisar", "false"), new("Input.MotivoRevision", ""),
            new("Input.Version", version), new("accion", accion),
        };
        foreach (var t in testigos ?? ["Noe Lopez Garcia", "Alma Luna de Lopez"]) campos.Add(new("Input.Testigos", t));
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
        var r = await PostAsync(c, "/Matrimonio/Editar", campos);
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
        var r = await app.Cliente().GetAsync("/Matrimonio");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Fact]
    public async Task Capturista_crea_con_testigos_y_el_registro_aparece_en_busqueda_por_cada_contrayente_y_en_detalle()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var (esposo, esposa) = ("Zeta" + u, "Omega" + u);

        var id = await CrearAsync(c, Campos("L" + u, esposo, esposa, testigos: ["Noe Lopez", "", "Alma Luna", "Rosa Paz"]));

        var detalle = await HtmlAsync(c, $"/Matrimonio/Detalle/{id}");
        detalle.Should().Contain("Carlos Genaro " + esposo).And.Contain("Gloria Inés " + esposa)
            .And.Contain("21 de abril de 2007").And.Contain("29 años").And.Contain("la Srita.")
            .And.Contain("Noe Lopez").And.Contain("Alma Luna").And.Contain("Rosa Paz").And.Contain("Se guardó el matrimonio");
        // Orden del formulario conservado y fila vacía ignorada.
        var lista = Regex.Match(detalle, "<ol class=\"mb-0 ps-3\">(.*?)</ol>", RegexOptions.Singleline).Groups[1].Value;
        Regex.Matches(lista, "<li>").Count.Should().Be(3);
        lista.IndexOf("Noe Lopez", StringComparison.Ordinal).Should().BeLessThan(lista.IndexOf("Alma Luna", StringComparison.Ordinal));

        (await HtmlAsync(c, $"/Matrimonio?texto={esposo.ToLowerInvariant()}")).Should().Contain(esposo).And.Contain("1 registros");
        (await HtmlAsync(c, $"/Matrimonio?texto={esposa.ToLowerInvariant()}")).Should().Contain(esposa).And.Contain("1 registros");
        (await HtmlAsync(c, $"/Matrimonio?texto=carlos+{esposa.ToLowerInvariant()}")).Should().Contain("No hay registros");
    }

    [Fact]
    public async Task El_formulario_nuevo_trae_dos_contrayentes_tratamientos_sugeridos_y_filas_de_testigo()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var html = await HtmlAsync(c, "/Matrimonio/Editar");

        Regex.Matches(html, "name=\"Input\\.Testigos\"").Count.Should().Be(2);
        html.Should().Contain("name=\"Input.Esposo.Nombres\"").And.Contain("name=\"Input.Esposa.Nombres\"")
            .And.Contain("name=\"Input.Esposo.Edad\"").And.Contain("name=\"Input.Esposa.Edad\"");
        html.Should().Contain("hx-get=\"/Matrimonio/Editar?handler=FilaTestigo\"").And.Contain("hx-target=\"#testigos\"")
            .And.Contain("data-quitar-fila").And.Contain("/js/filas-dinamicas.");
        html.Should().Contain("Iglesia de Mazatenango, Suchitepéquez");
        // "don" para el esposo y "la Srita." para la esposa vienen marcados en el catálogo sembrado.
        Regex.IsMatch(html, "<select[^>]*Input_Esposo_Tratamiento.*?<option[^>]*selected[^>]*>don</option>", RegexOptions.Singleline)
            .Should().BeTrue();
        Regex.IsMatch(html, "<select[^>]*Input_Esposa_Tratamiento.*?<option[^>]*selected[^>]*>la Srita\\.</option>", RegexOptions.Singleline)
            .Should().BeTrue();
    }

    [Fact]
    public async Task El_fragmento_FilaTestigo_devuelve_solo_una_fila_vacia()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var fila = await HtmlAsync(c, "/Matrimonio/Editar?handler=FilaTestigo");

        fila.Should().Contain("name=\"Input.Testigos\"").And.Contain("data-quitar-fila").And.NotContain("<html");
        Regex.Matches(fila, "name=\"Input\\.Testigos\"").Count.Should().Be(1);
    }

    [Fact]
    public async Task Secretaria_no_puede_pedir_filas_de_testigo()
    {
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var r = await s.GetAsync("/Matrimonio/Editar?handler=FilaTestigo");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Fact]
    public async Task Editar_quitando_un_testigo_y_cambiando_una_edad_se_ve_en_el_historial()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, "Edit" + u, testigos: ["Noe Lopez", "Alma Luna", "Rosa Paz"]));

        var formulario = await HtmlAsync(c, $"/Matrimonio/Editar/{id}");
        Regex.Matches(formulario, "name=\"Input\\.Testigos\"").Count.Should().Be(3);
        var r = await PostAsync(c, $"/Matrimonio/Editar/{id}",
            Campos("L" + u, "Edit" + u, testigos: ["Noe Lopez", "Rosa Paz"], edadEsposo: "30", version: VersionDe(formulario)));

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var detalle = await HtmlAsync(c, $"/Matrimonio/Detalle/{id}");
        detalle.Should().NotContain("Alma Luna</li>").And.Contain("Modificación")
            .And.Contain("Noe Lopez; Alma Luna; Rosa Paz → Noe Lopez; Rosa Paz")
            .And.Contain("Esposo.Edad:").And.Contain("29 → 30");
    }

    [Fact]
    public async Task Datos_invalidos_se_muestran_en_espanol_por_contrayente_y_conservan_lo_digitado()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();

        var campos = Campos("", "Falta" + u, testigos: ["Testigo Uno" + u], fecha: "hoy", edadEsposo: "5");
        campos.RemoveAll(k => k.Key == "Input.Esposa.Nombres");
        campos.Add(new("Input.Esposa.Nombres", ""));
        var r = await PostAsync(c, "/Matrimonio/Editar", campos, "/Matrimonio/Editar");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        html.Should().Contain("Escriba el libro.").And.Contain("no es válida")
            .And.Contain("Escriba los nombres de la esposa.").And.Contain("La edad del esposo debe ser un número entre 12 y 120.")
            .And.Contain("Testigo Uno" + u).And.Contain("Falta" + u);
        (await HtmlAsync(c, $"/Matrimonio?texto=falta{u}")).Should().Contain("No hay registros");
    }

    [Fact]
    public async Task Guardar_y_nuevo_conserva_el_libro_y_sugiere_folio_y_partida_siguientes()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var libro = "L" + Unico();

        var r = await PostAsync(c, "/Matrimonio/Editar", Campos(libro, "Serie" + Unico(), accion: "nuevo"));

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Matrimonio/Editar").And.Contain($"libro={libro}")
            .And.Contain("folio=389").And.Contain("partida=13");
    }

    [Fact]
    public async Task Duplicado_avisa_sin_guardar_y_se_confirma_con_guardar_de_todos_modos()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var libro = "L" + Unico();
        var u = Unico();
        await CrearAsync(c, Campos(libro, "Primero" + u));

        var aviso = await PostAsync(c, "/Matrimonio/Editar", Campos(libro, "Segundo" + u, fecha: "1/1/2008"));

        aviso.StatusCode.Should().Be(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await aviso.Content.ReadAsStringAsync())
            .Should().Contain("Ya existe un registro parecido").And.Contain("Primero" + u);
        (await HtmlAsync(c, $"/Matrimonio?texto=segundo{u}")).Should().Contain("No hay registros");

        var confirmado = await PostAsync(c, "/Matrimonio/Editar",
            Campos(libro, "Segundo" + u, fecha: "1/1/2008", accion: "guardar-forzar"));
        confirmado.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Secretaria_ve_y_busca_pero_no_crea_ni_edita()
    {
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(capturista, Campos("L" + u, "Lectura" + u));

        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var lista = await HtmlAsync(s, $"/Matrimonio?texto=lectura{u}");
        lista.Should().Contain("Lectura" + u).And.NotContain("Nuevo registro");
        (await HtmlAsync(s, $"/Matrimonio/Detalle/{id}")).Should().NotContain("/Matrimonio/Editar/").And.NotContain("Anular este registro");
        (await s.GetAsync("/Matrimonio/Editar")).Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        (await s.GetAsync($"/Matrimonio/Editar/{id}")).Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        (await PostAsync(s, "/Matrimonio/Editar", Campos("L" + Unico()), "/Cuenta/Logout")).Headers.Location!.ToString()
            .Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Fact]
    public async Task Anular_oculta_el_registro_y_solo_el_administrador_lo_ve_y_revierte()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, "Anulable" + u));
        var url = $"/Matrimonio/Detalle/{id}";

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
    public async Task Los_nombres_de_testigos_se_escapan_y_no_se_ejecutan_como_html()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, "Esc" + u, testigos: [$"<b>negrita{u}</b>"]));

        var crudo = await (await c.GetAsync($"/Matrimonio/Detalle/{id}")).Content.ReadAsStringAsync();

        crudo.Should().NotContain($"<b>negrita{u}</b>").And.Contain($"&lt;b&gt;negrita{u}&lt;/b&gt;");
    }

    [Fact]
    public async Task La_busqueda_de_inicio_encuentra_a_cada_contrayente_en_su_propia_fila_y_el_menu_descubre_el_modulo()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        await CrearAsync(c, Campos("L" + u, "Solis" + u, "Solis" + u));
        await CrearAsync(c, Campos("M" + u, "Unico" + u, "Aparte" + u));

        var comun = await HtmlAsync(c, $"/?texto=solis{u}");
        Regex.Matches(comun, "Solis" + u).Count.Should().BeGreaterThanOrEqualTo(2); // esposo y esposa: dos filas
        comun.Should().Contain("Matrimonio").And.Contain("/Matrimonio");

        (await HtmlAsync(c, $"/?texto=aparte{u}")).Should().Contain("Aparte" + u).And.NotContain("Unico" + u);
    }

    [Fact]
    public async Task Parametros_invalidos_en_la_URL_no_rompen_el_listado()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        (await c.GetAsync("/Matrimonio?desde=abc&hasta=-5&pagina=xyz&pendientes=quizas")).StatusCode.Should().Be(HttpStatusCode.OK);
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
    public async Task Secretaria_imprime_la_constancia_de_matrimonio_y_queda_en_la_bitacora()
    {
        await AsegurarParrocoAsync();
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(capturista, Campos("L" + u, "Const" + u, "Marco" + u));
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var vista = await HtmlAsync(s, $"/Constancias/matrimonio/{id}");
        vista.Should().Contain("CERTIFICACIÓN DE MATRIMONIO").And.Contain("literalmente dice:")
            .And.Contain("Carlos Genaro Const" + u).And.Contain("Gloria Inés Marco" + u)
            .And.Contain("21 de abril de 2007").And.Contain("Noe Lopez Garcia y Alma Luna de Lopez")
            .And.Contain("Sin anotaciones").And.Contain("Ref:").And.Contain("border-top");

        var r = await PostAsync(s, $"/Constancias/matrimonio/{id}", []);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(await r.Content.ReadAsByteArrayAsync(), 0, 5).Should().Be("%PDF-");

        (await HtmlAsync(s, $"/Matrimonio/Detalle/{id}")).Should().Contain("Constancias impresas").And.Contain("Reimprimir constancia");
        var admin = await app.ClienteAdminAsync();
        (await HtmlAsync(admin, "/Admin/Historial?pestana=bitacora&entidad=Matrimonio"))
            .Should().Contain("Imprimir constancia").And.Contain($"Matrimonio #{id}");
    }

    [Fact]
    public async Task Constancia_con_datos_faltantes_pide_confirmacion()
    {
        await AsegurarParrocoAsync();
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var id = await CrearAsync(capturista, Campos("L" + Unico(), "Aviso" + Unico(), testigos: []));
        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        var vista = await HtmlAsync(s, $"/Constancias/matrimonio/{id}");

        vista.Should().Contain("Revise antes de imprimir").And.Contain("Faltan los testigos")
            .And.Contain("name=\"confirmar\"");
        var sin = await PostAsync(s, $"/Constancias/matrimonio/{id}", []);
        sin.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        var con = await PostAsync(s, $"/Constancias/matrimonio/{id}", [new("confirmar", "true")]);
        con.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task Capturista_no_imprime_la_constancia_de_matrimonio()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var id = await CrearAsync(c, Campos("L" + Unico(), "Sin" + Unico()));

        var r = await c.GetAsync($"/Constancias/matrimonio/{id}");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        (await HtmlAsync(c, $"/Matrimonio/Detalle/{id}")).Should().NotContain("Imprimir constancia");
    }
}
