namespace BasketApi.Domain;

public class Basket : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    private readonly List<BasketItem> _items = new List<BasketItem>();
    public IReadOnlyCollection<BasketItem> Items => _items.AsReadOnly();

    public int TotalItems => _items.Sum(i => i.Quantity);

    public Basket(string buyerId)
    {
        BuyerId = buyerId;
    }

    public void AddItem(int catalogItemId, decimal unitPrice, int quantity = 1)
    {
        if (!Items.Any(i => i.CatalogItemId == catalogItemId))
        {
            _items.Add(new BasketItem(catalogItemId, quantity, unitPrice));
            return;
        }
        var existingItem = Items.First(i => i.CatalogItemId == catalogItemId);
        existingItem.AddQuantity(quantity);
    }

    public void RemoveEmptyItems()
    {
        _items.RemoveAll(i => i.Quantity == 0);
    }

    // Bruges når en ordre er oprettet: træk de købte stk. fra. Antallet bliver aldrig negativt,
    // og varer lagt i kurven EFTER checkout bliver liggende. Kald RemoveEmptyItems bagefter.
    public void RemoveQuantity(int catalogItemId, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.CatalogItemId == catalogItemId);
        if (item == null)
        {
            return;
        }

        item.SetQuantity(Math.Max(0, item.Quantity - quantity));
    }

    public void SetNewBuyerId(string buyerId)
    {
        BuyerId = buyerId;
    }
}
