using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Comun;
using Parroquia.Infraestructura.Catalogos;
using Parroquia.Infraestructura.Numeracion;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Web.Pages.Bautismo;

/// <summary>Lo que viaja en el formulario. Todo es opcional a nivel de binding: las reglas viven en el servicio.</summary>
public class EntradaBautismo
{
    public string? Libro { get; set; }
    public string? Folio { get; set; }
    public string? Partida { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? FechaBautismo { get; set; }
    public string? FechaNacimiento { get; set; }
    public string? LugarNacimiento { get; set; }
    public string? Padre { get; set; }
    public string? Madre { get; set; }

    /// <summary>Un campo por padrino, todos con el mismo nombre; el orden es el del formulario.</summary>
    public List<string?> Padrinos { get; set; } = [];

    public string? Ministro { get; set; }
    public string? NotasMarginales { get; set; }
    public bool Revisar { get; set; }
    public string? MotivoRevision { get; set; }

    /// <summary>Alta de un sacramento nuevo: el sistema numera (libro/folio/partida); false = transcribir de un libro físico.</summary>
    public bool AsientoNuevo { get; set; }

    /// <summary>Versión (xmin) que vio el usuario al abrir el registro; detecta ediciones simultáneas.</summary>
    public uint Version { get; set; }

    public DatosBautismoFormulario ADatos() =>
        new(Libro, Folio, Partida, Nombres, Apellidos, FechaBautismo, FechaNacimiento, LugarNacimiento,
            Padre, Madre, Padrinos, Ministro, NotasMarginales, Revisar, MotivoRevision, AsientoNuevo);
}

[Authorize(Policy = Permisos.RegistrosVer)]
public class EditarModel(
    ServicioBautismo servicio,
    ServicioNumeracion numeracion,
    ServicioCatalogo<Ministro> ministros,
    IAuthorizationService autorizacion) : PageModel
{
    private const string AccionNuevo = "nuevo";
    private const string SufijoForzar = "-forzar";
    private const string ModoSistema = "sistema";

    /// <summary>Filas de padrino con que arranca un registro nuevo (lo habitual es uno o dos).</summary>
    private const int PadrinosIniciales = 2;

    [BindProperty] public EntradaBautismo Input { get; set; } = new();

    public long? Id { get; private set; }
    public bool EsNuevo => Id is null;
    public IReadOnlyList<string> Ministros { get; private set; } = [];
    public IReadOnlyList<RegistroDuplicado> Duplicados { get; private set; } = [];

    /// <summary>Botón con el que se intentó guardar; el aviso de duplicados lo repite con "de todos modos".</summary>
    public string AccionPrevia { get; private set; } = "guardar";

    /// <summary>Cuando se abre desde "Guardar y nuevo", el foco empieza en Nombres (Libro/Folio/Partida ya vienen).</summary>
    public bool ContinuaSerie { get; private set; }
    public string? Mensaje => TempData["Mensaje"] as string;
    public string? Error { get; private set; }

    /// <summary>Número que recibiría el próximo asiento nuevo (orientativo); null si no hay libro digital abierto.</summary>
    public ProximoNumero? Proximo { get; private set; }

    /// <summary>El registro que se edita lo numeró el sistema: libro/folio/partida son de solo lectura.</summary>
    public bool AsientoDelSistema { get; private set; }

    public async Task<IActionResult> OnGetAsync(long? id, string? libro, string? folio, string? partida, string? modo, CancellationToken ct)
    {
        if (!await PuedeAsync(id)) return Forbid();
        Id = id;
        var asiento = id is null && modo == ModoSistema;

        if (id is null)
        {
            Input = new EntradaBautismo
            {
                Libro = libro, Folio = folio, Partida = partida, AsientoNuevo = asiento,
                Padrinos = [.. Enumerable.Repeat<string?>(null, PadrinosIniciales)],
            };
            ContinuaSerie = asiento || !string.IsNullOrWhiteSpace(libro);
        }
        else
        {
            var v = await servicio.ObtenerVistaAsync(id.Value, ct: ct);
            if (v is null) return NotFound();

            Input = new EntradaBautismo
            {
                Libro = v.Libro, Folio = v.Folio, Partida = v.Partida, Nombres = v.Nombres, Apellidos = v.Apellidos,
                FechaBautismo = v.FechaBautismoCaptura, FechaNacimiento = v.FechaNacimientoCaptura,
                LugarNacimiento = v.LugarNacimiento, Padre = v.Padre, Madre = v.Madre,
                Padrinos = [.. v.Padrinos], Ministro = v.Ministro, NotasMarginales = v.NotasMarginales,
                Revisar = v.Revisar, MotivoRevision = v.MotivoRevision, Version = v.Version,
            };
        }

        await CargarMinistrosAsync(ct);
        await CargarNumeracionAsync(ct);
        return Page();
    }

    /// <summary>Fragmento htmx: una fila vacía para agregar otro padrino sin recargar la página.</summary>
    public async Task<IActionResult> OnGetFilaPadrinoAsync()
    {
        var puede = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosCrear)).Succeeded
            || (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosEditar)).Succeeded;
        return puede ? Partial("_FilaPadrino", (string?)null) : Forbid();
    }

    public async Task<IActionResult> OnPostAsync(long? id, string? accion, CancellationToken ct)
    {
        if (!await PuedeAsync(id)) return Forbid();
        Id = id;
        AccionPrevia = (accion ?? "guardar").Replace(SufijoForzar, "");
        var forzar = accion?.EndsWith(SufijoForzar, StringComparison.Ordinal) == true;

        var r = await servicio.GuardarAsync(Input.ADatos(), id, id is null ? null : Input.Version, forzar, ct);
        switch (r.Estado)
        {
            case EstadoGuardado.Ok:
                var nombre = $"{Input.Nombres} {Input.Apellidos}".Trim();
                TempData["Mensaje"] = $"Se guardó el registro de {(nombre.Length == 0 ? "(sin nombre)" : nombre)}.";
                var asientoGuardado = id is null && Input.AsientoNuevo;
                if (asientoGuardado && await servicio.ObtenerVistaAsync(r.Id, ct: ct) is { } v)
                    TempData["Mensaje"] += $" Asiento numerado por el sistema: libro {v.Libro}, folio {v.Folio}, partida {v.Partida}.";

                if (id is null && AccionPrevia == AccionNuevo)
                    return asientoGuardado
                        ? RedirectToPage("Editar", new { modo = ModoSistema })
                        : RedirectToPage("Editar", new
                        {
                            libro = TextoLibro.Limpiar(Input.Libro),
                            folio = NumeroSiguiente.De(Input.Folio),
                            partida = NumeroSiguiente.De(Input.Partida),
                        });
                return RedirectToPage("Detalle", new { id = r.Id });

            case EstadoGuardado.NoExiste:
                return NotFound();

            case EstadoGuardado.Anulado:
                TempData["Mensaje"] = "El registro está anulado y no se puede editar.";
                return RedirectToPage("Detalle", new { id });

            case EstadoGuardado.Invalido:
                foreach (var e in r.Errores!) ModelState.AddModelError($"Input.{e.Campo}", e.Mensaje);
                break;

            case EstadoGuardado.Duplicados:
                Duplicados = r.Duplicados!;
                break;

            case EstadoGuardado.Conflicto:
                Error = "Otro usuario modificó este registro mientras usted lo editaba. Lo que digitó sigue abajo, " +
                        "pero no se guardó: abra el registro actualizado y aplique sus cambios de nuevo.";
                break;
        }

        await CargarMinistrosAsync(ct);
        await CargarNumeracionAsync(ct);
        return Page();
    }

    private async Task CargarNumeracionAsync(CancellationToken ct)
    {
        if (Id is null && Input.AsientoNuevo)
            Proximo = await numeracion.ProximoAsync(ServicioBautismo.Clave, ct);
        else if (Id is not null)
            AsientoDelSistema = (await servicio.ObtenerVistaAsync(Id.Value, ct: ct))?.AsientoDelSistema == true;
    }

    private async Task<bool> PuedeAsync(long? id) =>
        (await autorizacion.AuthorizeAsync(User, id is null ? Permisos.RegistrosCrear : Permisos.RegistrosEditar)).Succeeded;

    private async Task CargarMinistrosAsync(CancellationToken ct) =>
        Ministros = [.. (await ministros.ListarAsync(ct)).Where(m => m.Activo).Select(m => m.Nombre)];
}
