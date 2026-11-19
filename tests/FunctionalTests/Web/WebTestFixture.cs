using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.eShopWeb.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.FunctionalTests.Web;

public class TestApplication : WebApplicationFactory<IBasketViewModelService>
{
    private readonly string _environment = "Development";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        // Add mock/test services to the builder here
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d =>
                                                d.ServiceType == typeof(DbContextOptions<CatalogContext>) ||
                                                d.ServiceType == typeof(DbContextOptions<AppIdentityDbContext>))
                                            .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddScoped(sp =>
            {
                // Replace SQLite with in-memory database for tests
                return new DbContextOptionsBuilder<CatalogContext>()
                .UseInMemoryDatabase("InMemoryDbForTesting")
                .UseApplicationServiceProvider(sp)
                .Options;
            });
            services.AddScoped(sp =>
            {
                // Replace SQLite with in-memory database for tests
                return new DbContextOptionsBuilder<AppIdentityDbContext>()
                .UseInMemoryDatabase("Identity")
                .UseApplicationServiceProvider(sp)
                .Options;
            });

            // Basket.API og RabbitMQ kører ikke under testene – brug en in-memory udgave af klienten i stedet.
            services.RemoveAll<IBasketClient>();
            services.AddSingleton<IBasketClient, InMemoryBasketClient>();

            // OutboxDispatcher ville prøve at sende til RabbitMQ, som ikke kører. Uden den bliver beskederne
            // liggende i OutboxMessages, hvor testene kan se dem.
            // (IsKeyedService først: ImplementationType kaster en exception på keyed services i .NET 8.)
            foreach (var dispatcher in services.Where(d => !d.IsKeyedService && d.ImplementationType == typeof(OutboxDispatcher)).ToList())
            {
                services.Remove(dispatcher);
            }
        });

        return base.CreateHost(builder);
    }
}
