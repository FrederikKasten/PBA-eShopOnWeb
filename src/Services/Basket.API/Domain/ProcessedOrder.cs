namespace BasketApi.Domain;

// Idempotent Receiver: hvilke OrderCreated-beskeder Basket.API allerede har behandlet.
// RabbitMQ leverer "mindst én gang", så samme ordre kan komme igen. Uden denne tabel ville
// en dublet trække varerne fra kurven to gange.
public class ProcessedOrder
{
    public int OrderId { get; private set; }
    public DateTime ProcessedAtUtc { get; private set; }

    public ProcessedOrder(int orderId)
    {
        OrderId = orderId;
        ProcessedAtUtc = DateTime.UtcNow;
    }
}
