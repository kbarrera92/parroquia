using System.Net;
using System.Text.RegularExpressions;
using Parroquia.Aplicacion.Seguridad;

namespace Parroquia.Pruebas.Humo;

/// <summary>Flujo web de Comunión con datos sintéticos únicos por prueba (la base se comparte entre pruebas).</summary>
[Collection(AppCollection.Nombre)]
public class ComunionWebPruebas(AppFixture app)
{
    private static string Unico() => Guid.NewGuid().ToString("N")[..8];

    private static Dictionary<string, string> Campos(
        string libro, string folio = "60", string partida = "12", string nombres = "Ana María",
        string apellidos = "Prueba", string fecha = "2/10/2004", string accion = "guardar", string version = "0") => new()
    {
        ["Input.Libro"] = libro,
        ["Input.Folio"] = folio,
        ["Input.Partida"] = partida,
        ["Input.Nombres"] = nombres,
        ["Input.Apellidos"] = apellidos,
        ["Input.Fecha"] = fecha,
        ["Input.Ministro"] = "",
        ["Input.NotasMarginales"] = "",
        ["Input.Revisar"] = "false",
        ["Input.MotivoRevision"] = "",
        ["Input.Version"] = version,
        ["accion"] = accion,
    };

    private static Task<HttpResponseMessage> CrearRespuestaAsync(HttpClient c, Dictionary<string, string> campos) =>
        AppFixture.PostFormAsync(c, "/Comunion/Editar", "/Comunion/Editar", campos);

    private static async Task<long> CrearAsync(HttpClient c, Dictionary<string, string> campos)
    {
        var r = await CrearRespuestaAsync(c, campos);
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
        var r = await app.Cliente().GetAsync("/Comunion");

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain("/Cuenta/Login");
    }

    [Fact]
    public async Task Capturista_crea_y_el_registro_aparece_en_busqueda_y_detalle()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Zeta" + Unico();

        var id = await CrearAsync(c, Campos("L" + Unico(), apellidos: apellido));

        var detalle = await HtmlAsync(c, $"/Comunion/Detalle/{id}");
        detalle.Should().Contain("Ana María " + apellido).And.Contain("2 de octubre de 2004").And.Contain("Se guardó el registro");

        var busqueda = await HtmlAsync(c, $"/Comunion?texto={apellido.ToLowerInvariant()}");
        busqueda.Should().Contain(apellido).And.Contain("1 registros");
    }

    [Fact]
    public async Task Busqueda_ignora_tildes_y_mayusculas()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        await CrearAsync(c, Campos("L" + u, nombres: "José", apellidos: "Peña" + u));

        var html = await HtmlAsync(c, $"/Comunion?texto=JOSE+pena{u}");

        html.Should().Contain("Peña" + u);
    }

    [Fact]
    public async Task Guardar_y_nuevo_conserva_el_libro_y_sugiere_folio_y_partida_siguientes()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var libro = "L" + Unico();

        var r = await CrearRespuestaAsync(c, Campos(libro, folio: "60", partida: "12", apellidos: "Serie" + Unico(), accion: "nuevo"));

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var destino = r.Headers.Location!.ToString();
        destino.Should().Contain("/Comunion/Editar").And.Contain($"libro={libro}").And.Contain("folio=61").And.Contain("partida=13");

        var formulario = await HtmlAsync(c, destino);
        formulario.Should().Contain($"value=\"{libro}\"").And.Contain("value=\"61\"").And.Contain("value=\"13\"");
        formulario.Should().Contain("Se guardó el registro");
    }

    [Fact]
    public async Task Datos_obligatorios_faltantes_se_muestran_en_espanol_y_no_se_guarda()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var apellido = "Falta" + Unico();

        var r = await CrearRespuestaAsync(c, Campos("", apellidos: apellido));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        html.Should().Contain("Escriba el libro.").And.Contain(apellido);
        (await HtmlAsync(c, $"/Comunion?texto={apellido}")).Should().Contain("No hay registros");
    }

    [Fact]
    public async Task Duplicado_avisa_sin_guardar_y_se_confirma_con_guardar_de_todos_modos()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var libro = "L" + Unico();
        var u = Unico();
        await CrearAsync(c, Campos(libro, apellidos: "Primero" + u));

        var segundo = Campos(libro, nombres: "Otra", apellidos: "Segundo" + u, fecha: "1/1/2005");
        var aviso = await CrearRespuestaAsync(c, segundo);

        aviso.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await aviso.Content.ReadAsStringAsync());
        html.Should().Contain("Ya existe un registro parecido").And.Contain("Guardar de todos modos").And.Contain("Primero" + u);
        (await HtmlAsync(c, $"/Comunion?texto=segundo{u}")).Should().Contain("No hay registros");

        segundo["accion"] = "guardar-forzar";
        var confirmado = await CrearRespuestaAsync(c, segundo);
        confirmado.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await HtmlAsync(c, $"/Comunion?texto=segundo{u}")).Should().Contain("Segundo" + u);
    }

    [Fact]
    public async Task Editar_actualiza_el_registro_y_el_historial_muestra_el_cambio()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, apellidos: "Antes" + u));

        var formulario = await HtmlAsync(c, $"/Comunion/Editar/{id}");
        formulario.Should().Contain("Antes" + u);
        var campos = Campos("L" + u, apellidos: "Despues" + u, version: VersionDe(formulario));
        var r = await AppFixture.PostFormAsync(c, $"/Comunion/Editar/{id}", $"/Comunion/Editar/{id}", campos);

        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.ToString().Should().Contain($"/Comunion/Detalle/{id}");
        var detalle = await HtmlAsync(c, $"/Comunion/Detalle/{id}");
        detalle.Should().Contain("Despues" + u).And.Contain("Modificación").And.Contain("Apellidos:")
            .And.Contain("Antes" + u + " → Despues" + u);
    }

    [Fact]
    public async Task Dos_ediciones_simultaneas_la_segunda_recibe_aviso_y_conserva_lo_digitado()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, apellidos: "Base" + u));
        var version = VersionDe(await HtmlAsync(c, $"/Comunion/Editar/{id}"));

        var primera = await AppFixture.PostFormAsync(c, $"/Comunion/Editar/{id}", $"/Comunion/Editar/{id}",
            Campos("L" + u, apellidos: "Primera" + u, version: version));
        primera.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var segunda = await AppFixture.PostFormAsync(c, $"/Comunion/Editar/{id}", $"/Comunion/Editar/{id}",
            Campos("L" + u, apellidos: "Segunda" + u, version: version));

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await segunda.Content.ReadAsStringAsync());
        html.Should().Contain("Otro usuario modificó este registro").And.Contain("Segunda" + u);
        (await HtmlAsync(c, $"/Comunion/Detalle/{id}")).Should().Contain("Primera" + u);
    }

    [Fact]
    public async Task Secretaria_ve_y_busca_pero_no_crea_edita_ni_anula()
    {
        var capturista = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(capturista, Campos("L" + u, apellidos: "Lectura" + u));

        var s = await app.ClienteConRolAsync(Roles.Secretaria);

        (await s.GetAsync("/Comunion")).StatusCode.Should().Be(HttpStatusCode.OK);
        var lista = await HtmlAsync(s, $"/Comunion?texto=lectura{u}");
        lista.Should().Contain("Lectura" + u).And.NotContain("Nuevo registro");
        var detalle = await HtmlAsync(s, $"/Comunion/Detalle/{id}");
        detalle.Should().Contain("Lectura" + u).And.NotContain("Anular este registro").And.NotContain("/Comunion/Editar/");

        var nuevo = await s.GetAsync("/Comunion/Editar");
        nuevo.StatusCode.Should().Be(HttpStatusCode.Redirect);
        nuevo.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        (await s.GetAsync($"/Comunion/Editar/{id}")).Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");

        var anular = await AppFixture.PostFormAsync(s, "/Cuenta/Logout", $"/Comunion/Detalle/{id}?handler=Anular",
            new() { ["motivo"] = "No debería poder" });
        anular.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
        var guardar = await AppFixture.PostFormAsync(s, "/Cuenta/Logout", "/Comunion/Editar", Campos("L" + Unico()));
        guardar.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");
    }

    [Fact]
    public async Task Anular_exige_motivo_oculta_el_registro_y_solo_el_administrador_lo_ve_y_revierte()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, apellidos: "Anulable" + u));

        var sinMotivo = await AppFixture.PostFormAsync(c, $"/Comunion/Detalle/{id}", $"/Comunion/Detalle/{id}?handler=Anular",
            new() { ["motivo"] = " " });
        sinMotivo.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await HtmlAsync(c, $"/Comunion/Detalle/{id}")).Should().Contain("Escriba el motivo de la anulación");

        var anulado = await AppFixture.PostFormAsync(c, $"/Comunion/Detalle/{id}", $"/Comunion/Detalle/{id}?handler=Anular",
            new() { ["motivo"] = "Registro duplicado" });
        anulado.StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await c.GetAsync($"/Comunion/Detalle/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await HtmlAsync(c, $"/Comunion?texto=anulable{u}&anulados=true")).Should().Contain("No hay registros");

        var admin = await app.ClienteAdminAsync();
        var vistaAdmin = await HtmlAsync(admin, $"/Comunion/Detalle/{id}");
        vistaAdmin.Should().Contain("Registro anulado").And.Contain("Registro duplicado").And.Contain("Revertir anulación")
            .And.Contain("Anulación");
        (await HtmlAsync(admin, $"/Comunion?texto=anulable{u}&anulados=true")).Should().Contain("Anulable" + u);

        var capturistaNoRevierte = await AppFixture.PostFormAsync(c, "/Cuenta/Logout", $"/Comunion/Detalle/{id}?handler=Revertir", []);
        capturistaNoRevierte.Headers.Location!.ToString().Should().Contain("/Cuenta/AccesoDenegado");

        var revertido = await AppFixture.PostFormAsync(admin, $"/Comunion/Detalle/{id}", $"/Comunion/Detalle/{id}?handler=Revertir", []);
        revertido.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await c.GetAsync($"/Comunion/Detalle/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ver_el_detalle_deja_rastro_en_la_bitacora_de_acceso()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var id = await CrearAsync(c, Campos("L" + Unico(), apellidos: "Vista" + Unico()));
        (await c.GetAsync($"/Comunion/Detalle/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = await app.ClienteAdminAsync();
        var bitacora = await HtmlAsync(admin, "/Admin/Historial?pestana=bitacora&entidad=Comunion");

        bitacora.Should().Contain("Ver registro").And.Contain($"Comunion #{id}");
    }

    [Fact]
    public async Task Los_nombres_se_escapan_y_no_se_ejecutan_como_html()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        var id = await CrearAsync(c, Campos("L" + u, apellidos: $"<b>negrita{u}</b>"));

        var crudo = await (await c.GetAsync($"/Comunion/Detalle/{id}")).Content.ReadAsStringAsync();

        crudo.Should().NotContain($"<b>negrita{u}</b>");
        crudo.Should().Contain($"&lt;b&gt;negrita{u}&lt;/b&gt;");
    }

    [Fact]
    public async Task Peticion_htmx_devuelve_solo_la_tabla_y_la_normal_la_pagina_completa()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);
        var u = Unico();
        await CrearAsync(c, Campos("L" + u, apellidos: "Parcial" + u));

        var peticion = new HttpRequestMessage(HttpMethod.Get, $"/Comunion?texto=parcial{u}");
        peticion.Headers.Add("HX-Request", "true");
        var parcial = await (await c.SendAsync(peticion)).Content.ReadAsStringAsync();
        var completa = await (await c.GetAsync($"/Comunion?texto=parcial{u}")).Content.ReadAsStringAsync();

        parcial.Should().Contain("Parcial" + u).And.NotContain("<html");
        completa.Should().Contain("Parcial" + u).And.Contain("<html");
    }

    [Fact]
    public async Task Parametros_invalidos_en_la_URL_no_rompen_el_listado()
    {
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var r = await c.GetAsync("/Comunion?desde=abc&hasta=-5&pagina=xyz&pendientes=quizas");

        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_menu_muestra_el_libro_de_comuniones_a_quien_puede_buscar()
    {
        var c = await app.ClienteConRolAsync(Roles.Secretaria);

        (await HtmlAsync(c, "/")).Should().Contain("/Comunion");
    }

    // ---- Asientos nuevos numerados por el sistema (libro digital; la base se comparte, se deja cerrado al terminar) ----

    private const string PaginaLibros = "/Admin/LibrosDigitales";

    private async Task CerrarLibroDigitalAsync(HttpClient admin) =>
        await AppFixture.PostFormAsync(admin, PaginaLibros, $"{PaginaLibros}?handler=Cerrar", new() { ["clave"] = "comunion" });

    private static Task<HttpResponseMessage> AbrirLibroDigitalAsync(HttpClient admin, string numero) =>
        AppFixture.PostFormAsync(admin, PaginaLibros, $"{PaginaLibros}?handler=Abrir", new()
        {
            ["clave"] = "comunion", ["numero"] = numero, ["folioInicial"] = "1", ["partidaInicial"] = "1",
            ["partidasPorFolio"] = "5", ["foliosPorLibro"] = "20", ["modoPartida"] = "Correlativa", ["partidasYaEnFolio"] = "0",
        });

    [Fact]
    public async Task Asiento_nuevo_sin_libro_digital_muestra_el_aviso()
    {
        var admin = await app.ClienteAdminAsync();
        await CerrarLibroDigitalAsync(admin);
        var c = await app.ClienteConRolAsync(Roles.Capturista);

        var html = await HtmlAsync(c, "/Comunion/Editar?modo=sistema");
        html.Should().Contain("No hay un libro digital abierto").And.NotContain("name=\"Input.Libro\"");

        var campos = Campos("", "", "", apellidos: "Zeta" + Unico());
        campos["Input.AsientoNuevo"] = "true";
        var r = await CrearRespuestaAsync(c, campos);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("libro digital abierto");
    }

    [Fact]
    public async Task Capturista_asienta_un_sacramento_nuevo_y_el_sistema_le_da_el_numero()
    {
        var admin = await app.ClienteAdminAsync();
        await CerrarLibroDigitalAsync(admin);
        var numero = "D" + Unico();
        (await AbrirLibroDigitalAsync(admin, numero)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        try
        {
            var c = await app.ClienteConRolAsync(Roles.Capturista);
            var form = await HtmlAsync(c, "/Comunion/Editar?modo=sistema");
            form.Should().Contain("Próximo número").And.Contain(numero);

            var campos = Campos("999", "999", "999", apellidos: "Zeta" + Unico());
            campos["Input.AsientoNuevo"] = "true";
            var id = await CrearAsync(c, campos);

            var detalle = await HtmlAsync(c, $"/Comunion/Detalle/{id}");
            detalle.Should().Contain("Asiento numerado por el sistema")
                .And.Contain($"libro {numero}, folio 1, partida 1");
            detalle.Should().Contain("Asiento del sistema");

            var edicion = await HtmlAsync(c, $"/Comunion/Editar/{id}");
            edicion.Should().Contain("readonly");
        }
        finally
        {
            await CerrarLibroDigitalAsync(admin);
        }
    }
}
