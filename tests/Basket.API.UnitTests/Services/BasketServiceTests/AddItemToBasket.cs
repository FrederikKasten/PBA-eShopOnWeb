using System.Threading.Tasks;
using BasketApi.Data;
using BasketApi.Domain;
using BasketApi.Services;
using BasketApi.Specifications;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace BasketApi.UnitTests.Services.BasketServiceTests;

public class AddItemToBasket
{
    private readonly string _buyerId = "Test buyerId";
    private readonly IRepository<Basket> _mockBasketRepo = Substitute.For<IRepository<Basket>>();
    private readonly ILogger<BasketService> _mockLogger = Substitute.For<ILogger<BasketService>>();

    [Fact]
    public async Task InvokesBasketRepositoryGetBySpecAsyncOnce()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(1, 1.5m);

        _mockBasketRepo.FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default).Returns(basket);

        var basketService = new BasketService(_mockBasketRepo, _mockLogger);

        await basketService.AddItemToBasket(basket.BuyerId, 1, 1.50m);

        await _mockBasketRepo.Received().FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default);
    }

    [Fact]
    public async Task InvokesBasketRepositoryUpdateAsyncOnce()
    {
        var basket = new Basket(_buyerId);
        basket.AddItem(1, 1.1m, 1);
        _mockBasketRepo.FirstOrDefaultAsync(Arg.Any<BasketWithItemsSpecification>(), default).Returns(basket);

        var basketService = new BasketService(_mockBasketRepo, _mockLogger);

        await basketService.AddItemToBasket(basket.BuyerId, 1, 1.50m);

        await _mockBasketRepo.Received().UpdateAsync(basket, default);
    }
}
