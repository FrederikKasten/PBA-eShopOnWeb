using System.Collections.Generic;
using System.Threading.Tasks;
using BasketApi.Data;
using BasketApi.Domain;
using BasketApi.Services;
using BasketApi.UnitTests.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketApi.UnitTests.Data;

// Flyttet fra IntegrationTests/Repositories/BasketRepositoryTests.
// Kører mod Basket.API's egen DbContext (in-memory) i stedet for monolittens CatalogContext.
public class SetQuantities
{
    private readonly BasketDbContext _basketContext;
    private readonly EfRepository<Basket> _basketRepository;
    private readonly BasketBuilder BasketBuilder = new BasketBuilder();

    public SetQuantities()
    {
        var dbOptions = new DbContextOptionsBuilder<BasketDbContext>()
            .UseInMemoryDatabase(databaseName: "TestBasket")
            .Options;
        _basketContext = new BasketDbContext(dbOptions);
        _basketRepository = new EfRepository<Basket>(_basketContext);
    }

    [Fact]
    public async Task RemoveEmptyQuantities()
    {
        var basket = BasketBuilder.WithOneBasketItem();
        var basketService = new BasketService(_basketRepository, NullLogger<BasketService>.Instance);
        await _basketRepository.AddAsync(basket);
        _basketContext.SaveChanges();

        await basketService.SetQuantities(BasketBuilder.BasketId, new Dictionary<string, int>() { { BasketBuilder.BasketId.ToString(), 0 } });

        Assert.Equal(0, basket.Items.Count);
    }
}
