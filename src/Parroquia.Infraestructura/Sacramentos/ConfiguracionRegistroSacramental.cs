using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Numeracion;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

/// <summary>Mapeo EF de los campos comunes de todo sacramento. Cada módulo lo llama y agrega lo suyo.</summary>
public static class ConfiguracionRegistroSacramental
{
    public static EntityTypeBuilder<T> ConfigurarRegistro<T>(this EntityTypeBuilder<T> e, string tabla)
        where T : RegistroSacramental
    {
        e.ToTable(tabla);

        e.Property(x => x.Libro).HasMaxLength(ValidadorRegistro.MaxClave);
        e.Property(x => x.Folio).HasMaxLength(ValidadorRegistro.MaxClave);
        e.Property(x => x.Partida).HasMaxLength(ValidadorRegistro.MaxClave);
        e.Property(x => x.LibroOrden).HasMaxLength(ValidadorRegistro.MaxOrden).NoAuditar();
        e.Property(x => x.FolioOrden).HasMaxLength(ValidadorRegistro.MaxOrden).NoAuditar();
        e.Property(x => x.PartidaOrden).HasMaxLength(ValidadorRegistro.MaxOrden).NoAuditar();
        e.Property(x => x.BusquedaNormalizada).HasMaxLength(ValidadorRegistro.MaxBusqueda).NoAuditar();
        e.Property(x => x.MotivoRevision).HasMaxLength(ValidadorRegistro.MaxMotivo);
        e.Property(x => x.MotivoAnulacion).HasMaxLength(ValidadorRegistro.MaxMotivo);
        e.Property(x => x.NotasMarginales).HasMaxLength(ValidadorRegistro.MaxNotas);
        e.Property(x => x.MinistroTexto).HasMaxLength(ValidadorRegistro.MaxNombre);

        e.HasOne(x => x.Ministro).WithMany().HasForeignKey(x => x.MinistroId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<LibroDigital>().WithMany().HasForeignKey(x => x.LibroDigitalId).OnDelete(DeleteBehavior.Restrict);
        e.Ignore(x => x.EsAsientoDelSistema);

        // Duplicados (advertencia, no bloqueo) y listado en orden natural.
        e.HasIndex(x => new { x.LibroOrden, x.FolioOrden, x.PartidaOrden });
        // Búsqueda parcial sin tildes: LIKE '%texto%' usa el índice trigram.
        e.HasIndex(x => x.BusquedaNormalizada).HasMethod("gin").HasOperators("gin_trgm_ops");
        e.HasIndex(x => x.Anulado);
        e.HasIndex(x => x.Revisar);

        // Los anulados no salen en listados ni búsquedas; el Administrador los ve con IgnoreQueryFilters.
        e.HasQueryFilter(x => !x.Anulado);
        return e;
    }
}
