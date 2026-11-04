using System.Net;
using Microsoft.eShopWeb.Web.Interfaces;

namespace Microsoft.eShopWeb.Web.Services;

public class OrderingApiClient : IOrderingApiClient
{
    private readonly HttpClient _httpClient;

    // HttpClient bliver givet af ASP.NET Core med BaseAddress sat (se ConfigureWebServices).
    public OrderingApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<OrderingApiOrderSummary>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var url = $"orders?buyerId={Uri.EscapeDataString(buyerId)}";
        var orders = await _httpClient.GetFromJsonAsync<List<OrderingApiOrderSummary>>(url, cancellationToken);
        return orders ?? new List<OrderingApiOrderSummary>();
    }

    public async Task<OrderingApiOrderDetails?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"orders/{orderId}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderingApiOrderDetails>(cancellationToken: cancellationToken);
    }
}
