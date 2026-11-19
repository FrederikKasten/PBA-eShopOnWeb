using Contracts;
using Microsoft.eShopWeb.Web.Interfaces;

namespace Microsoft.eShopWeb.FunctionalTests.Web;

/// <summary>
/// Erstatter Basket.API og RabbitMQ i de funktionelle tests, så Web kan testes uden at de kører.
/// Opfører sig som BasketService: én kurv pr. køber, antal 0 fjerner varen, transfer lægger kurvene sammen.
/// Kurven tømmes ikke ved checkout – det gør Basket.API, når den modtager OrderCreated.
/// </summary>
public class InMemoryBasketClient : IBasketClient
{
    private readonly object _lock = new();
    private readonly List<FakeBasket> _baskets = new();
    private int _nextBasketId = 1;
    private int _nextItemId = 1;

    private class FakeBasket
    {
        public int Id { get; init; }
        public string BuyerId { get; init; } = string.Empty;
        public List<BasketSnapshotItem> Items { get; } = new();
    }

    public Task<BasketSnapshot> GetBasketAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var basket = _baskets.FirstOrDefault(b => b.BuyerId == buyerId);
            return Task.FromResult(basket is null
                ? new BasketSnapshot { BasketId = 0, BuyerId = buyerId }
                : ToSnapshot(basket));
        }
    }

    // afterOrderId bruges ikke: her er der ingen ventetid mellem checkout og kurven.
    public Task<int> CountTotalBasketItemsAsync(string buyerId, int? afterOrderId = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var count = _baskets
                .Where(b => b.BuyerId == buyerId)
                .SelectMany(b => b.Items)
                .Sum(x => x.Quantity);
            return Task.FromResult(count);
        }
    }

    public Task<BasketSnapshot> AddItemAsync(string buyerId, int catalogItemId, decimal unitPrice, int quantity = 1, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var basket = GetOrCreate(buyerId);
            AddOrIncrease(basket, catalogItemId, unitPrice, quantity);
            return Task.FromResult(ToSnapshot(basket));
        }
    }

    public Task<BasketSnapshot?> SetQuantitiesAsync(int basketId, IReadOnlyCollection<BasketQuantity> quantities, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var basket = _baskets.FirstOrDefault(b => b.Id == basketId);
            if (basket is null || quantities.Any(q => q.Quantity < 0))
            {
                return Task.FromResult<BasketSnapshot?>(null);
            }

            for (var index = 0; index < basket.Items.Count; index++)
            {
                var current = basket.Items[index];
                var change = quantities.FirstOrDefault(q => q.BasketItemId == current.BasketItemId);
                if (change is not null)
                {
                    basket.Items[index] = current with { Quantity = change.Quantity };
                }
            }
            basket.Items.RemoveAll(x => x.Quantity == 0);

            return Task.FromResult<BasketSnapshot?>(ToSnapshot(basket));
        }
    }

    public Task TransferBasketAsync(string anonymousBuyerId, string userName, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var anonymousBasket = _baskets.FirstOrDefault(b => b.BuyerId == anonymousBuyerId);
            if (anonymousBasket is not null)
            {
                var userBasket = GetOrCreate(userName);
                foreach (var x in anonymousBasket.Items)
                {
                    AddOrIncrease(userBasket, x.CatalogItemId, x.UnitPrice, x.Quantity);
                }
                _baskets.Remove(anonymousBasket);
            }
        }
        return Task.CompletedTask;
    }

    private FakeBasket GetOrCreate(string buyerId)
    {
        var basket = _baskets.FirstOrDefault(b => b.BuyerId == buyerId);
        if (basket is null)
        {
            basket = new FakeBasket { Id = _nextBasketId++, BuyerId = buyerId };
            _baskets.Add(basket);
        }
        return basket;
    }

    private void AddOrIncrease(FakeBasket basket, int catalogItemId, decimal unitPrice, int quantity)
    {
        var index = basket.Items.FindIndex(x => x.CatalogItemId == catalogItemId);
        if (index < 0)
        {
            basket.Items.Add(new BasketSnapshotItem
            {
                BasketItemId = _nextItemId++,
                CatalogItemId = catalogItemId,
                UnitPrice = unitPrice,
                Quantity = quantity
            });
        }
        else
        {
            var existing = basket.Items[index];
            basket.Items[index] = existing with { Quantity = existing.Quantity + quantity };
        }
    }

    private static BasketSnapshot ToSnapshot(FakeBasket basket) =>
        new() { BasketId = basket.Id, BuyerId = basket.BuyerId, Items = basket.Items.ToList() };
}
