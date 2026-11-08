using Parroquia.Dominio.Comun;

namespace Parroquia.Dominio.Configuracion;

/// <summary>Datos del membrete de las constancias. Una sola fila; se edita desde Administración, no en código.</summary>
public class ConfiguracionParroquia : EntidadAuditable
{
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Municipio { get; set; } = "";
    public string Telefono { get; set; } = "";

    /// <summary>Lugar en el cierre de la constancia, p. ej. "Mazatenango, Such.".</summary>
    public string TextoLugarEmision { get; set; } = "";
    public string CiudadAbreviada { get; set; } = "";

    /// <summary>Imagen PNG o JPEG (≤ 1 MB) guardada en la base de datos: un solo respaldo lo cubre todo.</summary>
    public byte[]? Logo { get; set; }
    public string? LogoTipoContenido { get; set; }
}

/// <summary>
/// Párroco que firma las constancias. Toda constancia, incluso la reimpresión de un registro antiguo,
/// lleva al párroco vigente al emitir (<see cref="EsActual"/>).
/// </summary>
public class ParrocoVigente : EntidadAuditable
{
    public string Nombre { get; set; } = "";
    public string Titulo { get; set; } = "Párroco";
    public DateOnly VigenteDesde { get; set; }
    public DateOnly? VigenteHasta { get; set; }
    public bool EsActual { get; set; }
}
