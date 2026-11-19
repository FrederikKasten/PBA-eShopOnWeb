using System.Text.Json;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Web.Interfaces;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Transactional Outbox: ordren og beskeden OrderCreated gemmes i SAMME transaktion i monolittens database.
/// Enten findes begge, eller ingen af dem. Er RabbitMQ nede, venter beskeden i tabellen OutboxMessages,
/// til OutboxDispatcher kan sende den (Guaranteed Delivery).
/// Før blev ordren gemt og beskeden sendt bagefter: var RabbitMQ nede netop da, fandtes ordren,
/// men hverken Ordering.API eller Basket.API hørte om den.
/// </summary>
public class OrderPlacementService : IOrderPlacementService
{
    private readonly CatalogContext _db;
    private readonly IOrderService _orderService;
    private readonly OutboxSignal _outboxSignal;
    private readonly ILogger<OrderPlacementService> _logger;

    public OrderPlacementService(CatalogContext db,
        IOrderService orderService,
        OutboxSignal outboxSignal,
        ILogger<OrderPlacementService> logger)
    {
        _db = db;
        _orderService = orderService;
        _outboxSignal = outboxSignal;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderItemRequest> items, Address shipToAddress)
    {
        // In-memory-databasen (UseOnlyInMemoryDatabase og de funktionelle tests) kan ikke lave transaktioner.
        // Der gemmes ordre og besked stadig begge to, bare ikke atomart.
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync()
            : null;

        // 1) Ordren gemmes og får sit Id. OrderService bruger samme CatalogContext, så det sker i transaktionen.
        var order = await _orderService.CreateOrderAsync(buyerId, items, shipToAddress);

        // 2) Beskeden lægges i outboxen. Den skal have ordrens Id, derfor først nu.
        var outboxMessage = new OutboxMessage(typeof(OrderCreated).FullName!, JsonSerializer.Serialize(ToOrderCreated(order)));
        _db.OutboxMessages.Add(outboxMessage);
        await _db.SaveChangesAsync();

        // 3) Først her bliver ordre og besked synlige. Fejler noget før, ruller transaktionen begge tilbage.
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        _logger.LogInformation("OrderCreated for order {OrderId} stored in the outbox as {OutboxMessageId}",
            order.Id, outboxMessage.Id);

        // Væk OutboxDispatcher, så beskeden sendes nu og ikke først ved næste runde.
        _outboxSignal.Notify();

        return order;
    }

    // Message Translator: monolittens Order (EF-entitet) -> kontrakten (simple data).
    private static OrderCreated ToOrderCreated(Order order)
    {
        return new OrderCreated
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            ShipToAddress = new OrderCreatedAddress
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Lines = order.OrderItems.Select(item => new OrderCreatedLine
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                PictureUri = item.ItemOrdered.PictureUri,
                UnitPrice = item.UnitPrice,
                Units = item.Units
            }).ToList()
        };
    }
}
