using BasketApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace BasketApi.Data;

public class BasketDbContext : DbContext
{
    public BasketDbContext(DbContextOptions<BasketDbContext> options) : base(options)
    {
    }

    public DbSet<Basket> Baskets => Set<Basket>();
    public DbSet<BasketItem> BasketItems => Set<BasketItem>();
    public DbSet<ProcessedOrder> ProcessedOrders => Set<ProcessedOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Samme regler som monolittens BasketConfiguration + BasketItemConfiguration.
        modelBuilder.Entity<Basket>(basket =>
        {
            basket.Property(b => b.BuyerId)
                  .IsRequired()
                  .HasMaxLength(256);

            basket.HasIndex(b => b.BuyerId);

            basket.HasMany(b => b.Items)
                  .WithOne()
                  .HasForeignKey(i => i.BasketId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Items er read-only udadtil; EF skal skrive direkte i feltet _items.
            basket.Navigation(b => b.Items)
                  .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<BasketItem>(item =>
        {
            item.Property(i => i.UnitPrice)
                .IsRequired()
                .HasColumnType("decimal(18,2)");
        });

        // Idempotent Receiver: OrderId er nøglen og kommer fra beskeden (ikke autonummer).
        // En dublet giver derfor en nøgle-konflikt i stedet for en ekstra række.
        modelBuilder.Entity<ProcessedOrder>(processed =>
        {
            processed.HasKey(p => p.OrderId);
            processed.Property(p => p.OrderId).ValueGeneratedNever();
        });
    }
}
