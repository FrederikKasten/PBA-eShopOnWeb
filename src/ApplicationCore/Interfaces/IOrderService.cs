using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    // Kurven ligger nu i Basket.API, så ordren laves ud fra de linjer Web har hentet derfra
    // (før: basketId, og OrderService læste selv kurven i monolittens database).
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderItemRequest> items, Address shippingAddress);
}

// Én kurv-linje som OrderService skal lave en ordrelinje ud fra.
public record OrderItemRequest(int CatalogItemId, decimal UnitPrice, int Quantity);
