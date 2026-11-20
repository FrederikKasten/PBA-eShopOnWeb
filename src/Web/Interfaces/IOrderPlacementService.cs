using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Web.Interfaces;

public interface IOrderPlacementService
{
    // Opretter ordren og lægger OrderCreated i outboxen i samme transaktion.
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderItemRequest> items, Address shipToAddress);
}
