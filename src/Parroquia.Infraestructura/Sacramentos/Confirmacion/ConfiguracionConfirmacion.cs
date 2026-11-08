using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Parroquia.Dominio.Sacramentos;
using Parroquia.Infraestructura.Persistencia;

namespace Parroquia.Infraestructura.Sacramentos;

public sealed class ConfiguracionConfirmacion : IEntityTypeConfiguration<Confirmacion>
{
    public const string TablaConfirmacion = "confirmacion";

    /// <summary>Columna generada con la clave de orden de la fecha de la confirmación (año*10000+mes*100+día).</summary>
    public const string ClaveFecha = "fecha_confirmacion_clave_orden";

    public void Configure(EntityTypeBuilder<Confirmacion> e)
    {
        e.ConfigurarRegistro(TablaConfirmacion);
        e.Property(x => x.Nombres).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.Apellidos).HasMaxLength(ValidadorRegistro.MaxNombre);
        e.Property(x => x.Padrinos).HasColumnType("text[]");
        e.MapearFechaParcial(x => x.FechaConfirmacion, "fecha_confirmacion");
        e.HasIndex(ClaveFecha);
        e.HasIndex(x => new { x.Apellidos, x.Nombres });
    }
}
