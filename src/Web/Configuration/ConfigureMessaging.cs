using Contracts;
using MassTransit;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.eShopWeb.Web.Services;
using RabbitMQ.Client;

namespace Microsoft.eShopWeb.Web.Configuration;

public static class ConfigureMessaging
{
    // Hvor længe Web venter på svar fra Basket.API (samme 5 sekunder som de andre kald til services).
    private static readonly RequestTimeout BasketRequestTimeout = RequestTimeout.After(s: 5);

    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbit = configuration.GetSection("RabbitMq");

        services.AddMassTransit(x =>
        {
            // Request-Reply til Basket.API: hver request-type sendes direkte til sin egen kø (Point-to-Point).
            x.AddRequestClient<GetBasket>(BasketQueues.Address(BasketQueues.GetBasket), BasketRequestTimeout);
            x.AddRequestClient<AddItemToBasket>(BasketQueues.Address(BasketQueues.AddItem), BasketRequestTimeout);
            x.AddRequestClient<SetBasketQuantities>(BasketQueues.Address(BasketQueues.SetQuantities), BasketRequestTimeout);
            x.AddRequestClient<TransferBasket>(BasketQueues.Address(BasketQueues.Transfer), BasketRequestTimeout);

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

        // Transactional Outbox: ordre + OrderCreated i én transaktion, og en baggrundstjeneste der sender beskederne.
        services.AddScoped<IOrderPlacementService, OrderPlacementService>();
        services.AddSingleton<OutboxSignal>();
        services.AddHostedService<OutboxDispatcher>();
        // Circuit Breaker: singleton, så alle requests deler samme tilstand (lukket, åben, halvåben).
        services.AddSingleton<BasketCircuitBreaker>();
        services.AddScoped<IBasketClient, BasketMessagingClient>();

        return services;
    }
}
