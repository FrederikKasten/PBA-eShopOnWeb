namespace Contracts;

/// <summary>
/// Event: "An order has been created in the monolith."
/// Sent by Web (the monolith) and received by Ordering.API.
/// Contains only simple data – no EF entities from the monolith.
/// </summary>
public record OrderCreated
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public DateTimeOffset OrderDate { get; init; }
    public decimal Total { get; init; }
    public OrderCreatedAddress ShipToAddress { get; init; } = new();
    public List<OrderCreatedLine> Lines { get; init; } = new();
}

public record OrderCreatedLine
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string PictureUri { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}

public record OrderCreatedAddress
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string ZipCode { get; init; } = string.Empty;
}
