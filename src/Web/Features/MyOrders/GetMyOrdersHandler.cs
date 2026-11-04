using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Features.MyOrders;

public class GetMyOrdersHandler : IRequestHandler<GetMyOrders, IEnumerable<OrderViewModel>>
{
    private readonly IOrderingApiClient _orderingApi;
    private readonly ILogger<GetMyOrdersHandler> _logger;

    public GetMyOrdersHandler(IOrderingApiClient orderingApi, ILogger<GetMyOrdersHandler> logger)
    {
        _orderingApi = orderingApi;
        _logger = logger;
    }

    public async Task<IEnumerable<OrderViewModel>> Handle(GetMyOrders request,
        CancellationToken cancellationToken)
    {
        try
        {
            var orders = await _orderingApi.GetOrdersForBuyerAsync(request.UserName, cancellationToken);

            return orders.Select(o => new OrderViewModel
            {
                OrderNumber = o.OrderId,
                OrderDate = o.OrderDate,
                Total = o.Total,
                Status = o.Status,
                ShippingAddress = new Address(o.ShipToAddress.Street, o.ShipToAddress.City,
                    o.ShipToAddress.State, o.ShipToAddress.Country, o.ShipToAddress.ZipCode)
            }).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Ordering.Api does not respond: show an empty list instead of an error page.
            _logger.LogError(ex, "Kunne ikke hente ordrer fra Ordering.API for {UserName}.", request.UserName);
            return Enumerable.Empty<OrderViewModel>();
        }
    }
}
