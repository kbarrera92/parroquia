using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Numeracion;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Infraestructura.Numeracion;

public sealed class ConfiguracionLibroDigital : IEntityTypeConfiguration<LibroDigital>
{
    public const int MaxTipo = 50;

    public void Configure(EntityTypeBuilder<LibroDigital> e)
    {
        e.ToTable("libro_digital");
        e.Property(l => l.TipoSacramento).HasMaxLength(MaxTipo);
        e.Property(l => l.Numero).HasMaxLength(ValidadorRegistro.MaxClave);

        e.Property(l => l.ModoPartida).HasConversion<string>().HasMaxLength(30);

        // Los contadores avanzan con cada asiento: se guardan pero no llenan la auditoría
        // (cada asiento ya queda auditado en su propio registro con libro/folio/partida).
        e.Property(l => l.FolioActual).NoAuditar();
        e.Property(l => l.PartidasEnFolio).NoAuditar();
        e.Property(l => l.UltimaPartida).NoAuditar();
        e.Property(l => l.Entregados).NoAuditar();

        // A lo sumo un libro abierto por sacramento, y un número de libro no se repite dentro del sacramento.
        e.HasIndex(l => l.TipoSacramento).IsUnique().HasFilter("abierto");
        e.HasIndex(l => new { l.TipoSacramento, l.Numero }).IsUnique();
    }
}
