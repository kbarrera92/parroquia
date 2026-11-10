using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Comun;
using Parroquia.Infraestructura.Catalogos;
using Parroquia.Infraestructura.Numeracion;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Web.Pages.Matrimonio;

/// <summary>Un contrayente en el formulario. Todo es opcional a nivel de binding: las reglas viven en el servicio.</summary>
public class EntradaContrayente
{
    public string? Tratamiento { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? Edad { get; set; }
    public string? OriginarioDe { get; set; }
    public string? FeligresDe { get; set; }
    public string? Padre { get; set; }
    public string? Madre { get; set; }

    public DatosContrayenteFormulario ADatos() =>
        new(Tratamiento, Nombres, Apellidos, Edad, OriginarioDe, FeligresDe, Padre, Madre);
}

/// <summary>Lo que viaja en el formulario.</summary>
public class EntradaMatrimonio
{
    public string? Libro { get; set; }
    public string? Folio { get; set; }
    public string? Partida { get; set; }
    public EntradaContrayente Esposo { get; set; } = new();
    public EntradaContrayente Esposa { get; set; } = new();
    public string? FechaMatrimonio { get; set; }
    public string? LugarCelebracion { get; set; }

    /// <summary>Un campo por testigo, todos con el mismo nombre; el orden es el del formulario.</summary>
    public List<string?> Testigos { get; set; } = [];

    public string? Ministro { get; set; }
    public string? NotasMarginales { get; set; }
    public bool Revisar { get; set; }
    public string? MotivoRevision { get; set; }

    /// <summary>Alta de un sacramento nuevo: el sistema numera (libro/folio/partida); false = transcribir de un libro físico.</summary>
    public bool AsientoNuevo { get; set; }

    /// <summary>Versión (xmin) que vio el usuario al abrir el registro; detecta ediciones simultáneas.</summary>
    public uint Version { get; set; }

    public DatosMatrimonioFormulario ADatos() =>
        new(Libro, Folio, Partida, Esposo.ADatos(), Esposa.ADatos(), FechaMatrimonio, LugarCelebracion, Testigos,
            Ministro, NotasMarginales, Revisar, MotivoRevision, AsientoNuevo);
}

[Authorize(Policy = Permisos.RegistrosVer)]
public class EditarModel(
    ServicioMatrimonio servicio,
    ServicioNumeracion numeracion,
    ServicioCatalogo<Ministro> ministros,
    ServicioCatalogo<Tratamiento> tratamientos,
    IAuthorizationService autorizacion) : PageModel
{
    private const string AccionNuevo = "nuevo";
    private const string SufijoForzar = "-forzar";
    private const string ModoSistema = "sistema";

    /// <summary>Filas de testigo con que arranca un registro nuevo (lo habitual son dos).</summary>
    private const int TestigosIniciales = 2;

    /// <summary>Tratamientos que se sugieren en un registro nuevo si existen en el catálogo.</summary>
    private const string TratamientoEsposoPorDefecto = "don";
    private const string TratamientoEsposaPorDefecto = "la Srita.";

    [BindProperty] public EntradaMatrimonio Input { get; set; } = new();

    public long? Id { get; private set; }
    public bool EsNuevo => Id is null;
    public IReadOnlyList<string> Ministros { get; private set; } = [];
    public IReadOnlyList<string> Tratamientos { get; private set; } = [];
    public IReadOnlyList<RegistroDuplicado> Duplicados { get; private set; } = [];

    /// <summary>Botón con el que se intentó guardar; el aviso de duplicados lo repite con "de todos modos".</summary>
    public string AccionPrevia { get; private set; } = "guardar";

    /// <summary>Cuando se abre desde "Guardar y nuevo", el foco empieza en los nombres del esposo (Libro/Folio/Partida ya vienen).</summary>
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
        await CargarCatalogosAsync(ct);

        if (id is null)
        {
            Input = new EntradaMatrimonio
            {
                Libro = libro, Folio = folio, Partida = partida, AsientoNuevo = asiento,
                Esposo = new EntradaContrayente { Tratamiento = Sugerido(TratamientoEsposoPorDefecto) },
                Esposa = new EntradaContrayente { Tratamiento = Sugerido(TratamientoEsposaPorDefecto) },
                LugarCelebracion = PlantillaMatrimonio.LugarPorDefecto,
                Testigos = [.. Enumerable.Repeat<string?>(null, TestigosIniciales)],
            };
            ContinuaSerie = asiento || !string.IsNullOrWhiteSpace(libro);
        }
        else
        {
            var v = await servicio.ObtenerVistaAsync(id.Value, ct: ct);
            if (v is null) return NotFound();

            Input = new EntradaMatrimonio
            {
                Libro = v.Libro, Folio = v.Folio, Partida = v.Partida,
                Esposo = Entrada(v.Esposo), Esposa = Entrada(v.Esposa),
                FechaMatrimonio = v.FechaMatrimonioCaptura, LugarCelebracion = v.LugarCelebracion,
                Testigos = [.. v.Testigos], Ministro = v.Ministro, NotasMarginales = v.NotasMarginales,
                Revisar = v.Revisar, MotivoRevision = v.MotivoRevision, Version = v.Version,
            };
        }

        await CargarNumeracionAsync(ct);
        return Page();
    }

    /// <summary>Fragmento htmx: una fila vacía para agregar otro testigo sin recargar la página.</summary>
    public async Task<IActionResult> OnGetFilaTestigoAsync()
    {
        var puede = (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosCrear)).Succeeded
            || (await autorizacion.AuthorizeAsync(User, Permisos.RegistrosEditar)).Succeeded;
        return puede ? Partial("_FilaTestigo", (string?)null) : Forbid();
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
                TempData["Mensaje"] = $"Se guardó el matrimonio de {Resumen()}.";
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

        await CargarCatalogosAsync(ct);
        await CargarNumeracionAsync(ct);
        return Page();
    }

    /// <summary>
    /// Opciones del tratamiento: los activos del catálogo y, si el registro trae uno que ya no está
    /// (inactivo o escrito antes), también ese para no perderlo al guardar.
    /// </summary>
    public List<SelectListItem> OpcionesTratamiento(string? actual)
    {
        var textos = Tratamientos.ToList();
        if (TextoLibro.Limpiar(actual) is { } a
            && !textos.Contains(a, StringComparer.OrdinalIgnoreCase))
            textos.Add(a);
        return [.. textos.Select(t => new SelectListItem(t, t))];
    }

    private string Resumen()
    {
        static string N(EntradaContrayente c) => $"{c.Nombres} {c.Apellidos}".Trim();
        var (a, b) = (N(Input.Esposo), N(Input.Esposa));
        return $"{(a.Length == 0 ? "(sin nombre)" : a)} y {(b.Length == 0 ? "(sin nombre)" : b)}";
    }

    private static EntradaContrayente Entrada(VistaContrayente c) => new()
    {
        Tratamiento = c.Tratamiento, Nombres = c.Nombres, Apellidos = c.Apellidos,
        Edad = c.Edad?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        OriginarioDe = c.OriginarioDe, FeligresDe = c.FeligresDe, Padre = c.Padre, Madre = c.Madre,
    };

    private string? Sugerido(string texto) =>
        Tratamientos.FirstOrDefault(t => string.Equals(t, texto, StringComparison.OrdinalIgnoreCase));

    private async Task CargarNumeracionAsync(CancellationToken ct)
    {
        if (Id is null && Input.AsientoNuevo)
            Proximo = await numeracion.ProximoAsync(ServicioMatrimonio.Clave, ct);
        else if (Id is not null)
            AsientoDelSistema = (await servicio.ObtenerVistaAsync(Id.Value, ct: ct))?.AsientoDelSistema == true;
    }

    private async Task<bool> PuedeAsync(long? id) =>
        (await autorizacion.AuthorizeAsync(User, id is null ? Permisos.RegistrosCrear : Permisos.RegistrosEditar)).Succeeded;

    private async Task CargarCatalogosAsync(CancellationToken ct)
    {
        Ministros = [.. (await ministros.ListarAsync(ct)).Where(m => m.Activo).Select(m => m.Nombre)];
        Tratamientos = [.. (await tratamientos.ListarAsync(ct)).Where(t => t.Activo).Select(t => t.Texto)];
    }
}
