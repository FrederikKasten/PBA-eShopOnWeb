using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Web.Features.MyOrders;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MediatorHandlers.OrdersTests;

public class GetMyOrders
{
    private readonly IOrderingApiClient _mockOrderingApi = Substitute.For<IOrderingApiClient>();

    public GetMyOrders()
    {
        var address = new OrderingApiAddress("", "", "", "", "");
        IReadOnlyList<OrderingApiOrderSummary> orders = new List<OrderingApiOrderSummary>
        {
            new OrderingApiOrderSummary(1, "SomeUserName", DateTimeOffset.Now, 100m, "Received", address)
        };

        _mockOrderingApi.GetOrdersForBuyerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(orders);
    }

    [Fact]
    public async Task NotReturnNullIfOrdersArePresIent()
    {
        var request = new eShopWeb.Web.Features.MyOrders.GetMyOrders("SomeUserName");

        var handler = new GetMyOrdersHandler(_mockOrderingApi, NullLogger<GetMyOrdersHandler>.Instance);

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Received", result.First().Status);
    }
}
