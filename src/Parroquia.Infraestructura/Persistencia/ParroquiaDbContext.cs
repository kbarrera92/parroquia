using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Infraestructura.Persistencia;

public class ParroquiaDbContext(DbContextOptions<ParroquiaDbContext> options)
    : IdentityDbContext<Usuario>(options)
{
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Usuario>().Property(u => u.NombreCompleto).HasMaxLength(200);

        ConfiguracionAuditable.Aplicar(builder); // siempre al final
    }
}
