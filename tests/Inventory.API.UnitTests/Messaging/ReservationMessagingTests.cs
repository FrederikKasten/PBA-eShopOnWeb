using Contracts;
using Inventory.API.Data;
using Inventory.API.Domain;
using Inventory.API.Messaging;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.API.UnitTests.Messaging;

/// <summary>
/// Tester reservationsflowet uden RabbitMQ: MassTransit's test harness kører en bus i hukommelsen.
/// Databasen er SQLite i hukommelsen (ikke EF InMemory), så nøgler, transaktioner og concurrency token
/// opfører sig som i den rigtige database. Startbeholdningen er den samme som i InventoryDbContext:
/// 20 stk. af vare 1-12, undtagen vare 5 med 2 stk.
/// </summary>
public class ReservationMessagingTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        // En SQLite-database i hukommelsen findes kun, så længe forbindelsen er åben.
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<InventoryDbContext>(options => options.UseSqlite(_connection))
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<ReserveStockConsumer>();
                x.AddConsumer<OrderCancelledConsumer>();
            })
            .BuildServiceProvider(true);

        using (var scope = _provider.CreateScope())
        {
            // Opretter tabellerne og startbeholdningen (HasData).
            scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.EnsureCreated();
        }

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Enough_stock_reserves_every_line_and_publishes_StockReserved()
    {
        await Deliver(Order(orderId: 1, Line(catalogItemId: 1, units: 2), Line(catalogItemId: 2, units: 1)));

        Assert.True(await _harness.Published.Any<StockReserved>(m => m.Context.Message.OrderId == 1));
        Assert.Equal(2, (await Stock(1)).Reserved);
        Assert.Equal(1, (await Stock(2)).Reserved);
        Assert.Equal(ReservationStatus.Reserved, (await ReservationFor(1)).Status);
    }

    [Fact]
    public async Task One_item_short_rejects_the_whole_order_and_reserves_nothing()
    {
        // Vare 5 har kun 2 på lager.
        await Deliver(Order(orderId: 2, Line(catalogItemId: 1, units: 1), Line(catalogItemId: 5, units: 3)));

        Assert.True(await _harness.Published.Any<StockReservationRejected>(m =>
            m.Context.Message.OrderId == 2
            && m.Context.Message.Shortages.Count == 1
            && m.Context.Message.Shortages[0].CatalogItemId == 5
            && m.Context.Message.Shortages[0].Requested == 3
            && m.Context.Message.Shortages[0].Available == 2));
        Assert.Equal(0, (await Stock(1)).Reserved);
        Assert.Equal(0, (await Stock(5)).Reserved);
        Assert.Equal(ReservationStatus.Rejected, (await ReservationFor(2)).Status);
    }

    [Fact]
    public async Task Unknown_catalog_item_counts_as_zero_in_stock()
    {
        await Deliver(Order(orderId: 3, Line(catalogItemId: 99, units: 1)));

        Assert.True(await _harness.Published.Any<StockReservationRejected>(m =>
            m.Context.Message.OrderId == 3 && m.Context.Message.Shortages[0].Available == 0));
    }

    [Fact]
    public async Task Same_ReserveStock_twice_reserves_once_and_answers_twice()
    {
        var command = Order(orderId: 4, Line(catalogItemId: 3, units: 2));

        await Deliver(command);
        await Deliver(command);

        Assert.Equal(2, (await Stock(3)).Reserved);
        Assert.Equal(2, _harness.Published.Select<StockReserved>().Count(m => m.Context.Message.OrderId == 4));
    }

    [Fact]
    public async Task OrderCancelled_releases_the_reservation_only_once()
    {
        await Deliver(Order(orderId: 5, Line(catalogItemId: 4, units: 3)));

        await DeliverCancelled(orderId: 5);
        await DeliverCancelled(orderId: 5);

        var item = await Stock(4);
        Assert.Equal(0, item.Reserved);
        Assert.Equal(20, item.OnHand);
        Assert.Equal(ReservationStatus.Released, (await ReservationFor(5)).Status);
        // Var reservationen frigivet to gange, ville Release have kastet en exception, og MassTransit
        // ville have publiceret en Fault<OrderCancelled>.
        Assert.False(await _harness.Published.Any<Fault<OrderCancelled>>());
    }

    // Sender en ReserveStock og venter, til consumeren er færdig med netop den besked.
    private async Task Deliver(ReserveStock command)
    {
        var messageId = Guid.NewGuid();
        await _harness.Bus.Publish(command, ctx => ctx.MessageId = messageId);
        Assert.True(await _harness.GetConsumerHarness<ReserveStockConsumer>()
            .Consumed.Any<ReserveStock>(m => m.Context.MessageId == messageId));
    }

    private async Task DeliverCancelled(int orderId)
    {
        var messageId = Guid.NewGuid();
        await _harness.Bus.Publish(new OrderCancelled { OrderId = orderId }, ctx => ctx.MessageId = messageId);
        Assert.True(await _harness.GetConsumerHarness<OrderCancelledConsumer>()
            .Consumed.Any<OrderCancelled>(m => m.Context.MessageId == messageId));
    }

    private async Task<StockItem> Stock(int catalogItemId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.StockItems.AsNoTracking().SingleAsync(s => s.CatalogItemId == catalogItemId);
    }

    private async Task<Reservation> ReservationFor(int orderId)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.Reservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
    }

    private static ReserveStock Order(int orderId, params ReserveStockLine[] lines) =>
        new() { OrderId = orderId, Lines = lines.ToList() };

    private static ReserveStockLine Line(int catalogItemId, int units) =>
        new() { CatalogItemId = catalogItemId, Units = units };
}
