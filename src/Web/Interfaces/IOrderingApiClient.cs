namespace Microsoft.eShopWeb.Web.Interfaces;

/// <summary>Henter ordrer fra Ordering.API over HTTP.</summary>
public interface IOrderingApiClient
{
    Task<IReadOnlyList<OrderingApiOrderSummary>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<OrderingApiOrderDetails?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);
}

// Monolittens egen kopi af de felter, den bruger fra Ordering.API's JSON.
// Den deler ikke typer med Ordering.API – den læser bare det, den har brug for.
public record OrderingApiAddress(string Street, string City, string State, string Country, string ZipCode);

public record OrderingApiOrderSummary(
    int OrderId,
    string BuyerId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Status,
    OrderingApiAddress ShipToAddress);

public record OrderingApiOrderLine(
    int CatalogItemId,
    string ProductName,
    string PictureUri,
    decimal UnitPrice,
    int Units);

public record OrderingApiOrderDetails(
    int OrderId,
    string BuyerId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Status,
    OrderingApiAddress ShipToAddress,
    List<OrderingApiOrderLine> Lines);


//Hvorfor ikke bare genbruge DTO'erne fra Ordering.API?
//Så skulle Web referere til Ordering.API-projektet
//og de to ville være koblet sammen igen. Monolitten beskriver selv de felter den læser.
//Mønstret hedder Tolerant Reader: får Ordering.API et nyt felt, sker der intet i monolitten.
