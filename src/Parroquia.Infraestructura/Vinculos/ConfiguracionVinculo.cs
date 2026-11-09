using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Vinculos;

namespace Parroquia.Infraestructura.Vinculos;

public sealed class ConfiguracionVinculo : IEntityTypeConfiguration<Vinculo>
{
    public const int MaxTipo = 50;
    public const int MaxPersona = 20;
    public const int MaxMotivo = 500;

    public void Configure(EntityTypeBuilder<Vinculo> e)
    {
        e.ToTable("vinculo");
        e.Property(v => v.TipoOrigen).HasMaxLength(MaxTipo);
        e.Property(v => v.TipoDestino).HasMaxLength(MaxTipo);
        e.Property(v => v.PersonaOrigen).HasMaxLength(MaxPersona);
        e.Property(v => v.PersonaDestino).HasMaxLength(MaxPersona);
        e.Property(v => v.MotivoAnulacion).HasMaxLength(MaxMotivo);
        e.Ignore(v => v.Origen);
        e.Ignore(v => v.Destino);

        // Un par solo existe una vez (desvincular anula la fila; volver a vincular la reactiva).
        e.HasIndex(v => new { v.TipoOrigen, v.IdOrigen, v.PersonaOrigen, v.TipoDestino, v.IdDestino, v.PersonaDestino })
            .IsUnique();
        e.HasIndex(v => new { v.TipoOrigen, v.IdOrigen });
        e.HasIndex(v => new { v.TipoDestino, v.IdDestino });

        e.HasQueryFilter(v => !v.Anulado);
    }
}
