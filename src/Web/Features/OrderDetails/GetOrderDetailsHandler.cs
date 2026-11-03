using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.ViewModels;

namespace Microsoft.eShopWeb.Web.Features.OrderDetails;

public class GetOrderDetailsHandler : IRequestHandler<GetOrderDetails, OrderDetailViewModel?>
{
    private readonly IOrderingApiClient _orderingApi;
    private readonly ILogger<GetOrderDetailsHandler> _logger;

    public GetOrderDetailsHandler(IOrderingApiClient orderingApi, ILogger<GetOrderDetailsHandler> logger)
    {
        _orderingApi = orderingApi;
        _logger = logger;
    }

    public async Task<OrderDetailViewModel?> Handle(GetOrderDetails request,
        CancellationToken cancellationToken)
    {
        OrderingApiOrderDetails? order;
        try
        {
            order = await _orderingApi.GetOrderAsync(request.OrderId, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Kunne ikke hente ordre {OrderId} fra Ordering.API.", request.OrderId);
            return null;
        }

        if (order == null || order.BuyerId != request.UserName)
        {
            return null;
        }

        return new OrderDetailViewModel
        {
            OrderNumber = order.OrderId,
            OrderDate = order.OrderDate,
            Total = order.Total,
            Status = order.Status,
            ShippingAddress = new Address(order.ShipToAddress.Street, order.ShipToAddress.City,
                order.ShipToAddress.State, order.ShipToAddress.Country, order.ShipToAddress.ZipCode),
            OrderItems = order.Lines.Select(l => new OrderItemViewModel
            {
                ProductId = l.CatalogItemId,
                ProductName = l.ProductName,
                PictureUrl = l.PictureUri,
                UnitPrice = l.UnitPrice,
                Units = l.Units
            }).ToList()
        };
    }
}

// Ud over at have tilføjet messaging,
// så er der også nyt i forhold til den gamle handler: den tjekker, at ordren tilhører den bruger, der er logget ind (order.BuyerId != request.UserName).
// Den gamle kode gjorde ikke det, så man kunne se andres ordrer ved at ændre nummeret i URL'en.
// - Niklas
