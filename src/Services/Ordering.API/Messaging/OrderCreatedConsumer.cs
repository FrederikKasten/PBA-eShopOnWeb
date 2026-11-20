using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Data;
using Ordering.API.Domain;

namespace Ordering.API.Messaging;

/// <summary>
/// Kaldes af MassTransit, når der ligger en OrderCreated i køen ordering-order-created.
/// Gemmer ordren med status AwaitingStock og beder lageret om at reservere varerne (ReserveStock).
/// Ordering.API styrer dermed lagerdelen af checkout-sagaen.
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

        // RabbitMQ kan levere samme besked mere end én gang (Idempotent Receiver).
        var order = await _db.Orders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == message.OrderId, context.CancellationToken);

        if (order is null)
        {
            order = OrderCreatedMapper.ToOrder(message);
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(context.CancellationToken);
            _logger.LogInformation("Order {OrderId} saved with status {Status}", order.Id, order.Status);
        }
        else if (order.Status != OrderStatus.AwaitingStock)
        {
            // Lageret har allerede svaret. Intet at gøre.
            _logger.LogInformation("Order {OrderId} already exists with status {Status}; OrderCreated ignored", order.Id, order.Status);
            return;
        }
        else
        {
            // Ordren findes, men venter stadig på lageret. Måske gik ReserveStock tabt sidst,
            // så den sendes igen. Lageret svarer det samme, hvis det allerede har reserveret.
            _logger.LogInformation("Order {OrderId} is still awaiting stock; sending ReserveStock again", order.Id);
        }

        // Først gemme, så sende. Fejler Send, prøves beskeden igen, og så rammer vi grenen ovenfor.
        var inventory = await context.GetSendEndpoint(InventoryQueues.Address(InventoryQueues.ReserveStock));
        await inventory.Send(OrderCreatedMapper.ToReserveStock(order), context.CancellationToken);
    }
}
