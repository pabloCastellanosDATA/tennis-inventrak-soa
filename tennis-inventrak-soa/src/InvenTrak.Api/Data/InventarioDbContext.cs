using InvenTrak.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace InvenTrak.Api.Data;

public class InventarioDbContext(DbContextOptions<InventarioDbContext> options) : DbContext(options)
{
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Existencia> Existencias => Set<Existencia>();
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();
    public DbSet<Reserva> Reservas => Set<Reserva>();
    public DbSet<ServicioRegistrado> Servicios => Set<ServicioRegistrado>();

    /// <summary>SQLite no guarda la zona horaria: todas las fechas se tratan como UTC al leerlas.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<FechaUtcConverter>();

    private sealed class FechaUtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Producto>(e =>
        {
            e.HasKey(p => p.Referencia);
            e.Property(p => p.Nombre).IsRequired();
        });

        modelBuilder.Entity<Existencia>(e =>
        {
            e.HasIndex(x => new { x.Referencia, x.Talla, x.Sede }).IsUnique();
            e.HasOne<Producto>().WithMany().HasForeignKey(x => x.Referencia);
            e.Property(x => x.VersionFila).IsConcurrencyToken();
            e.Ignore(x => x.Disponibles);
        });

        modelBuilder.Entity<Movimiento>(e => e.HasIndex(m => m.Referencia));

        modelBuilder.Entity<Reserva>(e => e.HasIndex(r => r.Estado));

        var listaComparer = new ValueComparer<List<string>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v.ToList());

        modelBuilder.Entity<ServicioRegistrado>(e =>
        {
            e.HasKey(s => s.Nombre);
            e.Property(s => s.Operaciones)
                .HasConversion(v => string.Join('|', v), v => v.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList())
                .Metadata.SetValueComparer(listaComparer);
            e.Property(s => s.Consumidores)
                .HasConversion(v => string.Join('|', v), v => v.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList())
                .Metadata.SetValueComparer(listaComparer);
        });
    }
}
