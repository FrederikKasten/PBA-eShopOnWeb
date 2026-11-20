using BasketApi.Services;
using Contracts;
using MassTransit;

namespace BasketApi.Messaging;

/// <summary>
/// Command via Request-Reply: flytter den anonyme kurv til brugeren ved login og svarer med brugerens kurv.
/// Web VENTER på svaret, fordi login ofte sender brugeren direkte videre til checkout,
/// og der skal kurven allerede være flyttet.
/// </summary>
public class TransferBasketConsumer : IConsumer<TransferBasket>
{
    private readonly IBasketService _basketService;
    private readonly ILogger<TransferBasketConsumer> _logger;

    public TransferBasketConsumer(IBasketService basketService, ILogger<TransferBasketConsumer> logger)
    {
        _basketService = basketService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TransferBasket> context)
    {
        var message = context.Message;

        if (string.IsNullOrWhiteSpace(message.AnonymousBuyerId) || string.IsNullOrWhiteSpace(message.UserName))
        {
            _logger.LogWarning("Rejected basket transfer: anonymous id or user name is missing");
            await context.RespondAsync(new BasketRequestRejected { Reason = "AnonymousBuyerId and UserName are required." });
            return;
        }

        // BasketService logger selve overførslen (eller at der ikke var noget at overføre).
        await _basketService.TransferBasketAsync(message.AnonymousBuyerId, message.UserName);

        var userBasket = await _basketService.GetBasketAsync(message.UserName);
        await context.RespondAsync(userBasket is null
            ? BasketSnapshotMapper.Empty(message.UserName)
            : BasketSnapshotMapper.ToSnapshot(userBasket));
    }
}
