using System.Text;
using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
    x.AddConsumer<StockReservedConsumer>();
    x.AddConsumer<StockReservationRejectedConsumer>();

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
            // Fejler databasen eller RabbitMQ et øjeblik, prøves beskeden igen. Derefter ordering-order-created_error.
            e.UseMessageRetry(r => r.Intervals(500, 1000, 2000));
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

        // Kø 4: lagerets svar. MassTransit binder køen til exchangerne for StockReserved og
        // StockReservationRejected (Publish-Subscribe), fordi vi bruger standard-topologien her.
        cfg.ReceiveEndpoint("ordering-stock-results", e =>
        {
            e.UseMessageRetry(r => r.Intervals(500, 1000, 2000));
            e.ConfigureConsumer<StockReservedConsumer>(context);
            e.ConfigureConsumer<StockReservationRejectedConsumer>(context);
        });
    });
});

// 3) POST /orders/{id}/cancel kræver et JWT fra PublicApi (samme nøgle som PublicApi og Basket.API).
//    GET-endpoints er stadig åbne, fordi Web kalder dem uden token.
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey is missing from the configuration");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtSecretKey)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// 4) Create database if it doesn't exist yet (for demo purposes).
//    NB: EnsureCreated() tilføjer ikke nye kolonner til en database, der allerede findes.
//    Efter RejectionReason kom til, skal ordering.db slettes én gang (docker compose down -v).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    db.Database.EnsureCreated();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Ordering.API kører");
app.MapOrderEndpoints();

app.Run();
