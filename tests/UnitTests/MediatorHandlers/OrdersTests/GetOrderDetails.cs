using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Web.Features.OrderDetails;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.MediatorHandlers.OrdersTests;

public class GetOrderDetails
{
    private readonly IOrderingApiClient _mockOrderingApi = Substitute.For<IOrderingApiClient>();

    public GetOrderDetails()
    {
        var address = new OrderingApiAddress("", "", "", "", "");
        var lines = new List<OrderingApiOrderLine> { new OrderingApiOrderLine(1, "ProductName", "URI", 10.00m, 10) };
        var order = new OrderingApiOrderDetails(0, "SomeUserName", DateTimeOffset.Now, 100m, "Received", address, lines);

        _mockOrderingApi.GetOrderAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(order);
    }

    [Fact]
    public async Task NotBeNullIfOrderExists()
    {
        var request = new eShopWeb.Web.Features.OrderDetails.GetOrderDetails("SomeUserName", 0);

        var handler = new GetOrderDetailsHandler(_mockOrderingApi, NullLogger<GetOrderDetailsHandler>.Instance);

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task BeNullIfOrderBelongsToAnotherUser()
    {
        var request = new eShopWeb.Web.Features.OrderDetails.GetOrderDetails("AnotherUser", 0);

        var handler = new GetOrderDetailsHandler(_mockOrderingApi, NullLogger<GetOrderDetailsHandler>.Instance);

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.Null(result);
    }
}
