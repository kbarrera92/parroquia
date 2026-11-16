using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Numeracion;
using Parroquia.Dominio.Supletorias;
using Parroquia.Infraestructura.Numeracion;
using Parroquia.Infraestructura.Persistencia;
using Parroquia.Infraestructura.Sacramentos;

namespace Parroquia.Infraestructura.Supletorias;

public sealed class ConfiguracionSupletoria : IEntityTypeConfiguration<Supletoria>
{
    public const int MaxTestigo = ValidadorRegistro.MaxNombre;
    public const int MaxParentesco = 100;

    /// <summary>Columna generada con la clave de orden de la fecha (año*10000+mes*100+día).</summary>
    public const string ClaveFecha = "fecha_supletoria_clave_orden";

    public void Configure(EntityTypeBuilder<Supletoria> e)
    {
        e.ToTable("supletoria");
        e.Property(x => x.TipoSacramento).HasMaxLength(ConfiguracionLibroDigital.MaxTipo);
        e.Property(x => x.Libro).HasMaxLength(ValidadorRegistro.MaxClave);
        e.Property(x => x.Folio).HasMaxLength(ValidadorRegistro.MaxClave);
        e.Property(x => x.Partida).HasMaxLength(ValidadorRegistro.MaxClave);
        e.Property(x => x.LibroOrden).HasMaxLength(ValidadorRegistro.MaxOrden).NoAuditar();
        e.Property(x => x.FolioOrden).HasMaxLength(ValidadorRegistro.MaxOrden).NoAuditar();
        e.Property(x => x.PartidaOrden).HasMaxLength(ValidadorRegistro.MaxOrden).NoAuditar();
        e.Property(x => x.Testigo).HasMaxLength(MaxTestigo);
        e.Property(x => x.ParentescoTestigo).HasMaxLength(MaxParentesco);
        e.Property(x => x.MotivoAnulacion).HasMaxLength(ValidadorRegistro.MaxMotivo);
        e.Ignore(x => x.EsAsientoDelSistema);

        e.HasOne<LibroDigital>().WithMany().HasForeignKey(x => x.LibroDigitalId).OnDelete(DeleteBehavior.Restrict);
        e.MapearFechaParcial(x => x.FechaSupletoria, "fecha_supletoria");

        // Un registro tiene a lo sumo una supletoria (anulada o no: anular el registro no la libera).
        e.HasIndex(x => new { x.TipoSacramento, x.RegistroId }).IsUnique();
        // Número del Libro de Supletorias: listado en orden natural y aviso de repetidos.
        e.HasIndex(x => new { x.LibroOrden, x.FolioOrden, x.PartidaOrden });
        e.HasIndex(ClaveFecha);
        e.HasIndex(x => x.Anulado);

        e.HasQueryFilter(x => !x.Anulado);
    }
}
