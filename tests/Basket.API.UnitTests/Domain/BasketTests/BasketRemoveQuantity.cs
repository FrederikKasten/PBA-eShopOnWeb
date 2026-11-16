using BasketApi.Domain;
using Xunit;

namespace BasketApi.UnitTests.Domain.BasketTests;

public class BasketRemoveQuantity
{
    private readonly int _testCatalogItemId = 123;
    private readonly decimal _testUnitPrice = 1.23m;
    private readonly string _buyerId = "Test buyerId";

    [Fact]
    public void ReducesQuantityOfMatchingItem()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, 3);

        basket.RemoveQuantity(_testCatalogItemId, 2);

        Assert.Equal(1, basket.Items.Single().Quantity);
    }

    [Fact]
    public void NeverGoesBelowZero()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, 1);

        basket.RemoveQuantity(_testCatalogItemId, 5);
        basket.RemoveEmptyItems();

        Assert.Empty(basket.Items);
    }

    [Fact]
    public void IgnoresItemsNotInTheBasket()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, 1);

        basket.RemoveQuantity(catalogItemId: 999, quantity: 1);

        Assert.Equal(1, basket.Items.Single().Quantity);
    }
}
