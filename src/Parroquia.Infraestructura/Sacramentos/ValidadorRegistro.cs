using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Comun;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Error de validación asociado al nombre del campo del formulario.</summary>
public sealed record ErrorCampo(string Campo, string Mensaje);

public enum EstadoGuardado
{
    Ok,
    Invalido,

    /// <summary>Hay registros parecidos; no se guardó. Repetir con la confirmación del usuario.</summary>
    Duplicados,

    /// <summary>Otro usuario modificó el registro mientras se editaba.</summary>
    Conflicto,
    NoExiste,
    Anulado,
}

public sealed record RegistroDuplicado(long Id, string Nombre, string Libro, string Folio, string Partida, string Fecha);

public sealed record ResultadoGuardado(
    EstadoGuardado Estado,
    long Id = 0,
    IReadOnlyList<ErrorCampo>? Errores = null,
    IReadOnlyList<RegistroDuplicado>? Duplicados = null)
{
    public bool Ok => Estado == EstadoGuardado.Ok;
}

/// <summary>Validaciones y límites comunes a todos los sacramentos (docs/02 §6).</summary>
public static class ValidadorRegistro
{
    public const int MaxClave = 30;
    /// <summary>Peor caso de <see cref="ClaveOrdenNatural"/> con 30 caracteres alternando dígito y letra: 15×(10+1).</summary>
    public const int MaxOrden = 200;
    public const int MaxNombre = 150;
    public const int MaxBusqueda = 400;
    public const int MaxMotivo = 500;
    public const int MaxNotas = 2000;

    /// <summary>Texto limpio (espacios recortados). Agrega un error si es obligatorio y falta, o si excede <paramref name="max"/>.</summary>
    public static string? Texto(ICollection<ErrorCampo> errores, string campo, string? valor, string etiqueta,
        int max, bool obligatorio)
    {
        var limpio = TextoLibro.Limpiar(valor);
        if (limpio is null)
        {
            if (obligatorio) errores.Add(new ErrorCampo(campo, $"Escriba {etiqueta}."));
            return null;
        }

        if (limpio.Length > max)
        {
            errores.Add(new ErrorCampo(campo, $"{Capitalizar(etiqueta)} admite máximo {max} caracteres."));
            return null;
        }
        return limpio;
    }

    /// <summary>Notas marginales: texto libre; respeta saltos de línea, solo recorta los extremos.</summary>
    public static string? Notas(ICollection<ErrorCampo> errores, string campo, string? valor)
    {
        var t = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        if (t is { Length: > MaxNotas })
        {
            errores.Add(new ErrorCampo(campo, $"Las notas admiten máximo {MaxNotas} caracteres."));
            return null;
        }
        return t;
    }

    /// <summary>
    /// Si el texto coincide (sin tildes ni mayúsculas) con un ministro activo del catálogo se usa ese;
    /// si no, queda como texto libre. Sin texto, ninguno.
    /// </summary>
    public static async Task<(long? Id, string? Texto)> ResolverMinistroAsync(
        ParroquiaDbContext db, string? texto, CancellationToken ct)
    {
        if (texto is null) return (null, null);

        var clave = NormalizadorBusqueda.Normalizar(texto);
        var activos = await db.Ministros.AsNoTracking().Where(m => m.Activo).ToListAsync(ct);
        var coincide = clave.Length == 0
            ? null
            : activos.FirstOrDefault(m => NormalizadorBusqueda.Normalizar(m.Nombre) == clave);

        return coincide is null ? (null, texto) : (coincide.Id, null);
    }

    private static string Capitalizar(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
