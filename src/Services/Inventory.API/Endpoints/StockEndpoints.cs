using Inventory.API.Data;
using Inventory.API.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.API.Endpoints;

// HTTP-indgangen til lageret: til administration, til den kommende API Gateway og til test.
// Ordreflowet bruger IKKE disse endpoints – det går via RabbitMQ (se Messaging/).
// Alle endpoints kræver et JWT fra PublicApi; restock kræver desuden rollen Administrators.
public static class StockEndpoints
{
    public static void MapStockEndpoints(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Inventory.API.Endpoints.StockEndpoints");

        var stock = app.MapGroup("/stock").RequireAuthorization();

        // GET /stock – alle varer med beholdning, reserveret og ledig.
        stock.MapGet("/", async (InventoryDbContext db) =>
        {
            var items = await db.StockItems
                .AsNoTracking()
                .OrderBy(s => s.CatalogItemId)
                .ToListAsync();
            return Results.Ok(items.Select(StockItemDto.From));
        });

        // GET /stock/5 – én vare. 404 hvis lageret ikke kender den.
        stock.MapGet("/{catalogItemId:int}", async (int catalogItemId, InventoryDbContext db) =>
        {
            var item = await db.StockItems.AsNoTracking().FirstOrDefaultAsync(s => s.CatalogItemId == catalogItemId);
            return item is null ? Results.NotFound() : Results.Ok(StockItemDto.From(item));
        });

        // POST /stock/5/restock  { "units": 10 } – læg varer på lager. Opretter varen, hvis den ikke findes.
        stock.MapPost("/{catalogItemId:int}/restock", async (int catalogItemId, RestockRequest request, InventoryDbContext db) =>
        {
            if (request.Units <= 0)
            {
                logger.LogWarning("Rejected restock of catalog item {CatalogItemId}: {Units} units", catalogItemId, request.Units);
                return Results.BadRequest("Units must be at least 1");
            }

            var item = await db.StockItems.FirstOrDefaultAsync(s => s.CatalogItemId == catalogItemId);
            if (item is null)
            {
                item = new StockItem(catalogItemId, request.Units);
                db.StockItems.Add(item);
            }
            else
            {
                item.Restock(request.Units);
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // En reservation nåede at ændre varen samtidig. Klienten kan bare prøve igen.
                logger.LogWarning("Restock of catalog item {CatalogItemId} conflicted with another change", catalogItemId);
                return Results.Conflict("The stock changed at the same time; try again");
            }

            logger.LogInformation("Restocked catalog item {CatalogItemId} with {Units} units; {OnHand} on hand",
                catalogItemId, request.Units, item.OnHand);
            return Results.Ok(StockItemDto.From(item));
        }).RequireAuthorization("admin");

        // GET /reservations/42 – status og linjer for en ordres reservation.
        app.MapGet("/reservations/{orderId:int}", async (int orderId, InventoryDbContext db) =>
        {
            var reservation = await db.Reservations
                .AsNoTracking()
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.OrderId == orderId);
            return reservation is null ? Results.NotFound() : Results.Ok(ReservationDto.From(reservation));
        }).RequireAuthorization();
    }
}
