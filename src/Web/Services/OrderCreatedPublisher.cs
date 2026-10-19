using Contracts;
using MassTransit;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Web.Interfaces;

namespace Microsoft.eShopWeb.Web.Services;

public class OrderCreatedPublisher : IOrderCreatedPublisher
{
    // How long the checkout waits for RabbitMQ before giving up.
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);

    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<OrderCreatedPublisher> _logger;

    public OrderCreatedPublisher(IPublishEndpoint publishEndpoint, ILogger<OrderCreatedPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishAsync(Order order)
    {
        // Translate the monolith's Order (EF entity) to the contract (simple data).
        var message = new OrderCreated
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            ShipToAddress = new OrderCreatedAddress
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Lines = order.OrderItems.Select(item => new OrderCreatedLine
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                PictureUri = item.ItemOrdered.PictureUri,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            }).ToList()
        };

        // Send the message to the message broker (RabbitMQ) with a timeout.
        using var timeout = new CancellationTokenSource(PublishTimeout);
        try
        {
            await _publishEndpoint.Publish(message, timeout.Token);
            _logger.LogInformation("OrderCreated publiceret for ordre {OrderId}.", order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Kunne ikke publicere OrderCreated for ordre {OrderId}. Ordren er gemt i monolitten, men Ordering.API får ikke besked.",
                order.Id);
        }
    }
}
