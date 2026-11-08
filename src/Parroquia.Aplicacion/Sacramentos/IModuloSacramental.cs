using Parroquia.Aplicacion.Constancias;

namespace Parroquia.Aplicacion.Sacramentos;

/// <param name="Texto">Lo que escribió el usuario; cada módulo lo normaliza con <c>NormalizadorBusqueda</c>.</param>
public sealed record ConsultaBusqueda(string? Texto, bool IncluirAnulados = false);

/// <summary>Una persona en un libro. Matrimonio aportará dos filas (una por contrayente) apuntando al mismo registro.</summary>
public sealed record ResultadoBusquedaRegistro(
    string Clave, string Sacramento, long Id, string Nombres, string Apellidos, string Fecha,
    string Libro, string Folio, string Partida, bool Revisar, bool Anulado, string RutaDetalle);

public sealed record ResultadosModulo(IReadOnlyList<ResultadoBusquedaRegistro> Items, int Total);

/// <summary>Constancia armada para un registro: el documento a imprimir y los avisos que se muestran antes de imprimir.</summary>
public sealed record ConstanciaPreparada(DocumentoConstancia Documento, IReadOnlyList<string> Advertencias);

/// <summary>
/// Un sacramento = un módulo autocontenido. Se registra con <c>AddModuloSacramental&lt;T&gt;()</c> y el menú,
/// la búsqueda global y la bitácora lo descubren sin tocar código de otros módulos (CLAUDE.md regla 8).
/// </summary>
public interface IModuloSacramental
{
    /// <summary>Identificador estable en minúsculas ("comunion"); se guarda en <c>ConstanciaEmitida.TipoSacramento</c>.</summary>
    string Clave { get; }

    /// <summary>Nombre para el menú y los resultados ("Primera Comunión").</summary>
    string Nombre { get; }

    /// <summary>Nombre de la entidad en <c>Auditoria</c> y <c>BitacoraAcceso</c> ("Comunion").</summary>
    string Entidad { get; }

    /// <summary>Posición en el menú y desempate en resultados.</summary>
    int Orden { get; }

    /// <summary>URL del listado del libro, p. ej. "/Comunion".</summary>
    string RutaListado { get; }

    string RutaDetalle(long id);

    /// <summary>Busca por nombres y apellidos (parcial, sin tildes). Devuelve hasta <paramref name="maximo"/> filas y el total.</summary>
    Task<ResultadosModulo> BuscarAsync(ConsultaBusqueda consulta, int maximo, CancellationToken ct = default);

    /// <summary>
    /// Arma la constancia del registro con la plantilla del módulo (función pura sobre los datos del libro).
    /// Null si el registro no existe o está anulado.
    /// </summary>
    Task<ConstanciaPreparada?> PrepararConstanciaAsync(long id, ContextoConstancia contexto, CancellationToken ct = default);
}
