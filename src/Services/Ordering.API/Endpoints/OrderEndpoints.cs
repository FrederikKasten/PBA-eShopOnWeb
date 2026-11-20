using System.Security.Claims;
using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Data;
using Ordering.API.Domain;

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
                order.Lines.Select(l => new OrderLineDto(l.CatalogItemId, l.ProductName, l.PictureUri, l.UnitPrice, l.Units)).ToList(),
                order.RejectionReason);

            return Results.Ok(dto);
        });

        // GET /orders/5/status
        // Kun status – det simple endpoint fra issue #5.
        orders.MapGet("/{id:int}/status", async (int id, OrderingDbContext db) =>
        {
            var status = await db.Orders
                .AsNoTracking()
                .Where(o => o.Id == id)
                .Select(o => new OrderStatusDto(o.Id, o.Status, o.RejectionReason))
                .FirstOrDefaultAsync();

            return status is null ? Results.NotFound() : Results.Ok(status);
        });

        // POST /orders/5/cancel
        // Annullér en bekræftet ordre. Lageret frigiver varerne, når det modtager OrderCancelled
        // (kompenserende handling i checkout-sagaen). Kræver et JWT fra PublicApi: køberen selv eller en administrator.
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ordering.API.Endpoints.OrderEndpoints");
        orders.MapPost("/{id:int}/cancel", async (int id, ClaimsPrincipal user, OrderingDbContext db, IPublishEndpoint publishEndpoint) =>
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id);
            if (order is null)
            {
                return Results.NotFound();
            }

            var isOwner = string.Equals(user.Identity?.Name, order.BuyerId, StringComparison.OrdinalIgnoreCase);
            if (!isOwner && !user.IsInRole("Administrators"))
            {
                logger.LogWarning("Denied cancelling order {OrderId}: it belongs to another buyer", id);
                return Results.Forbid();
            }

            if (order.Status != OrderStatus.Confirmed)
            {
                return Results.Conflict($"Only confirmed orders can be cancelled; order {id} is {order.Status}");
            }

            // Først publicere, så gemme. Fejler Publish, er intet ændret, og kunden kan prøve igen.
            // Fejler SaveChanges bagefter, står ordren stadig som Confirmed, og et nyt forsøg sender
            // OrderCancelled igen. Det er harmløst, fordi lageret kun frigiver en reservation én gang.
            await publishEndpoint.Publish(new OrderCancelled { OrderId = order.Id });
            order.Status = OrderStatus.Cancelled;
            await db.SaveChangesAsync();

            logger.LogInformation("Order {OrderId} cancelled", order.Id);
            return Results.Ok(new OrderStatusDto(order.Id, order.Status, order.RejectionReason));
        }).RequireAuthorization();
    }
}
