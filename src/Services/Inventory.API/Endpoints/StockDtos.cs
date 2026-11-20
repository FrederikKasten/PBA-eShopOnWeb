using Inventory.API.Domain;

namespace Inventory.API.Endpoints;

public record StockItemDto(int CatalogItemId, int OnHand, int Reserved, int Available)
{
    public static StockItemDto From(StockItem item) =>
        new(item.CatalogItemId, item.OnHand, item.Reserved, item.Available);
}

public record RestockRequest(int Units);

public record ReservationLineDto(int CatalogItemId, int Units);

public record ReservationDto(int OrderId, string Status, DateTimeOffset CreatedAt, List<ReservationLineDto> Lines)
{
    public static ReservationDto From(Reservation reservation) =>
        new(reservation.OrderId,
            reservation.Status,
            reservation.CreatedAt,
            reservation.Lines.Select(line => new ReservationLineDto(line.CatalogItemId, line.Units)).ToList());
}
