using BasketApi.Domain;

namespace BasketApi.Endpoints;

public record BasketItemDto(int Id, int CatalogItemId, decimal UnitPrice, int Quantity);

public record BasketDto(int Id, string BuyerId, List<BasketItemDto> Items)
{
    public static BasketDto From(Basket basket) => new(
        basket.Id,
        basket.BuyerId,
        basket.Items
            .Select(i => new BasketItemDto(i.Id, i.CatalogItemId, i.UnitPrice, i.Quantity))
            .ToList());

    // Køberen har ingen kurv endnu. Id 0 betyder "ikke gemt".
    public static BasketDto Empty(string buyerId) => new(0, buyerId, new List<BasketItemDto>());
}

public record AddBasketItemRequest(int CatalogItemId, decimal UnitPrice, int Quantity);

public record TransferBasketRequest(string AnonymousId, string UserName);
