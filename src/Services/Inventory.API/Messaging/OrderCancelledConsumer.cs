using Contracts;
using Inventory.API.Data;
using Inventory.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.API.Messaging;

/// <summary>
/// Kompenserende handling i checkout-sagaen: når Ordering.API annullerer en bekræftet ordre,
/// lægges de reserverede varer tilbage på lager.
/// </summary>
public class OrderCancelledConsumer : IConsumer<OrderCancelled>
{
    private readonly InventoryDbContext _db;
    private readonly ILogger<OrderCancelledConsumer> _logger;

    public OrderCancelledConsumer(InventoryDbContext db, ILogger<OrderCancelledConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var orderId = context.Message.OrderId;
        var cancellationToken = context.CancellationToken;

        var reservation = await _db.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

        // Idempotent: kun en reservation med status Reserved kan frigives. Er den allerede Released
        // (samme besked to gange) eller Rejected (intet blev reserveret), gør vi ingenting.
        if (reservation is null || reservation.Status != ReservationStatus.Reserved)
        {
            _logger.LogInformation("Nothing to release for order {OrderId} ({ReservationStatus})",
                orderId, reservation?.Status ?? "no reservation");
            return;
        }

        var catalogItemIds = reservation.Lines.Select(line => line.CatalogItemId).ToList();
        var stock = await _db.StockItems
            .Where(s => catalogItemIds.Contains(s.CatalogItemId))
            .ToDictionaryAsync(s => s.CatalogItemId, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            stock[line.CatalogItemId].Release(line.Units);
        }
        reservation.MarkReleased();

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Released the reservation for order {OrderId}", orderId);
    }
}
