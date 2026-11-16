using Contracts;
using Microsoft.eShopWeb.Web.Pages.Basket;

namespace Microsoft.eShopWeb.Web.Interfaces;

public interface IBasketViewModelService
{
    Task<BasketViewModel> GetOrCreateBasketForUser(string userName);
    //Task<int> CountTotalBasketItems(string username);
    Task<BasketViewModel> Map(BasketSnapshot basket);

    // afterOrderId: id på en ordre, der lige er lagt. Så tæller Basket.API først, når den er trukket fra kurven.
    Task<int> CountTotalBasketItems(string username, int? afterOrderId = null);
}
