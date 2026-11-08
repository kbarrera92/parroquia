using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Dominio.Comun;
using Parroquia.Dominio.Configuracion;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Configuracion;

public sealed record DatosConfiguracion(
    string? Nombre, string? Direccion, string? Municipio, string? Telefono,
    string? TextoLugarEmision, string? CiudadAbreviada);

public sealed record ArchivoLogo(byte[] Contenido, string TipoContenido);

public sealed class ServicioConfiguracionParroquia(ParroquiaDbContext db)
{
    public const int LogoMaximoBytes = 1024 * 1024;

    private static readonly byte[] FirmaPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] FirmaJpeg = [0xFF, 0xD8, 0xFF];

    /// <summary>La fila única de configuración, o null si aún no existe.</summary>
    public Task<ConfiguracionParroquia?> ObtenerAsync(CancellationToken ct = default) =>
        db.Configuraciones.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Guarda los datos (crea la fila si no existe). Un logo nuevo reemplaza al anterior;
    /// sin logo nuevo se conserva el actual salvo que <paramref name="quitarLogo"/> sea true.
    /// </summary>
    public async Task<ResultadoOperacion> GuardarAsync(
        DatosConfiguracion datos, ArchivoLogo? logo, bool quitarLogo, CancellationToken ct = default)
    {
        var error = ValidarTextos(datos, out var limpios) ?? (logo is null ? null : ValidarLogo(logo));
        if (error is not null) return ResultadoOperacion.Falla(error);

        var conf = await ObtenerAsync(ct);
        if (conf is null)
        {
            conf = new ConfiguracionParroquia();
            db.Configuraciones.Add(conf);
        }

        conf.Nombre = limpios.Nombre;
        conf.Direccion = limpios.Direccion;
        conf.Municipio = limpios.Municipio;
        conf.Telefono = limpios.Telefono;
        conf.TextoLugarEmision = limpios.TextoLugarEmision;
        conf.CiudadAbreviada = limpios.CiudadAbreviada;

        if (logo is not null)
        {
            conf.Logo = logo.Contenido;
            conf.LogoTipoContenido = logo.TipoContenido;
        }
        else if (quitarLogo)
        {
            conf.Logo = null;
            conf.LogoTipoContenido = null;
        }

        await db.SaveChangesAsync(ct);
        return ResultadoOperacion.Exito;
    }

    private static string? ValidarTextos(DatosConfiguracion d, out (string Nombre, string Direccion, string Municipio,
        string Telefono, string TextoLugarEmision, string CiudadAbreviada) limpios)
    {
        limpios = default;
        var campos = new (string? Valor, string Etiqueta, int Max)[]
        {
            (d.Nombre, "el nombre de la parroquia", 200),
            (d.Direccion, "la dirección", 200),
            (d.Municipio, "el municipio y departamento", 200),
            (d.Telefono, "el teléfono", 50),
            (d.TextoLugarEmision, "el lugar de emisión", 100),
            (d.CiudadAbreviada, "la ciudad abreviada", 100),
        };

        var valores = new string[campos.Length];
        for (var i = 0; i < campos.Length; i++)
        {
            var v = TextoLibro.Limpiar(campos[i].Valor);
            if (v is null) return $"Escriba {campos[i].Etiqueta}.";
            if (v.Length > campos[i].Max) return $"{Capitalizar(campos[i].Etiqueta)} admite máximo {campos[i].Max} caracteres.";
            valores[i] = v;
        }

        limpios = (valores[0], valores[1], valores[2], valores[3], valores[4], valores[5]);
        return null;
    }

    private static string? ValidarLogo(ArchivoLogo logo)
    {
        if (logo.Contenido.Length == 0) return "El archivo del logo está vacío.";
        if (logo.Contenido.Length > LogoMaximoBytes) return "El logo no puede pesar más de 1 MB.";

        var firma = logo.TipoContenido switch
        {
            "image/png" => FirmaPng,
            "image/jpeg" => FirmaJpeg,
            _ => null,
        };
        if (firma is null) return "El logo debe ser una imagen PNG o JPEG.";
        if (!logo.Contenido.AsSpan().StartsWith(firma)) return "El archivo no es una imagen válida del tipo indicado.";

        return null;
    }

    private static string Capitalizar(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
