using BasketApi.Services;
using Contracts;
using MassTransit;

namespace BasketApi.Messaging;

/// <summary>
/// Command via Request-Reply: lægger en vare i kurven og svarer med kurven bagefter,
/// så Web kan vise den opdaterede kurv med det samme.
/// </summary>
public class AddItemToBasketConsumer : IConsumer<AddItemToBasket>
{
    private readonly IBasketService _basketService;
    private readonly ILogger<AddItemToBasketConsumer> _logger;

    public AddItemToBasketConsumer(IBasketService basketService, ILogger<AddItemToBasketConsumer> logger)
    {
        _basketService = basketService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AddItemToBasket> context)
    {
        var message = context.Message;

        // Ugyldigt indhold er et normalt svar (BasketRequestRejected), ikke en exception.
        // En exception ville blive prøvet igen og ende i fejlkøen, selvom et nyt forsøg aldrig kan lykkes.
        if (message.Quantity < 1 || message.UnitPrice < 0)
        {
            _logger.LogWarning("Rejected adding catalog item {CatalogItemId}: quantity {Quantity}, unit price {UnitPrice}",
                message.CatalogItemId, message.Quantity, message.UnitPrice);
            await context.RespondAsync(new BasketRequestRejected
            {
                Reason = "Quantity must be at least 1 and UnitPrice cannot be negative."
            });
            return;
        }

        var basket = await _basketService.AddItemToBasket(message.BuyerId, message.CatalogItemId, message.UnitPrice, message.Quantity);
        await context.RespondAsync(BasketSnapshotMapper.ToSnapshot(basket));
    }
}
