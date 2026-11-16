namespace Contracts;

// Requests that Web sends to Basket.API (Request-Reply).
// Each request type has its own queue (see BasketQueues) and is answered with
// BasketSnapshot, or BasketRequestRejected when Basket.API refuses it.
// Contains only simple data – no EF entities from either side.

/// <summary>
/// Query: "Give me this buyer's basket."
/// Answered with BasketSnapshot (BasketId 0 and no items when the buyer has no basket).
/// </summary>
public record GetBasket
{
    public string BuyerId { get; init; } = string.Empty;

    // Read-your-writes: answer only once this order has been removed from the basket (OrderCreated processed),
    // waiting at most a couple of seconds. Web sets it on the first page after checkout. Optional, so old senders still work.
    public int? AfterOrderId { get; init; }
}

/// <summary>
/// Command: "Put this catalog item in the buyer's basket."
/// The price comes from the catalog in Web; Basket.API does not know the catalog.
/// </summary>
public record AddItemToBasket
{
    public string BuyerId { get; init; } = string.Empty;
    public int CatalogItemId { get; init; }
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; } = 1;
}

/// <summary>
/// Command: "Set new quantities on these basket lines." Quantity 0 removes the line.
/// </summary>
public record SetBasketQuantities
{
    public int BasketId { get; init; }
    public List<BasketQuantity> Quantities { get; init; } = new();
}

public record BasketQuantity
{
    public int BasketItemId { get; init; }
    public int Quantity { get; init; }
}

/// <summary>
/// Command: "Move the anonymous buyer's basket to the user who just logged in."
/// Answered with the user's basket after the transfer.
/// </summary>
public record TransferBasket
{
    public string AnonymousBuyerId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
}
