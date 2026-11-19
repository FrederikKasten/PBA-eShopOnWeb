using System.Security.Claims;
using BasketApi.Data;
using BasketApi.Services;
using Microsoft.EntityFrameworkCore;

namespace BasketApi.Endpoints;

// HTTP-indgangen til Basket.API: til den kommende API Gateway (krav 4) og til test med curl.
// Web bruger IKKE disse endpoints – Web taler med Basket.API via RabbitMQ (se Messaging/).
// Begge veje ender i samme IBasketService.
//
// Alle endpoints kræver et gyldigt JWT fra PublicApi (header "Authorization: Bearer <token>"), og en bruger
// må kun læse og ændre sin egen kurv. Administratorer må alle. Uden token: 401. En andens kurv: 403.
// Anonyme kurve (cookie-id fra Web) kan derfor kun nås via RabbitMQ – eller af en administrator.
public static class BasketEndpoints
{
    // Samme rollenavn som i monolitten (BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS).
    private const string AdministratorsRole = "Administrators";

    public static void MapBasketEndpoints(this WebApplication app)
    {
        var baskets = app.MapGroup("/baskets").RequireAuthorization();

        // En statisk klasse kan ikke bruges som T i ILogger<T>, så loggeren laves ud fra kategoriens navn.
        // Den vises som sourceContext = "BasketApi.Endpoints.BasketEndpoints" i Kibana.
        // Endpoints logger kun afviste kald (400/403/404); det der lykkes, logger BasketService,
        // og UseSerilogRequestLogging logger statuskode og varighed for alle kald.
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BasketApi.Endpoints.BasketEndpoints");

        // GET /baskets/demouser@microsoft.com
        // Køberens kurv. Findes den ikke, svares der med en tom kurv (Id 0) – der oprettes intet.
        baskets.MapGet("/{buyerId}", async (string buyerId, ClaimsPrincipal user, IBasketService basketService) =>
        {
            if (!MayAccess(user, buyerId))
            {
                return DeniedBuyer(logger);
            }

            var basket = await basketService.GetBasketAsync(buyerId);
            return Results.Ok(basket is null ? BasketDto.Empty(buyerId) : BasketDto.From(basket));
        });

        // GET /baskets/demouser@microsoft.com/count
        // Antal varer i alt. Summen laves i databasen.
        baskets.MapGet("/{buyerId}/count", async (string buyerId, ClaimsPrincipal user, BasketDbContext db) =>
        {
            if (!MayAccess(user, buyerId))
            {
                return DeniedBuyer(logger);
            }

            var count = await db.Baskets
                .Where(b => b.BuyerId == buyerId)
                .SelectMany(b => b.Items)
                .SumAsync(i => i.Quantity);

            return Results.Ok(count);
        });

        // POST /baskets/demouser@microsoft.com/items   { "catalogItemId": 2, "unitPrice": 8.50, "quantity": 1 }
        // Tilføj vare. Opretter kurven hvis den ikke findes.
        baskets.MapPost("/{buyerId}/items", async (string buyerId, AddBasketItemRequest request, ClaimsPrincipal user, IBasketService basketService) =>
        {
            if (!MayAccess(user, buyerId))
            {
                return DeniedBuyer(logger);
            }

            if (request.Quantity < 1 || request.UnitPrice < 0)
            {
                logger.LogWarning("Rejected adding catalog item {CatalogItemId}: quantity {Quantity}, unit price {UnitPrice}",
                    request.CatalogItemId, request.Quantity, request.UnitPrice);
                return Results.BadRequest("Quantity skal være mindst 1, og UnitPrice må ikke være negativ.");
            }

            var basket = await basketService.AddItemToBasket(buyerId, request.CatalogItemId, request.UnitPrice, request.Quantity);
            return Results.Ok(BasketDto.From(basket));
        });

        // PUT /baskets/5/quantities   { "12": 3, "13": 0 }   (basket-item-id -> nyt antal)
        // Sæt antal. Varer med antal 0 fjernes.
        baskets.MapPut("/{basketId:int}/quantities", async (int basketId, Dictionary<string, int> quantities, ClaimsPrincipal user,
            BasketDbContext db, IBasketService basketService) =>
        {
            var denied = await DenyUnlessOwnerAsync(db, basketId, user, logger);
            if (denied is not null)
            {
                return denied;
            }

            if (quantities.Values.Any(q => q < 0))
            {
                logger.LogWarning("Rejected negative quantity for basket {BasketId}", basketId);
                return Results.BadRequest("Antal må ikke være negativt.");
            }

            // Findes kurven ikke (slettet imens), logger BasketService det.
            var result = await basketService.SetQuantities(basketId, quantities);
            return result.IsSuccess
                ? Results.Ok(BasketDto.From(result.Value))
                : Results.NotFound();
        });

        // DELETE /baskets/5
        // Slet kurv. Web bruger den ikke – efter checkout tømmer Basket.API selv kurven, når OrderCreated kommer.
        baskets.MapDelete("/{basketId:int}", async (int basketId, ClaimsPrincipal user, BasketDbContext db, IBasketService basketService) =>
        {
            var denied = await DenyUnlessOwnerAsync(db, basketId, user, logger);
            if (denied is not null)
            {
                return denied;
            }

            try
            {
                await basketService.DeleteBasketAsync(basketId);
                return Results.NoContent();
            }
            catch (ArgumentNullException)
            {
                // Kurven blev slettet mellem tjekket ovenfor og nu. BasketService bruger Guard.Against.Null.
                logger.LogWarning("Basket {BasketId} not found when deleting", basketId);
                return Results.NotFound();
            }
        });

        // POST /baskets/transfer   { "anonymousId": "<guid fra cookie>", "userName": "demouser@microsoft.com" }
        // Overfør anonym kurv til bruger. Man kan kun overføre til sig selv.
        baskets.MapPost("/transfer", async (TransferBasketRequest request, ClaimsPrincipal user, IBasketService basketService) =>
        {
            if (string.IsNullOrWhiteSpace(request.AnonymousId) || string.IsNullOrWhiteSpace(request.UserName))
            {
                logger.LogWarning("Rejected basket transfer: anonymous id or user name is missing");
                return Results.BadRequest("AnonymousId og UserName skal udfyldes.");
            }

            if (!MayAccess(user, request.UserName))
            {
                return DeniedBuyer(logger);
            }

            await basketService.TransferBasketAsync(request.AnonymousId, request.UserName);
            return Results.NoContent();
        });
    }

    // Ejer-tjek: brugeren i token'et (ClaimTypes.Name, sat af PublicApi) skal være køberen, eller være administrator.
    private static bool MayAccess(ClaimsPrincipal user, string buyerId)
    {
        return user.IsInRole(AdministratorsRole)
            || string.Equals(user.Identity?.Name, buyerId, StringComparison.OrdinalIgnoreCase);
    }

    // null = adgang. Ellers 404 (kurven findes ikke) eller 403 (en andens kurv).
    private static async Task<IResult?> DenyUnlessOwnerAsync(BasketDbContext db, int basketId, ClaimsPrincipal user, ILogger logger)
    {
        var buyerId = await db.Baskets
            .Where(b => b.Id == basketId)
            .Select(b => b.BuyerId)
            .FirstOrDefaultAsync();

        if (buyerId is null)
        {
            logger.LogWarning("Basket {BasketId} not found", basketId);
            return Results.NotFound();
        }

        if (!MayAccess(user, buyerId))
        {
            logger.LogWarning("Denied access to basket {BasketId}: it belongs to another buyer", basketId);
            return Results.Forbid();
        }

        return null;
    }

    // Køberens id (e-mail) logges ikke – kun at det var en andens kurv.
    private static IResult DeniedBuyer(ILogger logger)
    {
        logger.LogWarning("Denied access to a basket that belongs to another buyer");
        return Results.Forbid();
    }
}
