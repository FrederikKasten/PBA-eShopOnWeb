using Ardalis.Result;
using BasketApi.Domain;

namespace BasketApi.Services;

// Kernen i Basket.API. Både HTTP-endpoints og RabbitMQ-consumers kalder den (Service Activator),
// så reglerne for en kurv kun findes et sted, uanset hvordan kaldet kommer ind.
public interface IBasketService
{
    // null = køberen har ingen kurv. Der oprettes ikke en tom kurv ved læsning.
    Task<Basket?> GetBasketAsync(string buyerId);
    Task TransferBasketAsync(string anonymousId, string userName);
    Task<Basket> AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1);
    Task<Result<Basket>> SetQuantities(int basketId, Dictionary<string, int> quantities);
    Task DeleteBasketAsync(int basketId);
}
