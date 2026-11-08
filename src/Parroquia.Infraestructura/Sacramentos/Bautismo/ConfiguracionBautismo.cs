using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

public sealed class ConfiguracionBautismo : IEntityTypeConfiguration<Bautismo>
{
    public const string TablaBautismo = "bautismo";

    /// <summary>Columna generada con la clave de orden de la fecha del bautismo (año*10000+mes*100+día).</summary>
    public const string ClaveFecha = "fecha_bautismo_clave_orden";

    public void Configure(EntityTypeBuilder<Bautismo> e)
    {
        e.ConfigurarRegistro(TablaBautismo);
        e.Property(x => x.Nombres).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.Apellidos).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.LugarNacimiento).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.Padre).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.Madre).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.Padrinos).HasColumnType("text[]");
        e.MapearFechaParcial(x => x.FechaBautismo, "fecha_bautismo");
        e.MapearFechaParcial(x => x.FechaNacimiento, "fecha_nacimiento");
        e.HasIndex(ClaveFecha);
        e.HasIndex(x => new { x.Apellidos, x.Nombres });
    }
}
