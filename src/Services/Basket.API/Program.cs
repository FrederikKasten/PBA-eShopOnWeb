using System.Text;
using BasketApi.Data;
using BasketApi.Endpoints;
using BasketApi.Messaging;
using BasketApi.Services;
using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RabbitMQ.Client;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 0) Al ILogger<T>-output går gennem Serilog: konsol + Logstash (se "Serilog" i appsettings.*.json).
//    Samme opsætning som Web og PublicApi, så alle tre services lander i app-logs-* i Kibana.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// 1) Egen database (SQLite) – Basket.API er den eneste, der kender den.
builder.Services.AddDbContext<BasketDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("BasketDb")));

// 2) Repository + BasketService (samme opbygning som før i monolitten).
builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped<IBasketService, BasketService>();

// 3) RabbitMQ via MassTransit – samme opbygning som i Ordering.API.
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<GetBasketConsumer>();
    x.AddConsumer<AddItemToBasketConsumer>();
    x.AddConsumer<SetBasketQuantitiesConsumer>();
    x.AddConsumer<TransferBasketConsumer>();
    x.AddConsumer<RemoveOrderedItemsConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbit = builder.Configuration.GetSection("RabbitMq");
        cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbit["Username"] ?? "guest");
            h.Password(rabbit["Password"] ?? "guest");
        });

        // OrderCreated ligger i en TOPIC exchange (skal matche Web og Ordering.API).
        cfg.Publish<OrderCreated>(p => p.ExchangeType = ExchangeType.Topic);

        // Request-Reply fra Web: én kø pr. request-type (Point-to-Point + Datatype Channel).
        PointToPointQueue<GetBasketConsumer>(cfg, context, BasketQueues.GetBasket);
        PointToPointQueue<AddItemToBasketConsumer>(cfg, context, BasketQueues.AddItem);
        PointToPointQueue<SetBasketQuantitiesConsumer>(cfg, context, BasketQueues.SetQuantities);
        PointToPointQueue<TransferBasketConsumer>(cfg, context, BasketQueues.Transfer);

        // Publish-Subscribe: Basket.API's egen kø på OrderCreated-exchangen, alle ordrer ("order.#").
        // Ordering.API har sine egne køer på samme exchange, så begge services får hver ordre.
        cfg.ReceiveEndpoint(BasketQueues.OrderCreated, e =>
        {
            e.ConfigureConsumeTopology = false;
            e.Bind<OrderCreated>(b =>
            {
                b.ExchangeType = ExchangeType.Topic;
                b.RoutingKey = OrderRoutingKeys.All;
            });

            // Fejler databasen et øjeblik, prøves beskeden igen. Efter 3 forsøg flytter MassTransit den
            // til køen basket-order-created_error, så den ikke går tabt og kan sendes igen bagefter.
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(1)));
            e.ConfigureConsumer<RemoveOrderedItemsConsumer>(context);
        });
    });
});

// 4) HTTP-endpoints kræver et JWT fra PublicApi (POST /api/authenticate). Samme nøgle og samme opsætning som PublicApi,
//    så API Gateway'en kan sende brugerens token videre. Nøglen står i "Jwt:SecretKey" og skal være den samme som i PublicApi.
//    RabbitMQ-delen berøres ikke: der er det adgangen til RabbitMQ (brugernavn og adgangskode), der beskytter køerne.
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

// 5) Opdatér databasen med migrations (Data/Migrations). En ny tabel eller kolonne når så også en database,
//    der allerede findes – det kunne EnsureCreated() ikke. Tre udfald logges forskelligt: opdateret, allerede opdateret, fejl.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BasketDbContext>();
    var dataSource = db.Database.GetDbConnection().DataSource;

    // Uden migrations ville Migrate() ikke oprette nogen tabeller, og alle beskeder ville fejle. Stop hellere med det samme.
    if (!db.Database.GetMigrations().Any())
    {
        app.Logger.LogCritical("Basket.API has no migrations. Create them with: dotnet ef migrations add InitialCreate --project src/Services/Basket.API --output-dir Data/Migrations");
        throw new InvalidOperationException("Basket.API has no migrations (Data/Migrations is empty)");
    }

    try
    {
        var pendingMigrations = db.Database.GetPendingMigrations().ToList();
        if (pendingMigrations.Count == 0)
        {
            app.Logger.LogInformation("Basket database at {DataSource} is up to date", dataSource);
        }
        else
        {
            db.Database.Migrate();
            app.Logger.LogInformation("Applied {MigrationCount} migrations to the basket database at {DataSource}: {Migrations}",
                pendingMigrations.Count, dataSource, pendingMigrations);
        }
    }
    catch (Exception ex)
    {
        // Typisk årsag: en basket.db fra før migrations, lavet af EnsureCreated(). Den skal slettes én gang.
        app.Logger.LogCritical(ex,
            "Could not migrate the basket database at {DataSource}. A database made by EnsureCreated() must be deleted once (docker compose down -v)",
            dataSource);
        throw;
    }
}

// 6) Én linje pr. HTTP-kald: metode, sti, statuskode og varighed (felterne requestPath, statusCode, elapsed i Kibana).
//    HTTP-endpoints er til API Gateway og test – Web bruger RabbitMQ.
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Basket.API kører");
app.MapBasketEndpoints();

app.Run();

// En kø som Web sender requests direkte til. Køen bindes ikke til en exchange for beskedtypen
// (ConfigureConsumeTopology = false), så kun den der kender køens navn kan sende til den.
// Retry: 3 hurtige forsøg, så et svar stadig kan nå frem inden Web's timeout på 5 sekunder.
// Fejler alle forsøg, får Web en fejl tilbage (RequestFaultException), og beskeden ender i <kø>_error.
static void PointToPointQueue<TConsumer>(IRabbitMqBusFactoryConfigurator cfg, IBusRegistrationContext context, string queueName)
    where TConsumer : class, IConsumer
{
    cfg.ReceiveEndpoint(queueName, e =>
    {
        e.ConfigureConsumeTopology = false;
        e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromMilliseconds(200)));
        e.ConfigureConsumer<TConsumer>(context);
    });
}
