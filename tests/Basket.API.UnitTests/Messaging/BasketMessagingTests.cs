using BasketApi.Data;
using BasketApi.Messaging;
using BasketApi.Services;
using Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BasketApi.UnitTests.Messaging;

/// <summary>
/// Tester beskederne uden RabbitMQ: MassTransit's test harness kører en bus i hukommelsen.
/// Consumerne bruger den rigtige BasketService mod en database i hukommelsen, så testene viser
/// hele vejen fra besked ind til svar (eller databaseændring) ud.
/// </summary>
public class BasketMessagingTests : IAsyncLifetime
{
    private const string Buyer = "demouser@microsoft.com";

    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        // Ny database for hver test, så testene ikke påvirker hinanden.
        var databaseName = Guid.NewGuid().ToString();

        _provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<BasketDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .AddScoped(typeof(IRepository<>), typeof(EfRepository<>))
            .AddScoped<IBasketService, BasketService>()
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<GetBasketConsumer>();
                x.AddConsumer<AddItemToBasketConsumer>();
                x.AddConsumer<SetBasketQuantitiesConsumer>();
                x.AddConsumer<TransferBasketConsumer>();
                x.AddConsumer<RemoveOrderedItemsConsumer>();
            })
            .BuildServiceProvider(true);

        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task GetBasket_for_unknown_buyer_replies_with_empty_basket()
    {
        var client = _harness.GetRequestClient<GetBasket>();

        var response = await client.GetResponse<BasketSnapshot>(new GetBasket { BuyerId = "nobody" });

        Assert.Equal(0, response.Message.BasketId);
        Assert.Empty(response.Message.Items);
    }

    [Fact]
    public async Task AddItemToBasket_twice_replies_with_quantity_two()
    {
        await AddItem(catalogItemId: 2, quantity: 1);

        var response = await AddItem(catalogItemId: 2, quantity: 1);

        Assert.True(response.Is(out Response<BasketSnapshot>? basket));
        var line = Assert.Single(basket!.Message.Items);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(8.50m, line.UnitPrice);
    }

    [Fact]
    public async Task AddItemToBasket_with_quantity_zero_is_rejected()
    {
        var response = await AddItem(catalogItemId: 2, quantity: 0);

        Assert.True(response.Is(out Response<BasketRequestRejected>? _));
    }

    [Fact]
    public async Task SetBasketQuantities_for_unknown_basket_is_rejected()
    {
        var client = _harness.GetRequestClient<SetBasketQuantities>();

        var response = await client.GetResponse<BasketSnapshot, BasketRequestRejected>(new SetBasketQuantities
        {
            BasketId = 999,
            Quantities = new List<BasketQuantity> { new() { BasketItemId = 1, Quantity = 1 } }
        });

        Assert.True(response.Is(out Response<BasketRequestRejected>? _));
    }

    [Fact]
    public async Task TransferBasket_moves_anonymous_items_to_the_user()
    {
        var anonymousId = Guid.NewGuid().ToString();
        await AddItem(catalogItemId: 2, quantity: 1, buyerId: anonymousId);

        var client = _harness.GetRequestClient<TransferBasket>();
        var response = await client.GetResponse<BasketSnapshot, BasketRequestRejected>(
            new TransferBasket { AnonymousBuyerId = anonymousId, UserName = Buyer });

        Assert.True(response.Is(out Response<BasketSnapshot>? userBasket));
        Assert.Equal(Buyer, userBasket!.Message.BuyerId);
        Assert.Single(userBasket.Message.Items);
        Assert.Empty((await GetBasket(anonymousId)).Items);
    }

    [Fact]
    public async Task OrderCreated_removes_ordered_items_and_deletes_empty_basket()
    {
        await AddItem(catalogItemId: 2, quantity: 1);

        await _harness.Bus.Publish(OrderFor(orderId: 1, catalogItemId: 2, units: 1));

        Assert.True(await _harness.GetConsumerHarness<RemoveOrderedItemsConsumer>().Consumed.Any<OrderCreated>());
        var basket = await GetBasket(Buyer);
        Assert.Equal(0, basket.BasketId);
        Assert.Empty(basket.Items);
    }

    [Fact]
    public async Task OrderCreated_delivered_twice_is_only_processed_once()
    {
        // Idempotent Receiver: samme ordre to gange må kun trække varerne fra én gang.
        await AddItem(catalogItemId: 2, quantity: 1);
        var consumer = _harness.GetConsumerHarness<RemoveOrderedItemsConsumer>();

        var firstDelivery = Guid.NewGuid();
        await _harness.Bus.Publish(OrderFor(orderId: 7, catalogItemId: 2, units: 1), ctx => ctx.MessageId = firstDelivery);
        Assert.True(await consumer.Consumed.Any<OrderCreated>(m => m.Context.MessageId == firstDelivery));

        // Brugeren lægger varen i kurven igen, og så kommer den samme ordre en gang til.
        await AddItem(catalogItemId: 2, quantity: 1);
        var secondDelivery = Guid.NewGuid();
        await _harness.Bus.Publish(OrderFor(orderId: 7, catalogItemId: 2, units: 1), ctx => ctx.MessageId = secondDelivery);
        Assert.True(await consumer.Consumed.Any<OrderCreated>(m => m.Context.MessageId == secondDelivery));

        var line = Assert.Single((await GetBasket(Buyer)).Items);
        Assert.Equal(1, line.Quantity);
    }

    [Fact]
    public async Task GetBasket_after_order_waits_until_the_order_is_removed_from_the_basket()
    {
        // Read-your-writes: Web spørger lige efter checkout. Svaret må ikke vise de varer, der netop er købt,
        // selvom GetBasket når frem før OrderCreated.
        await AddItem(catalogItemId: 2, quantity: 1);
        var client = _harness.GetRequestClient<GetBasket>();

        var reply = client.GetResponse<BasketSnapshot>(new GetBasket { BuyerId = Buyer, AfterOrderId = 3 });
        await _harness.Bus.Publish(OrderFor(orderId: 3, catalogItemId: 2, units: 1));

        var basket = (await reply).Message;
        Assert.Empty(basket.Items);
    }

    private async Task<Response<BasketSnapshot, BasketRequestRejected>> AddItem(int catalogItemId, int quantity, string buyerId = Buyer)
    {
        var client = _harness.GetRequestClient<AddItemToBasket>();
        return await client.GetResponse<BasketSnapshot, BasketRequestRejected>(new AddItemToBasket
        {
            BuyerId = buyerId,
            CatalogItemId = catalogItemId,
            UnitPrice = 8.50m,
            Quantity = quantity
        });
    }

    private async Task<BasketSnapshot> GetBasket(string buyerId)
    {
        var client = _harness.GetRequestClient<GetBasket>();
        var response = await client.GetResponse<BasketSnapshot>(new GetBasket { BuyerId = buyerId });
        return response.Message;
    }

    private static OrderCreated OrderFor(int orderId, int catalogItemId, int units)
    {
        return new OrderCreated
        {
            OrderId = orderId,
            BuyerId = Buyer,
            OrderDate = DateTimeOffset.UtcNow,
            Total = 8.50m * units,
            Lines = new List<OrderCreatedLine>
            {
                new() { CatalogItemId = catalogItemId, ProductName = ".NET Bot Black Sweatshirt", UnitPrice = 8.50m, Units = units }
            }
        };
    }
}
