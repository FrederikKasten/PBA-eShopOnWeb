using Contracts;
using Inventory.API.Data;
using Inventory.API.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.API.Messaging;

/// <summary>
/// Event-Driven Consumer på Point-to-Point-kanalen inventory-reserve-stock.
/// Reserverer hele ordren eller intet, gemmer svaret som en Reservation og publicerer
/// StockReserved eller StockReservationRejected.
/// </summary>
public class ReserveStockConsumer : IConsumer<ReserveStock>
{
    private readonly InventoryDbContext _db;
    private readonly ILogger<ReserveStockConsumer> _logger;

    public ReserveStockConsumer(InventoryDbContext db, ILogger<ReserveStockConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReserveStock> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // Invalid Message: en ordre uden linjer eller med 0 stk. kan ikke reserveres. ArgumentException prøves
        // ikke igen (se Program.cs), så beskeden havner med det samme i inventory-reserve-stock_error.
        if (message.Lines.Count == 0 || message.Lines.Any(line => line.Units < 1))
        {
            throw new ArgumentException($"ReserveStock for order {message.OrderId} has no lines or a line with less than 1 unit");
        }

        // Idempotent Receiver: findes der allerede en reservation for ordren, ændres beholdningen ikke,
        // men det samme svar sendes igen (så et svar, der er gået tabt, kommer frem).
        var existing = await _db.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.OrderId == message.OrderId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation("Reservation for order {OrderId} already exists ({ReservationStatus}); sending the answer again",
                message.OrderId, existing.Status);
            await PublishAnswer(context, existing);
            return;
        }

        // Samme vare kan stå på flere linjer; læg dem sammen, før der tjekkes.
        var requested = message.Lines
            .GroupBy(line => line.CatalogItemId)
            .Select(group => new { CatalogItemId = group.Key, Units = group.Sum(line => line.Units) })
            .ToList();

        var catalogItemIds = requested.Select(r => r.CatalogItemId).ToList();
        var stock = await _db.StockItems
            .Where(s => catalogItemIds.Contains(s.CatalogItemId))
            .ToDictionaryAsync(s => s.CatalogItemId, cancellationToken);

        // En vare, lageret ikke kender, tæller som 0 på lager.
        var lines = requested
            .Select(r => new ReservationLine(r.CatalogItemId, r.Units,
                stock.TryGetValue(r.CatalogItemId, out var item) ? item.Available : 0))
            .ToList();

        // Alt eller intet: mangler der bare én vare, reserveres ingen af dem.
        Reservation reservation;
        if (lines.All(line => line.Units <= line.AvailableAtCheck))
        {
            foreach (var line in lines)
            {
                stock[line.CatalogItemId].Reserve(line.Units);
            }
            reservation = Reservation.Reserve(message.OrderId, lines);
        }
        else
        {
            reservation = Reservation.Reject(message.OrderId, lines);
        }

        // Beholdning og reservation gemmes i samme SaveChanges (én transaktion). Har en anden ordre ændret
        // en vare i mellemtiden, kaster EF DbUpdateConcurrencyException, og MassTransit prøver beskeden igen.
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync(cancellationToken);

        if (reservation.Status == ReservationStatus.Reserved)
        {
            _logger.LogInformation("Reserved {LineCount} catalog items for order {OrderId}", lines.Count, message.OrderId);
        }
        else
        {
            _logger.LogInformation("Rejected order {OrderId}: {ShortageCount} catalog items are short",
                message.OrderId, reservation.Shortages.Count());
        }

        // Først gemme, så publicere (at-least-once). Fejler Publish, prøves beskeden igen, og så finder
        // consumeren reservationen ovenfor og sender svaret igen.
        await PublishAnswer(context, reservation);
    }

    private static Task PublishAnswer(ConsumeContext context, Reservation reservation)
    {
        return reservation.Status switch
        {
            ReservationStatus.Reserved => context.Publish(new StockReserved { OrderId = reservation.OrderId }),
            ReservationStatus.Rejected => context.Publish(new StockReservationRejected
            {
                OrderId = reservation.OrderId,
                Shortages = reservation.Shortages
                    .Select(line => new StockShortage
                    {
                        CatalogItemId = line.CatalogItemId,
                        Requested = line.Units,
                        Available = line.AvailableAtCheck
                    })
                    .ToList()
            }),
            // Released: ordren er annulleret bagefter. Ordering.API har ikke brug for et svar mere.
            _ => Task.CompletedTask
        };
    }
}
