using Contracts;
using MassTransit;

namespace Ordering.API.Messaging;

public class SmallOrderConsumer : IConsumer<OrderCreated>
{
    private readonly ILogger<SmallOrderConsumer> _logger;

    public SmallOrderConsumer(ILogger<SmallOrderConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderCreated> context)
    {
        _logger.LogInformation("Lille ordre {OrderId} på {Total} – godkendes automatisk.",
            context.Message.OrderId, context.Message.Total);
        return Task.CompletedTask;
    }
}
