using Contracts;
using MassTransit;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Web.Services;

/// <summary>
/// Circuit Breaker (krav 7): når Basket.API ikke svarer, skal Web holde op med at spørge i et stykke tid.
/// </summary>
public class BasketMessagingClientCircuitBreaker
{
    private readonly IRequestClient<GetBasket> _getBasket = Substitute.For<IRequestClient<GetBasket>>();

    [Fact]
    public async Task OpensAfterTwoTimeoutsAndStopsAskingBasketApi()
    {
        _getBasket.GetResponse<BasketSnapshot>(Arg.Any<GetBasket>(), Arg.Any<CancellationToken>(), Arg.Any<RequestTimeout>())
            .Returns(Task.FromException<Response<BasketSnapshot>>(new RequestTimeoutException("test")));
        var client = CreateClient();

        // To timeouts: Basket.API bliver spurgt begge gange.
        var first = await Assert.ThrowsAsync<BasketUnavailableException>(() => client.GetBasketAsync("buyer"));
        var second = await Assert.ThrowsAsync<BasketUnavailableException>(() => client.GetBasketAsync("buyer"));
        Assert.False(first.CircuitOpen);
        Assert.False(second.CircuitOpen);

        // Nu er kredsløbet åbent: det tredje kald afvises med det samme, uden at Basket.API bliver spurgt.
        var third = await Assert.ThrowsAsync<BasketUnavailableException>(() => client.GetBasketAsync("buyer"));
        Assert.True(third.CircuitOpen);
        await _getBasket.Received(2).GetResponse<BasketSnapshot>(Arg.Any<GetBasket>(), Arg.Any<CancellationToken>(), Arg.Any<RequestTimeout>());
    }

    [Fact]
    public async Task StaysClosedWhenBasketApiAnswers()
    {
        // Svaret laves FØR Returns: NSubstitute tillader ikke, at en anden substitute sættes op inde i Returns(...).
        var answer = ResponseWith(new BasketSnapshot { BasketId = 0, BuyerId = "buyer" });
        _getBasket.GetResponse<BasketSnapshot>(Arg.Any<GetBasket>(), Arg.Any<CancellationToken>(), Arg.Any<RequestTimeout>())
            .Returns(Task.FromResult(answer));
        var client = CreateClient();

        for (var call = 0; call < 3; call++)
        {
            var basket = await client.GetBasketAsync("buyer");
            Assert.Equal(0, basket.BasketId);
        }

        await _getBasket.Received(3).GetResponse<BasketSnapshot>(Arg.Any<GetBasket>(), Arg.Any<CancellationToken>(), Arg.Any<RequestTimeout>());
    }

    private BasketMessagingClient CreateClient()
    {
        return new BasketMessagingClient(
            _getBasket,
            Substitute.For<IRequestClient<AddItemToBasket>>(),
            Substitute.For<IRequestClient<SetBasketQuantities>>(),
            Substitute.For<IRequestClient<TransferBasket>>(),
            new BasketCircuitBreaker(NullLogger<BasketCircuitBreaker>.Instance),
            NullLogger<BasketMessagingClient>.Instance);
    }

    private static Response<BasketSnapshot> ResponseWith(BasketSnapshot snapshot)
    {
        var response = Substitute.For<Response<BasketSnapshot>>();
        response.Message.Returns(snapshot);
        return response;
    }
}
