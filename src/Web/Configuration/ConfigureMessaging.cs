using Contracts;
using MassTransit;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureMessaging
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbit = configuration.GetSection("RabbitMq");

        services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbit["Host"] ?? "localhost", "/", h =>
                {
                    h.Username(rabbit["Username"] ?? "guest");
                    h.Password(rabbit["Password"] ?? "guest");
                });

                // Content-based router
                cfg.Publish<OrderCreated>(p => p.ExchangeType = ExchangeType.Topic);

                // Each message gets a routing key based on its content (large/small order).
                cfg.Send<OrderCreated>(s => s.UseRoutingKeyFormatter(ctx => OrderRoutingKeys.For(ctx.Message)));
            });
        });

        services.AddScoped<IOrderCreatedPublisher, OrderCreatedPublisher>();

        return services;
    }
}
