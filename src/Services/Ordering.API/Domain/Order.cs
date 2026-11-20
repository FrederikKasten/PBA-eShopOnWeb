namespace Ordering.API.Domain;

/// <summary>
/// Ordering-servicens egen model af en ordre.
/// Den ligner monolittens Order, men er en selvstændig klasse:
/// Ordering.API kender ikke monolittens typer.
/// </summary>
public class Order
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = OrderStatus.AwaitingStock;

    /// <summary>Hvorfor lageret afviste ordren, fx "Roslyn Red Sheet: ordered 3, available 2". Ellers null.</summary>
    public string? RejectionReason { get; set; }

    public string ShipToStreet { get; set; } = string.Empty;
    public string ShipToCity { get; set; } = string.Empty;
    public string ShipToState { get; set; } = string.Empty;
    public string ShipToCountry { get; set; } = string.Empty;
    public string ShipToZipCode { get; set; } = string.Empty;

    public List<OrderLine> Lines { get; set; } = new();
}
