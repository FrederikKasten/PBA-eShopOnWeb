using Ardalis.GuardClauses;
using Ardalis.Result;
using BasketApi.Data;
using BasketApi.Domain;
using BasketApi.Specifications;

namespace BasketApi.Services;

// Flyttet fra ApplicationCore/Services/BasketService.cs.
// Ændringer: IAppLogger<T> er skiftet til ILogger<T> fra .NET, og der logges ved hvert udfald.
// Log-beskederne bruger named placeholders ({BasketId} osv.), så hver værdi bliver sit eget
// felt i Elasticsearch (basketId, catalogItemId, ...). Se elk/LOGGING.md, trin 8.
// BuyerId logges ikke: for indloggede brugere er det deres e-mail. BasketId er nok til at følge en kurv.
public class BasketService : IBasketService
{
    private readonly IRepository<Basket> _basketRepository;
    private readonly ILogger<BasketService> _logger;

    public BasketService(IRepository<Basket> basketRepository,
        ILogger<BasketService> logger)
    {
        _basketRepository = basketRepository;
        _logger = logger;
    }

    public async Task<Basket?> GetBasketAsync(string buyerId)
    {
        return await _basketRepository.FirstOrDefaultAsync(new BasketWithItemsSpecification(buyerId));
    }

    public async Task<Basket> AddItemToBasket(string username, int catalogItemId, decimal price, int quantity = 1)
    {
        var basketSpec = new BasketWithItemsSpecification(username);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);

        if (basket == null)
        {
            basket = new Basket(username);
            await _basketRepository.AddAsync(basket);
            _logger.LogInformation("Created basket {BasketId}", basket.Id);
        }

        basket.AddItem(catalogItemId, price, quantity);

        await _basketRepository.UpdateAsync(basket);
        _logger.LogInformation("Added {Quantity} of catalog item {CatalogItemId} to basket {BasketId}",
            quantity, catalogItemId, basket.Id);
        return basket;
    }

    public async Task DeleteBasketAsync(int basketId)
    {
        var basket = await _basketRepository.GetByIdAsync(basketId);
        Guard.Against.Null(basket, nameof(basket));
        await _basketRepository.DeleteAsync(basket);
        _logger.LogInformation("Deleted basket {BasketId}", basketId);
    }

    public async Task<Result<Basket>> SetQuantities(int basketId, Dictionary<string, int> quantities)
    {
        var basketSpec = new BasketWithItemsSpecification(basketId);
        var basket = await _basketRepository.FirstOrDefaultAsync(basketSpec);
        if (basket == null)
        {
            _logger.LogWarning("Basket {BasketId} not found when setting quantities", basketId);
            return Result<Basket>.NotFound();
        }

        foreach (var item in basket.Items)
        {
            if (quantities.TryGetValue(item.Id.ToString(), out var quantity))
            {
                _logger.LogInformation("Updating quantity of item {ItemId} to {Quantity}", item.Id, quantity);
                item.SetQuantity(quantity);
            }
        }

        var linesBefore = basket.Items.Count;
        basket.RemoveEmptyItems();
        await _basketRepository.UpdateAsync(basket);
        _logger.LogInformation("Updated quantities in basket {BasketId}: {RemovedLineCount} lines removed, {LineCount} lines left",
            basketId, linesBefore - basket.Items.Count, basket.Items.Count);
        return basket;
    }

    public async Task TransferBasketAsync(string anonymousId, string userName)
    {
        var anonymousBasketSpec = new BasketWithItemsSpecification(anonymousId);
        var anonymousBasket = await _basketRepository.FirstOrDefaultAsync(anonymousBasketSpec);
        if (anonymousBasket == null)
        {
            // Ikke en fejl: brugeren har bare ikke lagt noget i kurven før login.
            _logger.LogInformation("No anonymous basket to transfer");
            return;
        }
        var userBasketSpec = new BasketWithItemsSpecification(userName);
        var userBasket = await _basketRepository.FirstOrDefaultAsync(userBasketSpec);
        if (userBasket == null)
        {
            userBasket = new Basket(userName);
            await _basketRepository.AddAsync(userBasket);
            _logger.LogInformation("Created basket {BasketId}", userBasket.Id);
        }
        foreach (var item in anonymousBasket.Items)
        {
            userBasket.AddItem(item.CatalogItemId, item.UnitPrice, item.Quantity);
        }
        // Tælles før sletningen, så tallet ikke afhænger af hvad EF gør med de slettede linjer.
        var transferredLineCount = anonymousBasket.Items.Count;
        await _basketRepository.UpdateAsync(userBasket);
        await _basketRepository.DeleteAsync(anonymousBasket);
        _logger.LogInformation("Transferred {LineCount} lines from basket {AnonymousBasketId} to basket {UserBasketId}",
            transferredLineCount, anonymousBasket.Id, userBasket.Id);
    }
}
