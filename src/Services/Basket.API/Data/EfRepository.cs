using Ardalis.Specification.EntityFrameworkCore;
using BasketApi.Domain;

namespace BasketApi.Data;

public class EfRepository<T> : RepositoryBase<T>, IRepository<T> where T : class, IAggregateRoot
{
    public EfRepository(BasketDbContext dbContext) : base(dbContext)
    {
    }
}
