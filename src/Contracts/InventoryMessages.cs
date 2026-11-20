namespace Contracts;

/// <summary>
/// Command: "Reservér varerne til denne ordre."
/// Sendes af Ordering.API direkte til køen inventory-reserve-stock (Point-to-Point).
/// Indeholder kun det, lageret skal bruge: ordrens id og katalog-id + antal (Content Filter).
/// </summary>
public record ReserveStock
{
    public int OrderId { get; init; }
    public List<ReserveStockLine> Lines { get; init; } = new();
}

public record ReserveStockLine
{
    public int CatalogItemId { get; init; }
    public int Units { get; init; }
}

/// <summary>
/// Event: "Alle varer til ordren er reserveret."
/// Publiceres af Inventory.API (Publish-Subscribe). Ordering.API sætter ordren til Confirmed.
/// </summary>
public record StockReserved
{
    public int OrderId { get; init; }
}

/// <summary>
/// Event: "Der var ikke nok af mindst én vare, så intet er reserveret."
/// Publiceres af Inventory.API. Ordering.API sætter ordren til Rejected.
/// </summary>
public record StockReservationRejected
{
    public int OrderId { get; init; }
    public List<StockShortage> Shortages { get; init; } = new();
}

public record StockShortage
{
    public int CatalogItemId { get; init; }
    public int Requested { get; init; }
    public int Available { get; init; }
}

/// <summary>
/// Event: "En bekræftet ordre er annulleret."
/// Publiceres af Ordering.API. Inventory.API frigiver reservationen (kompenserende handling i sagaen).
/// </summary>
public record OrderCancelled
{
    public int OrderId { get; init; }
}
