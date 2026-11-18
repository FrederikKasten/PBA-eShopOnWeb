namespace Microsoft.eShopWeb.Web.Interfaces;

/// <summary>
/// Kastes af IBasketClient, når Basket.API ikke kan nås: timeout, fejl i Basket.API, RabbitMQ nede eller åbent kredsløb.
/// Kalderne skal kun kende denne ene exception – ikke MassTransit's eller Polly's (Messaging Gateway).
/// </summary>
public class BasketUnavailableException : Exception
{
    public BasketUnavailableException(string message, Exception innerException, bool circuitOpen)
        : base(message, innerException)
    {
        CircuitOpen = circuitOpen;
    }

    // true = kaldet blev slet ikke sendt, fordi kredsløbet er åbent (Basket.API har fejlet for nylig).
    public bool CircuitOpen { get; }
}
