namespace Contracts;

/// <summary>
/// Routing keys for OrderCreated on the topic exchange.
/// Sender chooses the key based on the order content; RabbitMQ sends it to the queues that match.
/// </summary>
public static class OrderRoutingKeys
{
    //Orders at this amount or more are considered large.
    public const decimal LargeOrderThreshold = 100m;

    public const string Large = "order.large";
    public const string Small = "order.small";

    // all orders, for example for the Shipping service, which needs to ship all orders
    public const string All = "order.#";

    public static string For(OrderCreated order) =>
        order.Total >= LargeOrderThreshold ? Large : Small;


    //Note fra Niklas
    // At vi bruger routing keys er udenlukkende for at demonstrere, hvordan man kan bruge routing keys i RabbitMQ.
    // I praksis ville man nok bare have en queue, der modtager alle OrderCreated events, og så filtrere i forbrugeren.
    // men det ville ikke demonstrere routing keys, så derfor har vi lavet det her eksempel.
}
