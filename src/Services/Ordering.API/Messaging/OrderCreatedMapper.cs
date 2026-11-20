using Contracts;
using Ordering.API.Domain;

namespace Ordering.API.Messaging;
public static class OrderCreatedMapper
{
    public static Order ToOrder(OrderCreated message)
    {
        return new Order
        {
            Id = message.OrderId,
            BuyerId = message.BuyerId,
            OrderDate = message.OrderDate,
            Total = message.Total,
            Status = OrderStatus.AwaitingStock,
            ShipToStreet = message.ShipToAddress.Street,
            ShipToCity = message.ShipToAddress.City,
            ShipToState = message.ShipToAddress.State,
            ShipToCountry = message.ShipToAddress.Country,
            ShipToZipCode = message.ShipToAddress.ZipCode,
            Lines = message.Lines.Select(line => new OrderLine
            {
                CatalogItemId = line.CatalogItemId,
                ProductName = line.ProductName,
                PictureUri = line.PictureUri,
                UnitPrice = line.UnitPrice,
                Units = line.Units
            }).ToList()
        };
    }

    /// <summary>
    /// Content Filter: lageret får kun ordrens id og linjernes katalog-id og antal –
    /// ikke adresse, priser eller navne.
    /// </summary>
    public static ReserveStock ToReserveStock(Order order)
    {
        return new ReserveStock
        {
            OrderId = order.Id,
            Lines = order.Lines
                .Select(line => new ReserveStockLine { CatalogItemId = line.CatalogItemId, Units = line.Units })
                .ToList()
        };
    }
}
