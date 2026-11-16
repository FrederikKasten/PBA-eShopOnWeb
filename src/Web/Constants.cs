namespace Microsoft.eShopWeb.Web;

public static class Constants
{
    public const string BASKET_COOKIENAME = "eShop";
    public const int ITEMS_PER_PAGE = 10;
    public const string DEFAULT_USERNAME = "Guest";
    public const string BASKET_ID = "BasketId";

    // TempData-nøgle: id på den ordre, der lige er lagt. Læses af kurv-ikonet på den næste side.
    public const string PLACED_ORDER_ID = "PlacedOrderId";
}
