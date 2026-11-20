using System.Text;
using Contracts;
using Inventory.API.Data;
using Inventory.API.Endpoints;
using Inventory.API.Health;
using Inventory.API.Messaging;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 0) Logning: konsol + Logstash (se "Serilog" i appsettings.*.json), samme opsætning som Basket.API.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// 1) Egen database (SQLite). Kun Inventory.API kender den.
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("InventoryDb")));

// 2) RabbitMQ via MassTransit.
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ReserveStockConsumer>();
    x.AddConsumer<OrderCancelledConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbit = builder.Configuration.GetSection("RabbitMq");
        cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbit["Username"] ?? "guest");
            h.Password(rabbit["Password"] ?? "guest");
        });

        // Point-to-Point: Ordering.API sender ReserveStock direkte til denne kø. Køen bindes ikke til
        // en exchange for beskedtypen, så kun den, der kender køens navn, kan sende hertil.
        cfg.ReceiveEndpoint(InventoryQueues.ReserveStock, e =>
        {
            e.ConfigureConsumeTopology = false;

            // Køber to kunder den sidste vare samtidig, fejler den ene SaveChanges (concurrency token).
            // Næste forsøg læser beholdningen igen og afviser ordren. En ugyldig besked (ArgumentException)
            // prøves ikke igen. Fejler alle forsøg, ender beskeden i inventory-reserve-stock_error.
            e.UseMessageRetry(r =>
            {
                r.Intervals(100, 500, 1000);
                r.Ignore<ArgumentException>();
            });
            e.ConfigureConsumer<ReserveStockConsumer>(context);
        });

        // Publish-Subscribe: egen kø, som MassTransit binder til exchangen for OrderCancelled.
        cfg.ReceiveEndpoint(InventoryQueues.OrderCancelled, e =>
        {
            e.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(1)));
            e.ConfigureConsumer<OrderCancelledConsumer>(context);
        });
    });
});

// 3) HTTP-endpoints kræver et JWT fra PublicApi (POST /api/authenticate) med samme nøgle som PublicApi og Basket.API.
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
builder.Services.AddAuthorization(options =>
    // Samme rollenavn som i monolitten (BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS).
    options.AddPolicy("admin", policy => policy.RequireRole("Administrators")));

// 4) /health: databasen (DatabaseHealthCheck) og RabbitMQ (MassTransit tilføjer selv sit tjek).
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// 5) OpenAPI-beskrivelse og Swagger UI på /swagger. Tryk "Authorize" og indsæt tokenet fra PublicApi.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Inventory.API", Version = "v1" });
    var bearer = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT fra PublicApi: POST /api/authenticate",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", bearer);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = Array.Empty<string>() });
});

var app = builder.Build();

// 6) Opdatér databasen med migrations (Data/Migrations), ligesom Basket.API. Tre udfald logges forskelligt.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    var dataSource = db.Database.GetDbConnection().DataSource;

    if (!db.Database.GetMigrations().Any())
    {
        app.Logger.LogCritical("Inventory.API has no migrations. Create them with: dotnet ef migrations add InitialCreate --project src/Services/Inventory.API --startup-project src/Services/Inventory.API --output-dir Data/Migrations");
        throw new InvalidOperationException("Inventory.API has no migrations (Data/Migrations is empty)");
    }

    try
    {
        var pendingMigrations = db.Database.GetPendingMigrations().ToList();
        if (pendingMigrations.Count == 0)
        {
            app.Logger.LogInformation("Inventory database at {DataSource} is up to date", dataSource);
        }
        else
        {
            db.Database.Migrate();
            app.Logger.LogInformation("Applied {MigrationCount} migrations to the inventory database at {DataSource}: {Migrations}",
                pendingMigrations.Count, dataSource, pendingMigrations);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "Could not migrate the inventory database at {DataSource}", dataSource);
        throw;
    }
}

// Én linje pr. HTTP-kald i loggen (metode, sti, statuskode, varighed).
app.UseSerilogRequestLogging();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Inventory.API kører");
app.MapHealthChecks("/health");
app.MapStockEndpoints();

app.Run();

