using Contracts;
using MassTransit;
using Microsoft.eShopWeb.Web.Interfaces;
using Polly.CircuitBreaker;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Taler med Basket.API via RabbitMQ med Request-Reply.
/// Hver request sendes direkte til sin egen kø (adresserne står i ConfigureMessaging).
/// MassTransit sætter selv:
///  - ResponseAddress (Return Address): Web's egen svar-kø, så Basket.API ved hvor svaret skal hen.
///  - RequestId (Correlation Identifier): så svaret kan matches med den request, der venter på det.
///  - TimeToLive = timeout (Message Expiration): en request ingen længere venter på, bliver ikke behandlet.
/// Alle kald går gennem BasketCircuitBreaker (krav 7). Svarer Basket.API ikke inden timeout (RequestTimeoutException),
/// fejler den efter sine genforsøg (RequestFaultException), eller er kredsløbet åbent (BrokenCircuitException),
/// oversættes det til BasketUnavailableException, så kalderne ikke skal kende MassTransit eller Polly.
/// </summary>
public class BasketMessagingClient : IBasketClient
{
    private readonly IRequestClient<GetBasket> _getBasket;
    private readonly IRequestClient<AddItemToBasket> _addItem;
    private readonly IRequestClient<SetBasketQuantities> _setQuantities;
    private readonly IRequestClient<TransferBasket> _transfer;
    private readonly BasketCircuitBreaker _circuitBreaker;
    private readonly ILogger<BasketMessagingClient> _logger;

    public BasketMessagingClient(
        IRequestClient<GetBasket> getBasket,
        IRequestClient<AddItemToBasket> addItem,
        IRequestClient<SetBasketQuantities> setQuantities,
        IRequestClient<TransferBasket> transfer,
        BasketCircuitBreaker circuitBreaker,
        ILogger<BasketMessagingClient> logger)
    {
        _getBasket = getBasket;
        _addItem = addItem;
        _setQuantities = setQuantities;
        _transfer = transfer;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    public Task<BasketSnapshot> GetBasketAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return GetBasketAsync(buyerId, afterOrderId: null, cancellationToken);
    }

    public async Task<int> CountTotalBasketItemsAsync(string buyerId, int? afterOrderId = null, CancellationToken cancellationToken = default)
    {
        var basket = await GetBasketAsync(buyerId, afterOrderId, cancellationToken);
        return basket.Items.Sum(item => item.Quantity);
    }

    private async Task<BasketSnapshot> GetBasketAsync(string buyerId, int? afterOrderId, CancellationToken cancellationToken)
    {
        var request = new GetBasket { BuyerId = buyerId, AfterOrderId = afterOrderId };
        var response = await SendAsync(token => _getBasket.GetResponse<BasketSnapshot>(request, token), cancellationToken);
        return response.Message;
    }

    public async Task<BasketSnapshot> AddItemAsync(string buyerId, int catalogItemId, decimal unitPrice, int quantity = 1, CancellationToken cancellationToken = default)
    {
        var request = new AddItemToBasket
        {
            BuyerId = buyerId,
            CatalogItemId = catalogItemId,
            UnitPrice = unitPrice,
            Quantity = quantity
        };

        // To mulige svar: kurven (accepteret) eller en afvisning.
        var response = await SendAsync(token => _addItem.GetResponse<BasketSnapshot, BasketRequestRejected>(request, token), cancellationToken);
        if (response.Is(out Response<BasketSnapshot>? accepted))
        {
            return accepted.Message;
        }

        // Web sender altid antal 1 og prisen fra kataloget, så en afvisning her er en fejl i koden.
        var reason = ReasonFor(response);
        _logger.LogError("Basket.API rejected adding catalog item {CatalogItemId}: {RejectionReason}", catalogItemId, reason);
        throw new InvalidOperationException($"Basket.API rejected adding catalog item {catalogItemId}: {reason}");
    }

    public async Task<BasketSnapshot?> SetQuantitiesAsync(int basketId, IReadOnlyCollection<BasketQuantity> quantities, CancellationToken cancellationToken = default)
    {
        var request = new SetBasketQuantities { BasketId = basketId, Quantities = quantities.ToList() };

        var response = await SendAsync(token => _setQuantities.GetResponse<BasketSnapshot, BasketRequestRejected>(request, token), cancellationToken);
        if (response.Is(out Response<BasketSnapshot>? accepted))
        {
            return accepted.Message;
        }

        _logger.LogWarning("Basket.API rejected new quantities for basket {BasketId}: {RejectionReason}", basketId, ReasonFor(response));
        return null;
    }

    public async Task TransferBasketAsync(string anonymousBuyerId, string userName, CancellationToken cancellationToken = default)
    {
        var request = new TransferBasket { AnonymousBuyerId = anonymousBuyerId, UserName = userName };

        var response = await SendAsync(token => _transfer.GetResponse<BasketSnapshot, BasketRequestRejected>(request, token), cancellationToken);
        if (!response.Is(out Response<BasketSnapshot>? _))
        {
            _logger.LogWarning("Basket.API rejected the basket transfer: {RejectionReason}", ReasonFor(response));
        }
    }

    // Sender en request gennem Circuit Breakeren og oversætter fejl til BasketUnavailableException.
    private async Task<T> SendAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
    {
        try
        {
            return await _circuitBreaker.Pipeline.ExecuteAsync<T>(async token => await request(token), cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            throw new BasketUnavailableException("Basket.API is not called while the circuit is open", ex, circuitOpen: true);
        }
        catch (Exception ex) when (ex is MassTransitException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new BasketUnavailableException("Basket.API did not answer", ex, circuitOpen: false);
        }
    }

    private static string ReasonFor(Response<BasketSnapshot, BasketRequestRejected> response)
    {
        return response.Is(out Response<BasketRequestRejected>? rejected) ? rejected.Message.Reason : "unknown reason";
    }
}
