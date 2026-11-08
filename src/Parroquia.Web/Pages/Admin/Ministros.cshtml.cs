using Microsoft.AspNetCore.Authorization;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Catalogos;
using Parroquia.Infraestructura.Catalogos;

namespace Parroquia.Web.Pages.Admin;

[Authorize(Policy = Permisos.CatalogosGestionar)]
public class MinistrosModel(ServicioCatalogo<Ministro> servicio) : CatalogoPagina<Ministro>(servicio)
{
    public override string Titulo => "Ministros";
    public override string Descripcion =>
        "Sacerdotes que pueden figurar como ministro en los registros. En el formulario también se puede escribir otro nombre.";
    public override string EtiquetaNuevo => "Nombre del ministro (ej. P. Nombre Apellido)";
}
