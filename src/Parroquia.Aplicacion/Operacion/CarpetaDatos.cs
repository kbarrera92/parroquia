namespace Parroquia.Aplicacion.Operacion;

/// <summary>
/// Carpeta de datos de la aplicación en el servidor (logs, claves de Data Protection, respaldos, marcadores).
/// Producción: <c>C:\ProgramData\Parroquia</c> (configurable con <c>Parroquia:CarpetaDatos</c>); desarrollo: <c>datos-dev</c>.
/// </summary>
public sealed record CarpetaDatos(string Ruta)
{
    public string Logs => Path.Combine(Ruta, "logs");
    public string Claves => Path.Combine(Ruta, "claves");
    public string Respaldos => Path.Combine(Ruta, "respaldos");
}
