namespace Ordering.API.Endpoints;
public record AddressDto(string Street, string City, string State, string Country, string ZipCode);

public record OrderSummaryDto(
    int OrderId,
    string BuyerId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Status,
    AddressDto ShipToAddress);

public record OrderLineDto(
    int CatalogItemId,
    string ProductName,
    string PictureUri,
    decimal UnitPrice,
    int Units);

public record OrderDetailsDto(
    int OrderId,
    string BuyerId,
    DateTimeOffset OrderDate,
    decimal Total,
    string Status,
    AddressDto ShipToAddress,
    List<OrderLineDto> Lines);

public record OrderStatusDto(int OrderId, string Status);
