using Parroquia.Dominio.Comun;

namespace Parroquia.Aplicacion.Sacramentos;

public sealed record ResumenModulo(string Clave, string Nombre, string RutaListado, int Mostrados, int Total)
{
    public bool HayMas => Total > Mostrados;
}

public sealed record ResultadoGlobal(
    bool TextoValido, IReadOnlyList<ResultadoBusquedaRegistro> Items, IReadOnlyList<ResumenModulo> Modulos)
{
    public static ResultadoGlobal Vacio(bool textoValido) => new(textoValido, [], []);
}

/// <summary>Busca una persona en todos los libros registrados y une los resultados ordenados por apellidos y nombres.</summary>
public sealed class BusquedaGlobal(IEnumerable<IModuloSacramental> modulos)
{
    public const int MinimoCaracteres = 2;
    public const int MaximoPorModuloPorDefecto = 50;

    public async Task<ResultadoGlobal> BuscarAsync(
        string? texto, int maximoPorModulo = MaximoPorModuloPorDefecto, CancellationToken ct = default)
    {
        if (NormalizadorBusqueda.Normalizar(texto).Length < MinimoCaracteres)
            return ResultadoGlobal.Vacio(false);

        var items = new List<(ResultadoBusquedaRegistro Fila, int Orden)>();
        var resumenes = new List<ResumenModulo>();

        // En serie: los módulos comparten el DbContext de la petición, que no admite consultas en paralelo.
        foreach (var modulo in modulos.OrderBy(m => m.Orden).ThenBy(m => m.Clave, StringComparer.Ordinal))
        {
            var r = await modulo.BuscarAsync(new ConsultaBusqueda(texto), maximoPorModulo, ct);
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
}
