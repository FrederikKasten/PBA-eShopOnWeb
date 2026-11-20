using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderService : IOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<CatalogItem> _itemRepository;

    // IRepository<Basket> er fjernet: monolitten har ikke længere adgang til kurven.
    public OrderService(IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _itemRepository = itemRepository;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderItemRequest> items, Address shippingAddress)
    {
        Guard.Against.EmptyBasketOnCheckout(items);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var catalogItemsSpecification = new CatalogItemsSpecification(items.Select(item => item.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var orderItems = items.Select(basketItem =>
        {
            var catalogItem = catalogItems.First(c => c.Id == basketItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            var orderItem = new OrderItem(itemOrdered, basketItem.UnitPrice, basketItem.Quantity);
            return orderItem;
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);

        await _orderRepository.AddAsync(order);

        return order;
    }
}
