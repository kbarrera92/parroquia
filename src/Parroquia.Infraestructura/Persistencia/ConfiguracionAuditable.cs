using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Comun;

namespace Parroquia.Infraestructura.Persistencia;

/// <summary>Convenciones EF comunes a todas las entidades auditables. Llamar al final de OnModelCreating.</summary>
public static class ConfiguracionAuditable
{
    public const string SecuenciaId = "secuencia_id";

    public static void Aplicar(ModelBuilder builder)
    {
        builder.Entity<Auditoria>(e =>
        {
            e.Property(a => a.Entidad).HasMaxLength(100);
            e.Property(a => a.UsuarioNombre).HasMaxLength(256);
            e.Property(a => a.Accion).HasConversion<string>().HasMaxLength(30);
            e.Property(a => a.CambiosJson).HasColumnType("jsonb");
            e.HasIndex(a => new { a.Entidad, a.EntidadId });
            e.HasIndex(a => a.Fecha);
        });

        foreach (var tipo in builder.Model.GetEntityTypes()
                     .Where(t => typeof(IAuditable).IsAssignableFrom(t.ClrType)).ToList())
        {
            var e = builder.Entity(tipo.ClrType);
            // Id por secuencia HiLo: existe al hacer Add, así la auditoría de Alta lleva EntidadId en el mismo SaveChanges.
            e.Property(nameof(IAuditable.Id)).UseHiLo(SecuenciaId);
            e.Property(nameof(EntidadAuditable.Version)).IsRowVersion();
            e.Property(nameof(IAuditable.CreadoPor)).HasMaxLength(256);
            e.Property(nameof(IAuditable.ModificadoPor)).HasMaxLength(256);
        }
    }

    /// <summary>
    /// Mapea una <see cref="FechaParcial"/> a columnas {prefijo}_anio/_mes/_dia más una columna generada
    /// {prefijo}_clave_orden para ordenar y filtrar. <paramref name="prefijo"/> en snake_case.
    /// </summary>
    public static EntityTypeBuilder<T> MapearFechaParcial<T>(
        this EntityTypeBuilder<T> builder, Expression<Func<T, FechaParcial?>> propiedad, string prefijo)
        where T : class
    {
        builder.ComplexProperty(propiedad, cp =>
        {
            cp.Property(f => f.Anio).HasColumnName($"{prefijo}_anio");
            cp.Property(f => f.Mes).HasColumnName($"{prefijo}_mes");
            cp.Property(f => f.Dia).HasColumnName($"{prefijo}_dia");
        });

        builder.Property<int>($"{prefijo}_clave_orden")
            .HasColumnName($"{prefijo}_clave_orden")
            .HasComputedColumnSql(
                $"COALESCE({prefijo}_anio, 0) * 10000 + COALESCE({prefijo}_mes, 0) * 100 + COALESCE({prefijo}_dia, 0)",
                stored: true);

        return builder;
    }
}
