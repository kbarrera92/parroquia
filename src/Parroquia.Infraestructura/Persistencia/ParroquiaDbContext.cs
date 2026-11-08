using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Infraestructura.Persistencia;

public class ParroquiaDbContext(DbContextOptions<ParroquiaDbContext> options)
    : IdentityDbContext<Usuario>(options)
{
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<BitacoraAcceso> BitacoraAccesos => Set<BitacoraAcceso>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Usuario>().Property(u => u.NombreCompleto).HasMaxLength(200);

        builder.Entity<BitacoraAcceso>(e =>
        {
            e.Property(b => b.UsuarioNombre).HasMaxLength(256);
            e.Property(b => b.Evento).HasConversion<string>().HasMaxLength(40);
            e.Property(b => b.Entidad).HasMaxLength(100);
            e.Property(b => b.Detalle).HasMaxLength(500);
            e.Property(b => b.Ip).HasMaxLength(45);
            e.HasIndex(b => b.Fecha);
            e.HasIndex(b => new { b.UsuarioNombre, b.Fecha });
        });

        ConfiguracionAuditable.Aplicar(builder); // siempre al final
    }
}
