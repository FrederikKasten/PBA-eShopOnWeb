using BasketApi.Data;
using BasketApi.Services;
using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BasketApi.Messaging;

/// <summary>
/// Request-Reply: svarer på GetBasket fra køen basket-get-basket.
/// MassTransit sender svaret til den adresse, der står i requestens ResponseAddress (Return Address),
/// med samme RequestId (Correlation Identifier), så Web ved hvilken request svaret hører til.
///
/// Read-your-writes: har requesten AfterOrderId, venter consumeren (højst MaxWait), til den ordre står i
/// ProcessedOrders. Så viser kurv-ikonet lige efter checkout ikke de varer, der netop er købt.
/// RemoveOrderedItemsConsumer gemmer kurv-ændringen og ProcessedOrder i samme SaveChanges, så når ordren
/// står der, er kurven også opdateret.
/// </summary>
public class GetBasketConsumer : IConsumer<GetBasket>
{
    // Skal være et godt stykke under Web's request-timeout på 5 sekunder.
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(100);

    private readonly IBasketService _basketService;
    private readonly BasketDbContext _db;
    private readonly ILogger<GetBasketConsumer> _logger;

    public GetBasketConsumer(IBasketService basketService, BasketDbContext db, ILogger<GetBasketConsumer> logger)
    {
        _basketService = basketService;
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GetBasket> context)
    {
        if (context.Message.AfterOrderId is int orderId)
        {
            await WaitUntilProcessedAsync(orderId, context.CancellationToken);
        }

        // Ellers ingen logning her: den kaldes for hver side Web viser (kurv-ikonet), så den ville drukne alt andet.
        var basket = await _basketService.GetBasketAsync(context.Message.BuyerId);

        await context.RespondAsync(basket is null
            ? BasketSnapshotMapper.Empty(context.Message.BuyerId)
            : BasketSnapshotMapper.ToSnapshot(basket));
    }

    private async Task WaitUntilProcessedAsync(int orderId, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + MaxWait;
        while (!await _db.ProcessedOrders.AnyAsync(p => p.OrderId == orderId, cancellationToken))
        {
            if (DateTime.UtcNow >= deadline)
            {
                // Ikke en fejl: kurven vises, som den er nu, og er rettet ved næste side.
                _logger.LogWarning("Order {OrderId} was not processed within {WaitMilliseconds} ms; replying with the basket as it is",
                    orderId, MaxWait.TotalMilliseconds);
                return;
            }

            await Task.Delay(PollDelay, cancellationToken);
        }
    }
}
