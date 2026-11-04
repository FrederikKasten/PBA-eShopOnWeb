using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Data;

namespace Ordering.API.Messaging;

/// <summary>
/// Called by MassTransit whenever there is an OrderCreated message in the queue.
/// </summary>
public class OrderCreatedConsumer : IConsumer<OrderCreated>
{
    private readonly OrderingDbContext _db;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(OrderingDbContext db, ILogger<OrderCreatedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var message = context.Message;

        // RabbitMQ kan levere samme besked mere end en gang.
        // Har vi allerede ordren, gør vi ingenting (consumeren er "idempotent").
        var alreadyExists = await _db.Orders.AnyAsync(o => o.Id == message.OrderId, context.CancellationToken);
        if (alreadyExists)
        {
            _logger.LogInformation("Ordre {OrderId} findes allerede – beskeden ignoreres.", message.OrderId);
            return;
        }

        var order = OrderCreatedMapper.ToOrder(message);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Ordre {OrderId} for {BuyerId} gemt med status {Status}.",
            order.Id, order.BuyerId, order.Status);
    }
}
