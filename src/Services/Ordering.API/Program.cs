using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Data;
using Ordering.API.Endpoints;
using Ordering.API.Messaging;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// 1) Database:
builder.Services.AddDbContext<OrderingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("OrderingDb")));

// 2) RabbitMQ via MassTransit:
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();
    x.AddConsumer<LargeOrderConsumer>();
    x.AddConsumer<SmallOrderConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbit = builder.Configuration.GetSection("RabbitMq");
        cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbit["Username"] ?? "guest");
            h.Password(rabbit["Password"] ?? "guest");
        });

        // OrderCreated is located in aTOPIC exchange (must match the sender in Web).
        cfg.Publish<OrderCreated>(p => p.ExchangeType = ExchangeType.Topic);

        // Kø 1: ALL ordrer ("order.#")
        cfg.ReceiveEndpoint("ordering-order-created", e =>
        {
            e.ConfigureConsumeTopology = false; 
            e.Bind<OrderCreated>(b =>
            {
                b.ExchangeType = ExchangeType.Topic;
                b.RoutingKey = OrderRoutingKeys.All;
            });
            e.ConfigureConsumer<OrderCreatedConsumer>(context);
        });

        // Kø 2: ("order.large").
        cfg.ReceiveEndpoint("ordering-large-orders", e =>
        {
            e.ConfigureConsumeTopology = false;
            e.Bind<OrderCreated>(b =>
            {
                b.ExchangeType = ExchangeType.Topic;
                b.RoutingKey = OrderRoutingKeys.Large;
            });
            e.ConfigureConsumer<LargeOrderConsumer>(context);
        });

        // Kø 3: ("order.small").
        cfg.ReceiveEndpoint("ordering-small-orders", e =>
        {
            e.ConfigureConsumeTopology = false;
            e.Bind<OrderCreated>(b =>
            {
                b.ExchangeType = ExchangeType.Topic;
                b.RoutingKey = OrderRoutingKeys.Small;
            });
            e.ConfigureConsumer<SmallOrderConsumer>(context);
        });
    });
});

var app = builder.Build();

// 3) Create database if it doesn't exist yet (for demo purposes).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/", () => "Ordering.API kører");
app.MapOrderEndpoints();

app.Run();
