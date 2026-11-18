using BasketApi.Data;
using BasketApi.Domain;
using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BasketApi.Messaging;

/// <summary>
/// Event-Driven Consumer på Publish-Subscribe-kanalen: Web publicerer OrderCreated på topic exchangen,
/// og RabbitMQ lægger en kopi i både Ordering.API's køer og Basket.API's kø basket-order-created.
/// Basket.API trækker de købte varer fra køberens kurv. Web skal derfor ikke længere selv slette kurven
/// efter checkout, og Web og Basket.API kender ikke hinanden i dette flow.
/// </summary>
public class RemoveOrderedItemsConsumer : IConsumer<OrderCreated>
{
    private readonly BasketDbContext _db;
    private readonly ILogger<RemoveOrderedItemsConsumer> _logger;

    public RemoveOrderedItemsConsumer(BasketDbContext db, ILogger<RemoveOrderedItemsConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var message = context.Message;

        // Idempotent Receiver: RabbitMQ kan levere samme besked mere end én gang.
        // Er ordren allerede behandlet, gør vi ingenting.
        var alreadyProcessed = await _db.ProcessedOrders.AnyAsync(p => p.OrderId == message.OrderId, context.CancellationToken);
        if (alreadyProcessed)
        {
            _logger.LogInformation("Order {OrderId} already processed; OrderCreated ignored", message.OrderId);
            return;
        }

        var basket = await _db.Baskets
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.BuyerId == message.BuyerId, context.CancellationToken);

        if (basket == null)
        {
            _logger.LogInformation("No basket to update for order {OrderId}", message.OrderId);
        }
        else
        {
            // Træk kun det købte fra – ikke hele kurven. Lægger brugeren en ny vare i kurven,
            // før beskeden når frem, bliver den liggende.
            foreach (var line in message.Lines)
            {
                basket.RemoveQuantity(line.CatalogItemId, line.Units);
            }
            basket.RemoveEmptyItems();

            if (basket.Items.Count == 0)
            {
                _db.Baskets.Remove(basket);
                _logger.LogInformation("Basket {BasketId} deleted after order {OrderId}", basket.Id, message.OrderId);
            }
            else
            {
                _logger.LogInformation("Removed ordered items from basket {BasketId} after order {OrderId}; {LineCount} lines left",
                    basket.Id, message.OrderId, basket.Items.Count);
            }
        }

        // Kurv-ændringen og "ordren er behandlet" gemmes i SAMME SaveChanges (én transaktion).
        // Går det galt, er ingen af delene gemt, og MassTransit prøver beskeden igen.
        _db.ProcessedOrders.Add(new ProcessedOrder(message.OrderId));
        await _db.SaveChangesAsync(context.CancellationToken);
    }
}
