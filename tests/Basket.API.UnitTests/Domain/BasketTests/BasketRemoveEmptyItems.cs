using BasketApi.Domain;
using Xunit;

namespace BasketApi.UnitTests.Domain.BasketTests;

public class BasketRemoveEmptyItems
{
    private readonly int _testCatalogItemId = 123;
    private readonly decimal _testUnitPrice = 1.23m;
    private readonly string _buyerId = "Test buyerId";

    [Fact]
    public void RemovesEmptyBasketItems()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(_testCatalogItemId, _testUnitPrice, 0);
        basket.RemoveEmptyItems();

        Assert.Equal(0, basket.Items.Count);
    }
}
