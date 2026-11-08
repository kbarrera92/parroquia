using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Parroquia.Aplicacion.Seguridad;
using Parroquia.Dominio.Comun;

namespace Parroquia.Infraestructura.Persistencia;

/// <summary>
/// Rellena CreadoPor/ModificadoPor y escribe <see cref="Auditoria"/> en el mismo SaveChanges.
/// Ningún código de negocio escribe auditoría a mano (CLAUDE.md regla 4).
/// </summary>
public sealed class AuditoriaInterceptor(IUsuarioActual usuario, TimeProvider reloj) : SaveChangesInterceptor
{
    private static readonly HashSet<string> CamposIgnorados =
    [
        nameof(IAuditable.CreadoPor), nameof(IAuditable.CreadoEn),
        nameof(IAuditable.ModificadoPor), nameof(IAuditable.ModificadoEn),
        nameof(EntidadAuditable.Version),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Auditar(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Auditar(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Auditar(DbContext contexto)
    {
        contexto.ChangeTracker.DetectChanges();
        var ahora = reloj.GetUtcNow();
        var nuevas = new List<Auditoria>();

        foreach (var entrada in contexto.ChangeTracker.Entries<IAuditable>().ToList())
        {
            switch (entrada.State)
            {
                case EntityState.Deleted:
                    throw new InvalidOperationException(
                        $"Los registros no se borran, se anulan ({entrada.Metadata.ClrType.Name}).");

                case EntityState.Added:
                    entrada.Entity.CreadoPor = usuario.Nombre;
                    entrada.Entity.CreadoEn = ahora;
                    entrada.Entity.ModificadoPor = usuario.Nombre;
                    entrada.Entity.ModificadoEn = ahora;
                    nuevas.Add(Crear(entrada, AccionAuditoria.Alta, CambiosDeAlta(entrada), ahora));
                    break;

                case EntityState.Modified:
                    var cambios = CambiosDeModificacion(entrada);
                    if (cambios.Count == 0)
                    {
                        // Solo cambió una columna derivada (NoAuditar): se guarda tal cual, sin auditoría ni ModificadoPor.
                        if (!CambioDerivado(entrada)) entrada.State = EntityState.Unchanged;
                        break;
                    }

                    entrada.Property(nameof(IAuditable.CreadoPor)).IsModified = false;
                    entrada.Property(nameof(IAuditable.CreadoEn)).IsModified = false;
                    entrada.Entity.ModificadoPor = usuario.Nombre;
                    entrada.Entity.ModificadoEn = ahora;
                    nuevas.Add(Crear(entrada, AccionDe(entrada), cambios, ahora));
                    break;
            }
        }

        if (nuevas.Count > 0) contexto.Set<Auditoria>().AddRange(nuevas);
    }

    private Auditoria Crear(EntityEntry<IAuditable> entrada, AccionAuditoria accion,
        Dictionary<string, Cambio> cambios, DateTimeOffset ahora) => new()
    {
        Entidad = entrada.Metadata.ClrType.Name,
        EntidadId = entrada.Entity.Id,
        Accion = accion,
        UsuarioId = usuario.Id,
        UsuarioNombre = usuario.Nombre,
        Fecha = ahora,
        CambiosJson = JsonSerializer.Serialize(cambios),
    };

    private static AccionAuditoria AccionDe(EntityEntry<IAuditable> entrada)
    {
        if (entrada.Entity is IAnulable)
        {
            var anulado = entrada.Property(nameof(IAnulable.Anulado));
            if (anulado.IsModified && !Equals(anulado.OriginalValue, anulado.CurrentValue))
                return (bool)anulado.CurrentValue! ? AccionAuditoria.Anulacion : AccionAuditoria.RevertirAnulacion;
        }
        return AccionAuditoria.Modificacion;
    }

    private static bool Auditable(PropertyEntry p) =>
        !p.Metadata.IsShadowProperty()
        && p.Metadata.ValueGenerated == ValueGenerated.Never
        && !CamposIgnorados.Contains(p.Metadata.Name)
        && p.Metadata.FindAnnotation(ConfiguracionAuditable.AnotacionNoAuditar) is null;

    private static bool CambioDerivado(EntityEntry<IAuditable> entrada) =>
        entrada.Properties.Any(p =>
            p.IsModified
            && p.Metadata.FindAnnotation(ConfiguracionAuditable.AnotacionNoAuditar) is not null
            && !Iguales(p.OriginalValue, p.CurrentValue));

    private static Dictionary<string, Cambio> CambiosDeAlta(EntityEntry<IAuditable> entrada)
    {
        var cambios = new Dictionary<string, Cambio>();
        foreach (var p in entrada.Properties.Where(Auditable))
            if (p.CurrentValue is not null)
                cambios[p.Metadata.Name] = new Cambio(null, Valor(p.CurrentValue));

        foreach (var cp in entrada.ComplexProperties)
        {
            var actual = Leer(cp, original: false);
            if (actual is not null) cambios[cp.Metadata.Name] = new Cambio(null, actual);
        }
        return cambios;
    }

    private static Dictionary<string, Cambio> CambiosDeModificacion(EntityEntry<IAuditable> entrada)
    {
        var cambios = new Dictionary<string, Cambio>();
        foreach (var p in entrada.Properties.Where(p => Auditable(p) && p.IsModified))
            if (!Iguales(p.OriginalValue, p.CurrentValue))
                cambios[p.Metadata.Name] = new Cambio(Valor(p.OriginalValue), Valor(p.CurrentValue));

        foreach (var cp in entrada.ComplexProperties)
        {
            var anterior = Leer(cp, original: true);
            var nuevo = Leer(cp, original: false);
            if (!Equals(anterior, nuevo))
                cambios[cp.Metadata.Name] = new Cambio(anterior, nuevo);
        }
        return cambios;
    }

    /// <summary>Texto de una propiedad compleja; null si está vacía. FechaParcial se imprime como en el libro.</summary>
    private static string? Leer(ComplexPropertyEntry cp, bool original)
    {
        object? Hoja(string nombre)
        {
            var p = cp.Property(nombre);
            return original ? p.OriginalValue : p.CurrentValue;
        }

        if (cp.Metadata.ClrType == typeof(FechaParcial))
        {
            var texto = new FechaParcial
            {
                Anio = (short?)Hoja(nameof(FechaParcial.Anio)),
                Mes = (short?)Hoja(nameof(FechaParcial.Mes)),
                Dia = (short?)Hoja(nameof(FechaParcial.Dia)),
            }.ATexto();
            return texto.Length == 0 ? null : texto;
        }

        var partes = cp.Properties
            .Select(p => original ? p.OriginalValue : p.CurrentValue)
            .Where(v => v is not null)
            .Select(v => Convert.ToString(v, CultureInfo.InvariantCulture));
        var unido = string.Join(" ", partes);
        return unido.Length == 0 ? null : unido;
    }

    private static bool Iguales(object? a, object? b) =>
        a is byte[] x && b is byte[] y ? x.AsSpan().SequenceEqual(y) : Equals(a, b);

    private static object? Valor(object? v) => v switch
    {
        null => null,
        byte[] bytes => $"[{bytes.Length} bytes]",
        bool or string or short or int or long => v,
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    private sealed record Cambio(
        [property: JsonPropertyName("anterior")] object? Anterior,
        [property: JsonPropertyName("nuevo")] object? Nuevo);
}
