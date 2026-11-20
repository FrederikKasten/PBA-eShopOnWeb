namespace Ordering.API.Domain;

/// <summary>
/// Ordrens status. Ordering.API ejer den og styrer den ud fra lagerets svar:
/// AwaitingStock -> Confirmed eller Rejected, og Confirmed -> Cancelled.
/// </summary>
public static class OrderStatus
{
    public const string AwaitingStock = "AwaitingStock";
    public const string Confirmed = "Confirmed";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}
