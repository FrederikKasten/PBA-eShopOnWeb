using System;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Transactional Outbox: en besked, der skal publiceres til RabbitMQ.
/// Den gemmes i samme database og samme transaktion som ordren. Så findes beskeden, hvis og kun hvis ordren findes,
/// også selvom RabbitMQ er nede i det øjeblik. OutboxDispatcher i Web sender den bagefter.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    // Fulde navn på kontrakten, fx "Contracts.OrderCreated".
    public string MessageType { get; private set; } = string.Empty;

    // Beskeden som JSON.
    public string Content { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    // null = ikke sendt endnu.
    public DateTime? SentAtUtc { get; private set; }

    // Sat, hvis beskeden ikke kan læses igen (ukendt type eller ugyldig JSON). Så prøves den ikke igen.
    public string? Error { get; private set; }

    public OutboxMessage(string messageType, string content)
    {
        MessageType = messageType;
        Content = content;
    }

    // Til EF Core.
    private OutboxMessage()
    {
    }

    public void MarkSent()
    {
        SentAtUtc = DateTime.UtcNow;
    }

    public void MarkInvalid(string error)
    {
        Error = error.Length > 2000 ? error[..2000] : error;
    }
}
