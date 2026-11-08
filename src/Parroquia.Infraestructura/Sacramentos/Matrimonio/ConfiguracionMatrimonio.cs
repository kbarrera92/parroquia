using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

public sealed class ConfiguracionMatrimonio : IEntityTypeConfiguration<Matrimonio>
{
    public const string TablaMatrimonio = "matrimonio";

    /// <summary>Columna generada con la clave de orden de la fecha del matrimonio (año*10000+mes*100+día).</summary>
    public const string ClaveFecha = "fecha_matrimonio_clave_orden";

    public void Configure(EntityTypeBuilder<Matrimonio> e)
    {
        e.ConfigurarRegistro(TablaMatrimonio);

        // La búsqueda común guarda a los dos contrayentes (hasta 2 × MaxBusqueda).
        e.Property(x => x.BusquedaNormalizada).HasMaxLength(2 * ValidadorRegistro.MaxBusqueda).NoAuditar();

        e.Property(x => x.LugarCelebracion).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.Testigos).HasColumnType("text[]");
        e.MapearFechaParcial(x => x.FechaMatrimonio, "fecha_matrimonio");
        e.HasIndex(ClaveFecha);

        MapearContrayente(e, x => x.Esposo, "esposo");
        MapearContrayente(e, x => x.Esposa, "esposa");

        // Búsqueda por cada contrayente (LIKE '%texto%' con índice trigram) y orden alfabético por apellidos.
        e.Property(x => x.BusquedaEsposo).HasMaxLength(ValidadorRegistro.MaxBusqueda).NoAuditar();
        e.Property(x => x.BusquedaEsposa).HasMaxLength(ValidadorRegistro.MaxBusqueda).NoAuditar();
        e.HasIndex(x => x.BusquedaEsposo).HasMethod("gin").HasOperators("gin_trgm_ops");
        e.HasIndex(x => x.BusquedaEsposa).HasMethod("gin").HasOperators("gin_trgm_ops");
    }

    private static void MapearContrayente(
        EntityTypeBuilder<Matrimonio> e, System.Linq.Expressions.Expression<Func<Matrimonio, Contrayente?>> propiedad, string prefijo)
    {
        e.ComplexProperty(propiedad, cp =>
        {
            cp.Property(c => c.Tratamiento).HasColumnName($"{prefijo}_tratamiento").HasMaxLength(ValidadorRegistro.MaxNombre);
            cp.Property(c => c.Nombres).HasColumnName($"{prefijo}_nombres").HasMaxLength(ValidadorRegistro.MaxNombre);
            cp.Property(c => c.Apellidos).HasColumnName($"{prefijo}_apellidos").HasMaxLength(ValidadorRegistro.MaxNombre);
            cp.Property(c => c.Edad).HasColumnName($"{prefijo}_edad");
            cp.Property(c => c.OriginarioDe).HasColumnName($"{prefijo}_originario_de").HasMaxLength(ValidadorRegistro.MaxNombre);
            cp.Property(c => c.FeligresDe).HasColumnName($"{prefijo}_feligres_de").HasMaxLength(ValidadorRegistro.MaxNombre);
            cp.Property(c => c.Padre).HasColumnName($"{prefijo}_padre").HasMaxLength(ValidadorRegistro.MaxNombre);
            cp.Property(c => c.Madre).HasColumnName($"{prefijo}_madre").HasMaxLength(ValidadorRegistro.MaxNombre);
        });
    }
}
