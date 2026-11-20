using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Data;
using Ordering.API.Domain;

namespace Ordering.API.Messaging;

/// <summary>
/// Lageret har reserveret alle varer: ordren er bekræftet.
/// </summary>
public class StockReservedConsumer : IConsumer<StockReserved>
{
    private readonly OrderingDbContext _db;
    private readonly ILogger<StockReservedConsumer> _logger;

    public StockReservedConsumer(OrderingDbContext db, ILogger<StockReservedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockReserved> context)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == context.Message.OrderId, context.CancellationToken);

        // Idempotent: kun en ordre, der venter på lageret, kan skifte status. Et svar, der kommer igen, ignoreres.
        if (order is null || order.Status != OrderStatus.AwaitingStock)
        {
            _logger.LogInformation("StockReserved for order {OrderId} ignored ({Status})",
                context.Message.OrderId, order?.Status ?? "unknown order");
            return;
        }

        order.Status = OrderStatus.Confirmed;
        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Order {OrderId} confirmed", order.Id);
    }
}

/// <summary>
/// Lageret havde ikke nok af mindst én vare: ordren er afvist. Årsagen bygges med varenavnene,
/// som Ordering.API har på ordrelinjerne (lageret kender kun katalog-id).
/// </summary>
public class StockReservationRejectedConsumer : IConsumer<StockReservationRejected>
{
    private readonly OrderingDbContext _db;
    private readonly ILogger<StockReservationRejectedConsumer> _logger;

    public StockReservationRejectedConsumer(OrderingDbContext db, ILogger<StockReservationRejectedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockReservationRejected> context)
    {
        var message = context.Message;
        var order = await _db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == message.OrderId, context.CancellationToken);

        if (order is null || order.Status != OrderStatus.AwaitingStock)
        {
            _logger.LogInformation("StockReservationRejected for order {OrderId} ignored ({Status})",
                message.OrderId, order?.Status ?? "unknown order");
            return;
        }

        order.Status = OrderStatus.Rejected;
        order.RejectionReason = string.Join("; ", message.Shortages.Select(shortage =>
        {
            var name = order.Lines.FirstOrDefault(line => line.CatalogItemId == shortage.CatalogItemId)?.ProductName
                ?? $"Item {shortage.CatalogItemId}";
            return $"{name}: ordered {shortage.Requested}, available {shortage.Available}";
        }));

        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Order {OrderId} rejected: {RejectionReason}", order.Id, order.RejectionReason);
    }
}
