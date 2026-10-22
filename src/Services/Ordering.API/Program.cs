using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.API.Data;
using Ordering.API.Messaging;
using Ordering.API.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrderingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("OrderingDb")));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbit = builder.Configuration.GetSection("RabbitMq");
        cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
        {
            h.Username(rabbit["Username"] ?? "guest");
            h.Password(rabbit["Password"] ?? "guest");
        });

        cfg.ReceiveEndpoint("ordering-order-created", e =>
        {
            e.ConfigureConsumer<OrderCreatedConsumer>(context);
        });
    });
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/", () => "Ordering.API kører");
app.MapOrderEndpoints();
app.Run();
