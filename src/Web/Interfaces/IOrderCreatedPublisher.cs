using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.Web.Interfaces;

public interface IOrderCreatedPublisher
{
    Task PublishAsync(Order order);
}
