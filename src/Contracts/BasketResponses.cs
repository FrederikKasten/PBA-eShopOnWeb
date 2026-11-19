namespace Contracts;

/// <summary>
/// Reply: the basket as it looks right now in Basket.API.
/// Sent back for every basket request that succeeds.
/// </summary>
public record BasketSnapshot
{
    // 0 = the buyer has no basket yet.
    public int BasketId { get; init; }
    public string BuyerId { get; init; } = string.Empty;
    public List<BasketSnapshotItem> Items { get; init; } = new();
}

public record BasketSnapshotItem
{
    public int BasketItemId { get; init; }
    public int CatalogItemId { get; init; }
    public decimal UnitPrice { get; init; }
    public int Quantity { get; init; }
}

/// <summary>
/// Reply: Basket.API refused the request (invalid input or unknown basket).
/// A refusal is a normal answer, not an error – nothing is retried or moved to an error queue.
/// </summary>
public record BasketRequestRejected
{
    public string Reason { get; init; } = string.Empty;
}
