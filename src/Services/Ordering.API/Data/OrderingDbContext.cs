using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Domain;

namespace Ordering.API.Data;

public class OrderingDbContext : DbContext
{
    public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id);

            order.Property(o => o.Id).ValueGeneratedNever();

            order.HasIndex(o => o.BuyerId);

            order.HasMany(o => o.Lines)
                 .WithOne()
                 .HasForeignKey(l => l.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
