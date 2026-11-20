namespace Contracts;

/// <summary>
/// Queue names in RabbitMQ for Basket.API.
/// Web sends requests directly to these queues (point-to-point), and Basket.API reads from them.
/// One queue per message type, so a queue always contains the same kind of message.
/// </summary>
public static class BasketQueues
{
    public const string GetBasket = "basket-get-basket";
    public const string AddItem = "basket-add-item";
    public const string SetQuantities = "basket-set-quantities";
    public const string Transfer = "basket-transfer";

    // Basket.API's own queue on the OrderCreated topic exchange (routing key "order.#").
    // Ordering.API has its own queues on the same exchange, so both get every order.
    public const string OrderCreated = "basket-order-created";

    /// <summary>The address Web uses to send to a queue, e.g. "queue:basket-add-item".</summary>
    public static Uri Address(string queueName) => new($"queue:{queueName}");
}
