using BasketApi.Domain;
using Contracts;

namespace BasketApi.Messaging;

// Message Translator: oversætter Basket.API's egen model (EF-entitet) til kontrakten i Contracts,
// som Web forstår. Samme rolle som OrderCreatedMapper i Ordering.API, bare den anden vej.
public static class BasketSnapshotMapper
{
    public static BasketSnapshot ToSnapshot(Basket basket)
    {
        return new BasketSnapshot
        {
            BasketId = basket.Id,
            BuyerId = basket.BuyerId,
            Items = basket.Items.Select(item => new BasketSnapshotItem
            {
                BasketItemId = item.Id,
                CatalogItemId = item.CatalogItemId,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity
            }).ToList()
        };
    }

    // Køberen har ingen kurv endnu.
    public static BasketSnapshot Empty(string buyerId)
    {
        return new BasketSnapshot { BasketId = 0, BuyerId = buyerId };
    }
}
