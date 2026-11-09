using Microsoft.EntityFrameworkCore;
using Parroquia.Aplicacion.Comun;
using Parroquia.Aplicacion.Sacramentos;
using Parroquia.Dominio.Vinculos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Vinculos;

/// <summary>Otra aparición de la misma persona. <c>VinculoId</c> solo si el enlace con el extremo consultado es directo.</summary>
public sealed record PersonaVinculada(
    ExtremoVinculo Extremo, ResultadoBusquedaRegistro Resumen, bool Directo, long? VinculoId, string? PorMedioDe);

/// <summary>
/// Vínculos opcionales "misma persona" entre registros (RF-07). Los vínculos forman cadenas: si A↔B y B↔C, desde A se ve C
/// "por medio de B". Desvincular anula (con historial), nunca borra. Quien llama exige <c>Registros.Editar</c>.
/// </summary>
public sealed class ServicioVinculos(ParroquiaDbContext db, IEnumerable<IModuloSacramental> modulos)
{
    /// <summary>Tope de personas que se recorren en una cadena (evita recorridos enormes por datos mal vinculados).</summary>
    public const int MaxCadena = 50;

    private IModuloSacramental? Modulo(string tipo) => modulos.FirstOrDefault(m => m.Clave == tipo);

    private static string Clave(ExtremoVinculo e) => $"{e.Tipo}|{e.Id}|{e.Persona}";

    public async Task<ResultadoOperacion> VincularAsync(ExtremoVinculo a, ExtremoVinculo b, CancellationToken ct = default)
    {
        if (a == b) return ResultadoOperacion.Falla("No se puede vincular un registro consigo mismo.");

        foreach (var extremo in new[] { a, b })
        {
            var error = await ValidarExtremoAsync(extremo, ct);
            if (error is not null) return ResultadoOperacion.Falla(error);
        }

        var (menor, mayor) = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
        var existente = await db.Vinculos.IgnoreQueryFilters().FirstOrDefaultAsync(v =>
            v.TipoOrigen == menor.Tipo && v.IdOrigen == menor.Id && v.PersonaOrigen == menor.Persona
            && v.TipoDestino == mayor.Tipo && v.IdDestino == mayor.Id && v.PersonaDestino == mayor.Persona, ct);

        if (existente is { Anulado: false })
            return ResultadoOperacion.Falla("Esos registros ya están vinculados.");

        if ((await CadenaAsync(a, ct)).Any(p => p.Extremo == b))
            return ResultadoOperacion.Falla("Esos registros ya están vinculados por medio de otro registro.");

        if (existente is not null)
        {
            existente.Anulado = false;
            existente.MotivoAnulacion = null;
        }
        else
        {
            var nuevo = new Vinculo();
            nuevo.Establecer(a, b);
            db.Vinculos.Add(nuevo);
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return ResultadoOperacion.Exito;
        }
        catch (DbUpdateException)
        {
            return ResultadoOperacion.Falla("Otro usuario acaba de vincular esos registros. Actualice la página.");
        }
    }

    public async Task<ResultadoOperacion> DesvincularAsync(long vinculoId, string? motivo = null, CancellationToken ct = default)
    {
        var v = await db.Vinculos.FirstOrDefaultAsync(x => x.Id == vinculoId, ct);
        if (v is null) return ResultadoOperacion.Falla("El vínculo ya no existe.");

        v.Anulado = true;
        v.MotivoAnulacion = string.IsNullOrWhiteSpace(motivo) ? "Desvinculado" : motivo.Trim();
        try
        {
            await db.SaveChangesAsync(ct);
            return ResultadoOperacion.Exito;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResultadoOperacion.Falla("Otro usuario modificó el vínculo. Actualice la página.");
        }
    }

    /// <summary>
    /// Todas las personas conectadas con <paramref name="inicio"/> (directas primero, luego las de "por medio de"),
    /// hasta <see cref="MaxCadena"/>. Los registros anulados o inexistentes se recorren pero no se muestran.
    /// </summary>
    public async Task<IReadOnlyList<PersonaVinculada>> CadenaAsync(ExtremoVinculo inicio, CancellationToken ct = default)
    {
        // Para cada persona alcanzada: de quién viene y el id del enlace (solo importa el de nivel 1).
        var alcanzados = new Dictionary<ExtremoVinculo, (ExtremoVinculo? Previo, long? VinculoId, int Nivel)>
        {
            [inicio] = (null, null, 0),
        };
        var frontera = new List<ExtremoVinculo> { inicio };
        var nivel = 0;

        while (frontera.Count > 0 && alcanzados.Count <= MaxCadena)
        {
            nivel++;
            var claves = frontera.Select(Clave).ToArray();
            var enlaces = await db.Vinculos.AsNoTracking()
                .Where(v => claves.Contains(v.TipoOrigen + "|" + v.IdOrigen + "|" + v.PersonaOrigen)
                         || claves.Contains(v.TipoDestino + "|" + v.IdDestino + "|" + v.PersonaDestino))
                .OrderBy(v => v.Id)
                .ToListAsync(ct);

            var siguiente = new List<ExtremoVinculo>();
            foreach (var enlace in enlaces)
            {
                foreach (var (desde, hacia) in new[] { (enlace.Origen, enlace.Destino), (enlace.Destino, enlace.Origen) })
                {
                    if (!alcanzados.ContainsKey(desde) || alcanzados.ContainsKey(hacia)) continue;
                    if (alcanzados.Count > MaxCadena) break;
                    alcanzados[hacia] = (desde, nivel == 1 ? enlace.Id : null, nivel);
                    siguiente.Add(hacia);
                }
            }
            frontera = siguiente;
        }

        alcanzados.Remove(inicio);
        if (alcanzados.Count == 0) return [];

        var resumenes = await ResumirAsync(alcanzados.Keys, ct);
        var resultado = new List<(int Nivel, PersonaVinculada Persona)>();
        foreach (var (extremo, (previo, vinculoId, nv)) in alcanzados)
        {
            if (!resumenes.TryGetValue(extremo, out var resumen)) continue; // anulado o inexistente

            string? porMedio = null;
            if (nv > 1 && previo is { } p && resumenes.TryGetValue(p, out var r))
                porMedio = $"{r.Nombres} {r.Apellidos}".Trim() + $" ({r.Sacramento})";
            else if (nv > 1)
                porMedio = "otro registro";

            resultado.Add((nv, new PersonaVinculada(extremo, resumen, nv == 1, nv == 1 ? vinculoId : null, porMedio)));
        }

        return [.. resultado
            .OrderBy(x => x.Nivel)
            .ThenBy(x => x.Persona.Resumen.Sacramento, StringComparer.Ordinal)
            .ThenBy(x => x.Persona.Extremo.Id)
            .Select(x => x.Persona)];
    }

    private async Task<Dictionary<ExtremoVinculo, ResultadoBusquedaRegistro>> ResumirAsync(
        IEnumerable<ExtremoVinculo> extremos, CancellationToken ct)
    {
        var mapa = new Dictionary<ExtremoVinculo, ResultadoBusquedaRegistro>();
        foreach (var grupo in extremos.GroupBy(e => e.Tipo))
        {
            if (Modulo(grupo.Key) is not { } modulo) continue;
            var filas = await modulo.ResumenesAsync([.. grupo.Select(e => e.Id).Distinct()], ct);
            foreach (var f in filas)
                mapa[new ExtremoVinculo(grupo.Key, f.Id, f.Persona)] = f;
        }
        return mapa;
    }

    /// <summary>Resumen de una persona (null si el módulo, la persona o el registro no existen o está anulado).</summary>
    public async Task<ResultadoBusquedaRegistro?> ResumenAsync(ExtremoVinculo extremo, CancellationToken ct = default) =>
        (await ResumirAsync([extremo], ct)).GetValueOrDefault(extremo);

    private async Task<string?> ValidarExtremoAsync(ExtremoVinculo e, CancellationToken ct)
    {
        if (Modulo(e.Tipo) is not { } modulo) return "El libro indicado no existe.";
        if (!modulo.Personas.Contains(e.Persona)) return "La persona indicada no existe en ese libro.";
        return await ResumenAsync(e, ct) is null ? "El registro ya no existe o está anulado." : null;
    }
}
