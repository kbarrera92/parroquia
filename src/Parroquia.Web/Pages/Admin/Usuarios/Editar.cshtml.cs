using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Web.Pages.Admin.Usuarios;

[Authorize(Policy = Permisos.UsuariosGestionar)]
public class EditarModel(ServicioUsuarios servicio, IUsuarioActual actual) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Id { get; set; }

    // Nullable a propósito: MVC trata un string no nulo como [Required] implícito y en edición el usuario no se envía.
    // Las reglas y mensajes los da ServicioUsuarios.
    [BindProperty, MaxLength(50)] public string? Usuario { get; set; }
    [BindProperty, MaxLength(ServicioUsuarios.LongitudNombreMaxima)] public string? NombreCompleto { get; set; }
    [BindProperty] public string? Rol { get; set; } = Roles.Capturista;

    public bool EsNuevo => string.IsNullOrEmpty(Id);
    public bool EsUnoMismo => !EsNuevo && Id == actual.Id;

    public async Task<IActionResult> OnGetAsync()
    {
        if (EsNuevo) return Page();

        var fila = await servicio.ObtenerAsync(Id!);
        if (fila is null) return NotFound();

        Usuario = fila.Usuario;
        NombreCompleto = fila.NombreCompleto;
        Rol = fila.Rol;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return await Recargar();

        if (EsNuevo)
        {
            var r = await servicio.CrearAsync(Usuario, NombreCompleto, Rol);
            if (!r.Ok) return await Recargar(r.Error);

            TempData["Temporal"] = r.Temporal;
            TempData["TemporalUsuario"] = (Usuario ?? "").Trim().ToLowerInvariant();
            TempData["Mensaje"] = "Usuario creado.";
            return RedirectToPage("Index");
        }

        var e = await servicio.EditarAsync(Id!, NombreCompleto, Rol);
        if (!e.Ok) return await Recargar(e.Error);

        TempData["Mensaje"] = "Usuario actualizado.";
        return RedirectToPage("Index");
    }

    // En edición el usuario no viaja como campo editable: se vuelve a leer para mostrarlo.
    private async Task<IActionResult> Recargar(string? error = null)
    {
        if (error is not null) ModelState.AddModelError("", error);
        if (!EsNuevo && await servicio.ObtenerAsync(Id!) is { } fila) Usuario = fila.Usuario;
        return Page();
    }
}
