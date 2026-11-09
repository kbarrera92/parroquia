using Parroquia.Dominio.Comun;

namespace Parroquia.Aplicacion.Sacramentos;

public sealed record ResumenModulo(string Clave, string Nombre, string RutaListado, int Mostrados, int Total)
{
    public bool HayMas => Total > Mostrados;
}

/// <summary>Texto obligatorio; el resto solo acota. <c>Sacramento</c> es la clave del módulo.</summary>
public sealed record FiltroGlobal(
    string? Texto, string? Sacramento = null, string? Libro = null, string? Folio = null,
    short? AnioDesde = null, short? AnioHasta = null);

public sealed record ResultadoGlobal(
    bool TextoValido, IReadOnlyList<ResultadoBusquedaRegistro> Items, IReadOnlyList<ResumenModulo> Modulos,
    string? Error = null)
{
    public static ResultadoGlobal Vacio(bool textoValido) => new(textoValido, [], []);
}

/// <summary>Busca una persona en todos los libros registrados y une los resultados ordenados por apellidos y nombres.</summary>
public sealed class BusquedaGlobal(IEnumerable<IModuloSacramental> modulos)
{
    public const int MinimoCaracteres = 2;
    public const int MaximoPorModuloPorDefecto = 50;
    public const short AnioMinimo = 1500;

    public Task<ResultadoGlobal> BuscarAsync(
        string? texto, int maximoPorModulo = MaximoPorModuloPorDefecto, CancellationToken ct = default) =>
        BuscarAsync(new FiltroGlobal(texto), maximoPorModulo, ct);

    public async Task<ResultadoGlobal> BuscarAsync(
        FiltroGlobal filtro, int maximoPorModulo = MaximoPorModuloPorDefecto, CancellationToken ct = default)
    {
        if (NormalizadorBusqueda.Normalizar(filtro.Texto).Length < MinimoCaracteres)
            return ResultadoGlobal.Vacio(false);

        var errorAnios = ValidarAnios(filtro.AnioDesde, filtro.AnioHasta);
        if (errorAnios is not null)
            return new ResultadoGlobal(true, [], [], errorAnios);

        var consulta = new ConsultaBusqueda(
            filtro.Texto, false,
            string.IsNullOrWhiteSpace(filtro.Libro) ? null : filtro.Libro.Trim(),
            string.IsNullOrWhiteSpace(filtro.Folio) ? null : filtro.Folio.Trim(),
            filtro.AnioDesde, filtro.AnioHasta);

        var items = new List<(ResultadoBusquedaRegistro Fila, int Orden)>();
        var resumenes = new List<ResumenModulo>();

        var elegidos = modulos
            .Where(m => string.IsNullOrWhiteSpace(filtro.Sacramento) || m.Clave == filtro.Sacramento)
            .OrderBy(m => m.Orden).ThenBy(m => m.Clave, StringComparer.Ordinal);

        // En serie: los módulos comparten el DbContext de la petición, que no admite consultas en paralelo.
        foreach (var modulo in elegidos)
        {
            var r = await modulo.BuscarAsync(consulta, maximoPorModulo, ct);
            items.AddRange(r.Items.Select(f => (f, modulo.Orden)));
            resumenes.Add(new ResumenModulo(modulo.Clave, modulo.Nombre, modulo.RutaListado, r.Items.Count, r.Total));
        }

        var ordenados = items
            .OrderBy(x => NormalizadorBusqueda.Normalizar(x.Fila.Apellidos), StringComparer.Ordinal)
            .ThenBy(x => NormalizadorBusqueda.Normalizar(x.Fila.Nombres), StringComparer.Ordinal)
            .ThenBy(x => x.Orden)
            .Select(x => x.Fila)
            .ToList();

        return new ResultadoGlobal(true, ordenados, resumenes);
    }

    private static string? ValidarAnios(short? desde, short? hasta)
    {
        var maximo = (short)DateTime.UtcNow.Year;
        foreach (var a in new[] { desde, hasta })
            if (a is { } v && (v < AnioMinimo || v > maximo))
                return $"El año debe estar entre {AnioMinimo} y {maximo}.";
        if (desde is { } d && hasta is { } h && d > h)
            return "El año «desde» no puede ser mayor que el año «hasta».";
        return null;
    }
}
