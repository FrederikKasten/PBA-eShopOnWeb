using Contracts;

namespace Microsoft.eShopWeb.Web.Interfaces;
/// Kan Basket.API ikke nås, kastes BasketUnavailableException.
/// <summary>
/// Messaging Gateway: siderne i Web kalder denne interface og ved ikke, at kurven ligger i
/// Basket.API bag RabbitMQ. Kun BasketMessagingClient kender beskederne og køerne.
/// Svarene er kontrakterne fra Contracts (BasketSnapshot) – samme fælles format som OrderCreated.
/// </summary>
public interface IBasketClient
{
    /// <summary>Køberens kurv. Har køberen ingen kurv, er BasketId 0 og listen tom.</summary>
    Task<BasketSnapshot> GetBasketAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Antal varer i alt. Med afterOrderId svarer Basket.API først, når den ordre er trukket fra kurven (højst 2 sekunder).
    /// </summary>
    Task<int> CountTotalBasketItemsAsync(string buyerId, int? afterOrderId = null, CancellationToken cancellationToken = default);

    Task<BasketSnapshot> AddItemAsync(string buyerId, int catalogItemId, decimal unitPrice, int quantity = 1, CancellationToken cancellationToken = default);

    /// <summary>Returnerer null, hvis Basket.API afviser (ukendt kurv eller negativt antal).</summary>
    Task<BasketSnapshot?> SetQuantitiesAsync(int basketId, IReadOnlyCollection<BasketQuantity> quantities, CancellationToken cancellationToken = default);

    Task TransferBasketAsync(string anonymousBuyerId, string userName, CancellationToken cancellationToken = default);
}
