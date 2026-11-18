using Ardalis.Specification;
using BasketApi.Domain;

namespace BasketApi.Data;

// Samme abstraktion som i monolitten, så BasketService og dens tests kan flyttes uændret.
public interface IRepository<T> : IRepositoryBase<T> where T : class, IAggregateRoot
{
}
