namespace Contracts;

/// <summary>
/// Kønavne i RabbitMQ for Inventory.API.
/// </summary>
public static class InventoryQueues
{
    // Point-to-Point: Ordering.API sender ReserveStock direkte hertil.
    public const string ReserveStock = "inventory-reserve-stock";

    // Publish-Subscribe: Inventory.API's egen kø på OrderCancelled-exchangen.
    public const string OrderCancelled = "inventory-order-cancelled";

    /// <summary>Adressen en afsender bruger, fx "queue:inventory-reserve-stock".</summary>
    public static Uri Address(string queueName) => new($"queue:{queueName}");
}
