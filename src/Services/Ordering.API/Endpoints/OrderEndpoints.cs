using Microsoft.EntityFrameworkCore;
using Ordering.API.Data;

namespace Ordering.API.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/orders");

        // GET /orders?buyerId=demouser@microsoft.com
        // Alle ordrer for én køber – nyeste først.
        orders.MapGet("/", async (string buyerId, OrderingDbContext db) =>
        {
            var result = await db.Orders
                .AsNoTracking()
                .Where(o => o.BuyerId == buyerId)
                .OrderByDescending(o => o.Id)
                .Select(o => new OrderSummaryDto(
                    o.Id,
                    o.BuyerId,
                    o.OrderDate,
                    o.Total,
                    o.Status,
                    new AddressDto(o.ShipToStreet, o.ShipToCity, o.ShipToState, o.ShipToCountry, o.ShipToZipCode)))
                .ToListAsync();

            return Results.Ok(result);
        });

        // GET /orders/5
        // Én ordre med alle linjer. 404 hvis den ikke findes.
        orders.MapGet("/{id:int}", async (int id, OrderingDbContext db) =>
        {
            var order = await db.Orders
                .AsNoTracking()
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order is null)
            {
                return Results.NotFound();
            }

            var dto = new OrderDetailsDto(
                order.Id,
                order.BuyerId,
                order.OrderDate,
                order.Total,
                order.Status,
                new AddressDto(order.ShipToStreet, order.ShipToCity, order.ShipToState, order.ShipToCountry, order.ShipToZipCode),
                order.Lines.Select(l => new OrderLineDto(l.CatalogItemId, l.ProductName, l.PictureUri, l.UnitPrice, l.Units)).ToList());

            return Results.Ok(dto);
        });

        // GET /orders/5/status
        // Kun status – det simple endpoint fra issue #5.
        orders.MapGet("/{id:int}/status", async (int id, OrderingDbContext db) =>
        {
            var status = await db.Orders
                .AsNoTracking()
                .Where(o => o.Id == id)
                .Select(o => new OrderStatusDto(o.Id, o.Status))
                .FirstOrDefaultAsync();

            return status is null ? Results.NotFound() : Results.Ok(status);
        });
    }
}
