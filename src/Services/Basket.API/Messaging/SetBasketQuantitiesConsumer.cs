using BasketApi.Services;
using Contracts;
using MassTransit;

namespace BasketApi.Messaging;

/// <summary>
/// Command via Request-Reply: sætter nye antal på kurvens linjer (0 fjerner linjen)
/// og svarer med kurven bagefter. Ukendt kurv eller negativt antal giver BasketRequestRejected.
/// </summary>
public class SetBasketQuantitiesConsumer : IConsumer<SetBasketQuantities>
{
    private readonly IBasketService _basketService;
    private readonly ILogger<SetBasketQuantitiesConsumer> _logger;

    public SetBasketQuantitiesConsumer(IBasketService basketService, ILogger<SetBasketQuantitiesConsumer> logger)
    {
        _basketService = basketService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SetBasketQuantities> context)
    {
        var message = context.Message;

        if (message.Quantities.Any(q => q.Quantity < 0))
        {
            _logger.LogWarning("Rejected negative quantity for basket {BasketId}", message.BasketId);
            await context.RespondAsync(new BasketRequestRejected { Reason = "Quantity cannot be negative." });
            return;
        }

        // Message Translator: kontraktens liste -> den ordbog BasketService har brugt siden monolitten.
        var quantities = message.Quantities.ToDictionary(q => q.BasketItemId.ToString(), q => q.Quantity);

        // Findes kurven ikke, logger BasketService det.
        var result = await _basketService.SetQuantities(message.BasketId, quantities);
        if (!result.IsSuccess)
        {
            await context.RespondAsync(new BasketRequestRejected { Reason = $"Basket {message.BasketId} was not found." });
            return;
        }

        await context.RespondAsync(BasketSnapshotMapper.ToSnapshot(result.Value));
    }
}
