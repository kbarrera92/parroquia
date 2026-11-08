using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Parroquia.Dominio.Catalogos;
using Parroquia.Dominio.Configuracion;
using Parroquia.Infraestructura.Identidad;

namespace Parroquia.Infraestructura.Persistencia;

public class ParroquiaDbContext(DbContextOptions<ParroquiaDbContext> options)
    : IdentityDbContext<Usuario>(options)
{
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<BitacoraAcceso> BitacoraAccesos => Set<BitacoraAcceso>();
    public DbSet<Ministro> Ministros => Set<Ministro>();
    public DbSet<Tratamiento> Tratamientos => Set<Tratamiento>();
    public DbSet<ConfiguracionParroquia> Configuraciones => Set<ConfiguracionParroquia>();
    public DbSet<ParrocoVigente> Parrocos => Set<ParrocoVigente>();

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

        builder.Entity<Ministro>().Property(m => m.Nombre).HasMaxLength(150);
        builder.Entity<Tratamiento>().Property(t => t.Texto).HasMaxLength(150);

        builder.Entity<ConfiguracionParroquia>(e =>
        {
            e.Property(c => c.Nombre).HasMaxLength(200);
            e.Property(c => c.Direccion).HasMaxLength(200);
            e.Property(c => c.Municipio).HasMaxLength(200);
            e.Property(c => c.Telefono).HasMaxLength(50);
            e.Property(c => c.TextoLugarEmision).HasMaxLength(100);
            e.Property(c => c.CiudadAbreviada).HasMaxLength(100);
            e.Property(c => c.LogoTipoContenido).HasMaxLength(50);
        });

        builder.Entity<ParrocoVigente>(e =>
        {
            e.Property(p => p.Nombre).HasMaxLength(150);
            e.Property(p => p.Titulo).HasMaxLength(50);
            // A lo sumo un párroco vigente: índice único parcial.
            e.HasIndex(p => p.EsActual).IsUnique().HasFilter("es_actual");
        });

        ConfiguracionAuditable.Aplicar(builder); // siempre al final
    }
}
