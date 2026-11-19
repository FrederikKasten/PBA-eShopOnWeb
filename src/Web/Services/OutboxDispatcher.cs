using System.Text.Json;
using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Web.Services;

/// <summary>
/// Message Relay: sender beskeder fra tabellen OutboxMessages til RabbitMQ.
/// Den vækkes, så snart en ordre er gemt (OutboxSignal), og kigger desuden selv hvert sekund (Polling Publisher),
/// så beskeder fra før en genstart eller fra et RabbitMQ-udfald også kommer af sted.
///
/// Leverer mindst én gang: går Web ned mellem afsendelsen og MarkSent, sendes beskeden igen ved næste start.
/// Det tåler modtagerne, fordi de er Idempotent Receivers (Basket.API: ProcessedOrders, Ordering.API: ordre-id).
/// Beskedens MessageId er outbox-rækkens Id, så en gentagelse har samme MessageId som originalen.
/// </summary>
public class OutboxDispatcher : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DelayAfterFailure = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBus _bus;
    private readonly OutboxSignal _signal;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(IServiceScopeFactory scopeFactory, IBus bus, OutboxSignal signal, ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _bus = bus;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollInterval;
            try
            {
                if (!await DispatchPendingAsync(stoppingToken))
                {
                    delay = DelayAfterFailure;
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Fx er databasen nede. Baggrundstjenesten må ikke dø af det; den prøver igen senere.
                _logger.LogError(ex, "Outbox dispatch failed; trying again in {DelaySeconds} s", DelayAfterFailure.TotalSeconds);
                delay = DelayAfterFailure;
            }

            try
            {
                await _signal.WaitAsync(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Web lukker ned.
                return;
            }
        }
    }

    // Returnerer false, hvis RabbitMQ ikke tog imod en besked. Resten venter så til næste runde, i samme rækkefølge.
    private async Task<bool> DispatchPendingAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.SentAtUtc == null && m.Error == null)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(stoppingToken);

        foreach (var outboxMessage in pending)
        {
            var message = Read(outboxMessage);
            if (message is null)
            {
                // Invalid Message Channel: beskeden kan aldrig sendes. Den markeres og springes over, så den ikke blokerer de andre.
                await db.SaveChangesAsync(stoppingToken);
                continue;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(PublishTimeout);
                await _bus.Publish(message, message.GetType(), context => context.MessageId = outboxMessage.Id, timeout.Token);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Could not publish outbox message {OutboxMessageId}; RabbitMQ may be down. Trying again in {DelaySeconds} s",
                    outboxMessage.Id, DelayAfterFailure.TotalSeconds);
                return false;
            }

            // Gemmes efter hver besked, så et nedbrud midt i en runde kun kan gentage én besked.
            outboxMessage.MarkSent();
            await db.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("Published outbox message {OutboxMessageId} ({MessageType})", outboxMessage.Id, outboxMessage.MessageType);
        }

        return true;
    }

    // Læser beskeden tilbage til sin kontrakt-type (fx Contracts.OrderCreated). null = ulæselig; rækken er så markeret.
    private object? Read(OutboxMessage outboxMessage)
    {
        var messageType = typeof(OrderCreated).Assembly.GetType(outboxMessage.MessageType);
        if (messageType is null)
        {
            outboxMessage.MarkInvalid($"Unknown message type {outboxMessage.MessageType}");
            _logger.LogError("Outbox message {OutboxMessageId} has unknown type {MessageType} and will not be published",
                outboxMessage.Id, outboxMessage.MessageType);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(outboxMessage.Content, messageType)
                ?? throw new JsonException("The content is null");
        }
        catch (JsonException ex)
        {
            outboxMessage.MarkInvalid(ex.Message);
            _logger.LogError(ex, "Outbox message {OutboxMessageId} has invalid content and will not be published", outboxMessage.Id);
            return null;
        }
    }
}
