using Microsoft.AspNetCore.Authorization;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Catalogos;
using Parroquia.Infraestructura.Catalogos;

namespace Parroquia.Web.Pages.Admin;

[Authorize(Policy = Permisos.CatalogosGestionar)]
public class TratamientosModel(ServicioCatalogo<Tratamiento> servicio) : CatalogoPagina<Tratamiento>(servicio)
{
    public override string Titulo => "Tratamientos";
    public override string Descripcion =>
        "Tratamiento de los contrayentes en la constancia de matrimonio (por ejemplo: don, la Srita., la Sra.).";
    public override string EtiquetaNuevo => "Tratamiento (tal como se imprime)";
}
