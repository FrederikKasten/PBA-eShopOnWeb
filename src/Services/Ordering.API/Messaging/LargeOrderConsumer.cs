using Contracts;
using MassTransit;

namespace Ordering.API.Messaging;
public class LargeOrderConsumer : IConsumer<OrderCreated>
{
    private readonly ILogger<LargeOrderConsumer> _logger;

    public LargeOrderConsumer(ILogger<LargeOrderConsumer> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<OrderCreated> context)
    {
        _logger.LogWarning("STOR ordre {OrderId} på {Total} – skal godkendes manuelt.",
            context.Message.OrderId, context.Message.Total);
        return Task.CompletedTask;
    }
}
