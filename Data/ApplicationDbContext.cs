using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Practica2AppCreditos.Models;

namespace Practica2AppCreditos.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();

    public DbSet<SolicitudCredito> SolicitudesCredito => Set<SolicitudCredito>();

    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Cliente>(entity =>
        {
            entity.HasKey(cliente => cliente.Id);

            entity.HasOne(cliente => cliente.Usuario)
                .WithOne()
                .HasForeignKey<Cliente>(cliente => cliente.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(cliente => cliente.UsuarioId)
                .IsRequired();

            entity.Property(cliente => cliente.IngresosMensuales)
                .HasColumnType("decimal(18,2)");

            entity.Property(cliente => cliente.Activo)
                .HasDefaultValue(true);
        });

        builder.Entity<SolicitudCredito>(entity =>
        {
            entity.HasKey(solicitud => solicitud.Id);

            entity.HasOne(solicitud => solicitud.Cliente)
                .WithMany(cliente => cliente.Solicitudes)
                .HasForeignKey(solicitud => solicitud.ClienteId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(solicitud => solicitud.MontoSolicitado)
                .HasColumnType("decimal(18,2)");

            entity.Property(solicitud => solicitud.Estado)
                .HasConversion<int>()
                .HasDefaultValue(EstadoSolicitud.Pendiente);

            entity.HasIndex(solicitud => new { solicitud.ClienteId, solicitud.Estado })
                .HasFilter("[Estado] = 0")
                .IsUnique();
        });

        builder.Entity<Notificacion>(entity =>
        {
            entity.HasKey(notificacion => notificacion.Id);

            entity.Property(notificacion => notificacion.MessageId)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(notificacion => notificacion.UsuarioId)
                .IsRequired()
                .HasMaxLength(450);

            entity.Property(notificacion => notificacion.Texto)
                .IsRequired();

            entity.HasIndex(notificacion => notificacion.MessageId)
                .IsUnique();

            entity.HasIndex(notificacion => new
            {
                notificacion.UsuarioId,
                notificacion.FechaProcesamientoUtc
            });
        });
    }
}
