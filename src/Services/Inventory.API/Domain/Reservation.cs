namespace Inventory.API.Domain;

public static class ReservationStatus
{
    public const string Reserved = "Reserved";
    public const string Rejected = "Rejected";
    public const string Released = "Released";
}

/// <summary>
/// Lagerets svar på én ordre. OrderId er nøglen, så der kan kun findes én reservation pr. ordre
/// (Idempotent Receiver: kommer samme ReserveStock igen, findes svaret allerede).
/// En afvist ordre gemmes også, så det samme svar kan sendes igen.
/// </summary>
public class Reservation
{
    // Til EF Core.
    private Reservation()
    {
    }

    private Reservation(int orderId, string status, List<ReservationLine> lines)
    {
        OrderId = orderId;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
        Lines = lines;
    }

    public static Reservation Reserve(int orderId, List<ReservationLine> lines) =>
        new(orderId, ReservationStatus.Reserved, lines);

    public static Reservation Reject(int orderId, List<ReservationLine> lines) =>
        new(orderId, ReservationStatus.Rejected, lines);

    public int OrderId { get; private set; }
    public string Status { get; private set; } = ReservationStatus.Reserved;
    public DateTimeOffset CreatedAt { get; private set; }
    public List<ReservationLine> Lines { get; private set; } = new();

    /// <summary>Linjerne, der var for få af, da ordren kom ind.</summary>
    public IEnumerable<ReservationLine> Shortages => Lines.Where(line => line.Units > line.AvailableAtCheck);

    public void MarkReleased()
    {
        if (Status != ReservationStatus.Reserved)
        {
            throw new InvalidOperationException($"Reservation for order {OrderId} is {Status} and cannot be released");
        }

        Status = ReservationStatus.Released;
    }
}

public class ReservationLine
{
    // Til EF Core.
    private ReservationLine()
    {
    }

    public ReservationLine(int catalogItemId, int units, int availableAtCheck)
    {
        CatalogItemId = catalogItemId;
        Units = units;
        AvailableAtCheck = availableAtCheck;
    }

    public int Id { get; private set; }
    public int OrderId { get; private set; }
    public int CatalogItemId { get; private set; }
    public int Units { get; private set; }

    /// <summary>Ledig beholdning, da ordren kom ind. Bruges til at sende samme afvisning igen.</summary>
    public int AvailableAtCheck { get; private set; }
}
