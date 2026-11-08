using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

public sealed class ConfiguracionComunion : IEntityTypeConfiguration<Comunion>
{
    public const string TablaComunion = "comunion";

    /// <summary>Columna generada con la clave de orden de la fecha (año*10000+mes*100+día).</summary>
    public const string ClaveFecha = "fecha_comunion_clave_orden";

    public void Configure(EntityTypeBuilder<Comunion> e)
    {
        e.ConfigurarRegistro(TablaComunion);
        e.Property(x => x.Nombres).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.Apellidos).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.MapearFechaParcial(x => x.FechaComunion, "fecha_comunion");
        e.HasIndex(ClaveFecha);
        e.HasIndex(x => new { x.Apellidos, x.Nombres });
    }
}
