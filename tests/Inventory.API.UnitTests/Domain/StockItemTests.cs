using Inventory.API.Domain;
using Xunit;

namespace Inventory.API.UnitTests.Domain;

public class StockItemTests
{
    [Fact]
    public void Reserve_with_enough_stock_lowers_available()
    {
        var item = new StockItem(catalogItemId: 1, onHand: 5);

        item.Reserve(3);

        Assert.Equal(3, item.Reserved);
        Assert.Equal(2, item.Available);
        Assert.Equal(5, item.OnHand);
    }

    [Fact]
    public void Reserve_exactly_what_is_available_leaves_zero()
    {
        var item = new StockItem(catalogItemId: 1, onHand: 2);

        item.Reserve(2);

        Assert.Equal(0, item.Available);
    }

    [Fact]
    public void Reserve_more_than_available_throws_and_changes_nothing()
    {
        var item = new StockItem(catalogItemId: 1, onHand: 2);

        Assert.Throws<InvalidOperationException>(() => item.Reserve(3));
        Assert.Equal(0, item.Reserved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_zero_or_negative_throws(int units)
    {
        var item = new StockItem(catalogItemId: 1, onHand: 2);

        Assert.Throws<InvalidOperationException>(() => item.Reserve(units));
    }

    [Fact]
    public void Release_gives_the_units_back()
    {
        var item = new StockItem(catalogItemId: 1, onHand: 5);
        item.Reserve(3);

        item.Release(3);

        Assert.Equal(0, item.Reserved);
        Assert.Equal(5, item.Available);
    }

    [Fact]
    public void Release_more_than_reserved_throws()
    {
        var item = new StockItem(catalogItemId: 1, onHand: 5);
        item.Reserve(1);

        Assert.Throws<InvalidOperationException>(() => item.Release(2));
    }

    [Fact]
    public void Every_change_increments_the_version()
    {
        var item = new StockItem(catalogItemId: 1, onHand: 5);

        item.Reserve(1);
        item.Release(1);
        item.Restock(4);

        Assert.Equal(3, item.Version);
        Assert.Equal(9, item.OnHand);
    }
}
