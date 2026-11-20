using Inventory.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.API.Data;

public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockItem>(stock =>
        {
            // Katalog-id'et kommer fra kataloget, så det er ikke et autonummer.
            stock.HasKey(s => s.CatalogItemId);
            stock.Property(s => s.CatalogItemId).ValueGeneratedNever();
            stock.Property(s => s.Version).IsConcurrencyToken();
            stock.Ignore(s => s.Available);

            // Startbeholdning for kataloget i monolitten (vare 1-12). Vare 5 (Roslyn Red Sheet) har kun 2 stk.,
            // så man kan se en ordre blive afvist ved at købe 3.
            stock.HasData(
                Enumerable.Range(1, 12).Select(id => new
                {
                    CatalogItemId = id,
                    OnHand = id == 5 ? 2 : 20,
                    Reserved = 0,
                    Version = 0
                }).ToArray());
        });

        modelBuilder.Entity<Reservation>(reservation =>
        {
            // Én reservation pr. ordre. En dublet giver en nøglekonflikt i stedet for en ekstra række.
            reservation.HasKey(r => r.OrderId);
            reservation.Property(r => r.OrderId).ValueGeneratedNever();
            reservation.Property(r => r.Status).IsRequired().HasMaxLength(16);
            reservation.Ignore(r => r.Shortages);

            reservation.HasMany(r => r.Lines)
                       .WithOne()
                       .HasForeignKey(l => l.OrderId)
                       .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
