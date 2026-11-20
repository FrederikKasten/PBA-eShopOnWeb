namespace Inventory.API.Domain;

/// <summary>
/// Beholdningen af én vare. Lageret kender kun katalog-id og antal – ikke navn eller pris.
/// Ledig beholdning = OnHand - Reserved, og den må aldrig blive negativ.
/// </summary>
public class StockItem
{
    // Til EF Core.
    private StockItem()
    {
    }

    public StockItem(int catalogItemId, int onHand)
    {
        if (onHand < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(onHand), "OnHand cannot be negative");
        }

        CatalogItemId = catalogItemId;
        OnHand = onHand;
    }

    public int CatalogItemId { get; private set; }

    /// <summary>Antal fysisk på lager.</summary>
    public int OnHand { get; private set; }

    /// <summary>Antal lovet væk til ordrer, der endnu ikke er sendt.</summary>
    public int Reserved { get; private set; }

    public int Available => OnHand - Reserved;

    /// <summary>
    /// Concurrency token: tælles op ved hver ændring. Køber to kunder den sidste vare samtidig,
    /// opdager EF Core, at rækken er ændret siden den blev læst, og den ene SaveChanges fejler.
    /// </summary>
    public int Version { get; private set; }

    public void Reserve(int units)
    {
        if (units <= 0 || units > Available)
        {
            throw new InvalidOperationException($"Cannot reserve {units} of catalog item {CatalogItemId}; {Available} available");
        }

        Reserved += units;
        Version++;
    }

    public void Release(int units)
    {
        if (units <= 0 || units > Reserved)
        {
            throw new InvalidOperationException($"Cannot release {units} of catalog item {CatalogItemId}; {Reserved} reserved");
        }

        Reserved -= units;
        Version++;
    }

    public void Restock(int units)
    {
        if (units <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(units), "Units must be at least 1");
        }

        OnHand += units;
        Version++;
    }
}
