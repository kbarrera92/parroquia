using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Vinculos;
using Parroquia.Infraestructura.Vinculos;

namespace Parroquia.Web.Pages.Vinculos;

/// <summary>
/// Fragmento htmx "Misma persona en otros libros", común a todos los sacramentos: se carga desde el detalle de cada registro
/// (<c>/Vinculos/{clave}/{id}/{persona?}</c>). Ver exige <c>Registros.Ver</c>; vincular y desvincular, <c>Registros.Editar</c>.
/// </summary>
[Authorize(Policy = Permisos.RegistrosVer)]
public class IndexModel(
    ServicioVinculos servicio,
    BusquedaGlobal busqueda,
    IEnumerable<IModuloSacramental> modulos,
    IAuthorizationService autorizacion) : PageModel
{
    public ExtremoVinculo Extremo { get; private set; }
    public bool PuedeEditar { get; private set; }
    public IReadOnlyList<PersonaVinculada> Cadena { get; private set; } = [];
    public IReadOnlyList<ResultadoBusquedaRegistro> Candidatos { get; private set; } = [];
    public string? Texto { get; private set; }
    public string? Mensaje { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Identificador HTML estable de la sección de esta persona.</summary>
    public string IdSeccion => $"vinculos-{Extremo.Tipo}-{Extremo.Id}-{(Extremo.Persona.Length == 0 ? "x" : Extremo.Persona)}";

    public string EtiquetaPersona => Extremo.Persona switch { "esposo" => "esposo", "esposa" => "esposa", _ => "" };

    public static string Etiqueta(string persona) => persona switch { "esposo" => " (esposo)", "esposa" => " (esposa)", _ => "" };

    public async Task<IActionResult> OnGetAsync(string clave, long id, string? persona, CancellationToken ct)
    {
        var inicio = await IniciarAsync(clave, id, persona, ct);
        if (inicio is not null) return inicio;

        await CargarAsync(ct);
        return Page();
    }

    /// <summary>Candidatos para vincular: personas de cualquier libro que coinciden con el texto, menos esta y las ya vinculadas.</summary>
    public async Task<IActionResult> OnGetBuscarAsync(string clave, long id, string? persona, string? texto, CancellationToken ct)
    {
        var inicio = await IniciarAsync(clave, id, persona, ct);
        if (inicio is not null) return inicio;
        if (!PuedeEditar) return Forbid();

        var cadena = await servicio.CadenaAsync(Extremo, ct);
        var r = await busqueda.BuscarAsync(texto, ct: ct);
        Texto = r.TextoValido ? texto : null;
        Error = r.Error;
        Candidatos =
        [
            .. r.Items
                .Where(f => new ExtremoVinculo(f.Clave, f.Id, f.Persona) != Extremo)
                .Where(f => cadena.All(p => p.Extremo != new ExtremoVinculo(f.Clave, f.Id, f.Persona)))
                .Take(30),
        ];
        return Partial("_Candidatos", this);
    }

    public async Task<IActionResult> OnPostVincularAsync(
        string clave, long id, string? persona, string tipo, long idDestino, string? personaDestino, CancellationToken ct)
    {
        var inicio = await IniciarAsync(clave, id, persona, ct);
        if (inicio is not null) return inicio;
        if (!PuedeEditar) return Forbid();

        var r = await servicio.VincularAsync(Extremo, new ExtremoVinculo(tipo, idDestino, personaDestino ?? ""), ct);
        if (r.Ok) Mensaje = "Registros vinculados."; else Error = r.Error;

        await CargarAsync(ct);
        return Partial("_Seccion", this);
    }

    public async Task<IActionResult> OnPostDesvincularAsync(
        string clave, long id, string? persona, long vinculoId, CancellationToken ct)
    {
        var inicio = await IniciarAsync(clave, id, persona, ct);
        if (inicio is not null) return inicio;
        if (!PuedeEditar) return Forbid();

        // Solo se puede quitar un vínculo directo de esta persona.
        var directos = await servicio.CadenaAsync(Extremo, ct);
        if (directos.All(p => p.VinculoId != vinculoId))
        {
            Error = "Ese vínculo ya no existe.";
        }
        else
        {
            var r = await servicio.DesvincularAsync(vinculoId, ct: ct);
            if (r.Ok) Mensaje = "Vínculo quitado. Los registros no se borraron."; else Error = r.Error;
        }

        await CargarAsync(ct);
        return Partial("_Seccion", this);
    }

    /// <summary>Null si todo es válido; si no, el resultado a devolver (404).</summary>
    private async Task<IActionResult?> IniciarAsync(string clave, long id, string? persona, CancellationToken ct)
    {
        var modulo = modulos.FirstOrDefault(m => m.Clave == clave);
        persona ??= "";
        if (modulo is null || !modulo.Personas.Contains(persona)) return NotFound();

        Extremo = new ExtremoVinculo(clave, id, persona);
        if (await servicio.ResumenAsync(Extremo, ct) is null) return NotFound();

        PuedeEditar = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosEditar)).Succeeded;
        return null;
    }

    private async Task CargarAsync(CancellationToken ct) => Cadena = await servicio.CadenaAsync(Extremo, ct);

    public string Ruta(string? handler = null) => Url.Page("/Vinculos/Index", handler,
        new { clave = Extremo.Tipo, id = Extremo.Id, persona = Extremo.Persona.Length == 0 ? null : Extremo.Persona })!;
}
