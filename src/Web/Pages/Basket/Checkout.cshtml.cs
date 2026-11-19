using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Interfaces;
using Contracts;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

[Authorize]
public class CheckoutModel : PageModel
{
    //private readonly IBasketService _basketService;
    private readonly IBasketClient _basketClient;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOrderPlacementService _orderPlacement;
    private string? _username = null;
    private readonly IBasketViewModelService _basketViewModelService;
    // ILogger<T> i stedet for IAppLogger<T>: Web-klasser bruger ILogger<T> 
    //private readonly IAppLogger<CheckoutModel> _logger;
    private readonly ILogger<CheckoutModel> _logger;
    //private readonly IOrderCreatedPublisher _orderCreatedPublisher;

    public CheckoutModel(IBasketClient basketClient,
            IBasketViewModelService basketViewModelService,
            SignInManager<ApplicationUser> signInManager,
            IOrderPlacementService orderPlacement,
            ILogger<CheckoutModel> logger)
    {
        _basketClient = basketClient;
        _signInManager = signInManager;
        _orderPlacement = orderPlacement;
        _basketViewModelService = basketViewModelService;
        _logger = logger;
    }

    public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

    public async Task OnGet()
    {
        await SetBasketModelAsync();
    }

    public async Task<IActionResult> OnPost(IEnumerable<BasketItemViewModel> items)
    {
        try
        {
            await SetBasketModelAsync();

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var quantities = items.Select(b => new BasketQuantity { BasketItemId = b.Id, Quantity = b.Quantity }).ToList();

            // 1) Request-Reply: gem de endelige antal i Basket.API og få den opdaterede kurv tilbage.
            var basket = await _basketClient.SetQuantitiesAsync(BasketModel.Id, quantities);

            // 2) Lav ordren ud fra kurvens linjer. Tom/ingen kurv giver EmptyBasketOnCheckoutException (fanges nedenfor).
            var orderItems = basket?.Items
                .Select(i => new OrderItemRequest(i.CatalogItemId, i.UnitPrice, i.Quantity))
                .ToList() ?? new List<OrderItemRequest>();

            // 3) Transactional Outbox: ordren og OrderCreated gemmes i samme transaktion. OutboxDispatcher publicerer
            //    beskeden bagefter (Publish-Subscribe) til både Ordering.API og Basket.API, som selv trækker varerne fra kurven.
            var order = await _orderPlacement.PlaceOrderAsync(BasketModel.BuyerId!, orderItems,
                new Address("123 Main St.", "Kent", "OH", "United States", "44240"));
            _logger.LogInformation("Order {OrderId} created from basket {BasketId} with {LineCount} lines",
                order.Id, BasketModel.Id, orderItems.Count);

            // 4) Read-your-writes: næste side (Success) beder Basket.API om at svare først, når ordren er trukket fra kurven,
            //    så kurv-ikonet ikke viser de varer, der lige er købt.
            TempData[Constants.PLACED_ORDER_ID] = order.Id;
        }
        catch (EmptyBasketOnCheckoutException)
        {
            //Redirect to Empty Basket page
            _logger.LogWarning("Checkout stopped: basket {BasketId} is empty", BasketModel.Id);
            return RedirectToPage("/Basket/Index");
        }

        return RedirectToPage("Success");
    }

    private async Task SetBasketModelAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        if (_signInManager.IsSignedIn(HttpContext.User))
        {
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(User.Identity.Name);
        }
        else
        {
            GetOrSetBasketCookieAndUserName();
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(_username!);
        }
    }

    private void GetOrSetBasketCookieAndUserName()
    {
        if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
        {
            _username = Request.Cookies[Constants.BASKET_COOKIENAME];
        }
        if (_username != null) return;

        _username = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions();
        cookieOptions.Expires = DateTime.Today.AddYears(10);
        Response.Cookies.Append(Constants.BASKET_COOKIENAME, _username, cookieOptions);
    }
}
