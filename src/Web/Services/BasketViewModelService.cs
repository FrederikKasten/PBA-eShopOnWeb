using Contracts;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Pages.Basket;

namespace Microsoft.eShopWeb.Web.Services;

public class BasketViewModelService : IBasketViewModelService
{
    private readonly IBasketClient _basketClient;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly ILogger<BasketViewModelService> _logger;

    public BasketViewModelService(IBasketClient basketClient,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        ILogger<BasketViewModelService> logger)
    {
        _basketClient = basketClient;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    // Navnet er bevaret, men der oprettes ikke længere en tom kurv:
    // Basket.API svarer med en tom kurv (BasketId 0), og kurven oprettes først når der lægges en vare i.
    public async Task<BasketViewModel> GetOrCreateBasketForUser(string userName)
    {
        var basket = await _basketClient.GetBasketAsync(userName);
        return await Map(basket);
    }

    // Message Translator: kontrakten fra Basket.API -> den view model som Razor-siderne bruger.
    public async Task<BasketViewModel> Map(BasketSnapshot basket)
    {
        return new BasketViewModel()
        {
            BuyerId = basket.BuyerId,
            Id = basket.BasketId,
            Items = await GetBasketItems(basket.Items)
        };
    }

    public async Task<int> CountTotalBasketItems(string username, int? afterOrderId = null)
    {
        // Kurv-ikonet vises på alle sider. Svarer Basket.API ikke (eller er RabbitMQ nede),
        // viser vi 0 i stedet for at hele siden fejler.
        try
        {
            return await _basketClient.CountTotalBasketItemsAsync(username, afterOrderId);
        }
        catch (BasketUnavailableException ex) when (ex.CircuitOpen)
        {
            // Kredsløbet er åbent: der blev ikke engang spurgt. BasketCircuitBreaker har allerede logget, at det åbnede,
            // så her logges kun på Debug for ikke at fylde Kibana med én linje pr. side.
            _logger.LogDebug("Circuit to Basket.API is open; the basket icon shows 0");
            return 0;
        }
        catch (BasketUnavailableException ex)
        {
            _logger.LogWarning(ex, "Basket.API did not respond; the basket icon shows 0");
            return 0;
        }
    }

    // Produktnavn og billede kommer stadig fra kataloget i monolitten – Basket.API kender kun CatalogItemId.
    // Content Enricher: beskeden fra Basket.API beriges med data fra kataloget, før den vises.
    private async Task<List<BasketItemViewModel>> GetBasketItems(IReadOnlyCollection<BasketSnapshotItem> basketItems)
    {
        if (basketItems.Count == 0)
        {
            return new List<BasketItemViewModel>();
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(basketItems.Select(b => b.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var items = basketItems.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);

            var basketItemViewModel = new BasketItemViewModel
            {
                Id = basketItem.BasketItemId,
                UnitPrice = basketItem.UnitPrice,
                Quantity = basketItem.Quantity,
                CatalogItemId = basketItem.CatalogItemId,
                PictureUrl = _uriComposer.ComposePicUri(catalogItem.PictureUri),
                ProductName = catalogItem.Name
            };
            return basketItemViewModel;
        }).ToList();

        return items;
    }
}
