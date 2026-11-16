namespace BasketApi.Domain;

// Kopi af monolittens BaseEntity. Id er virtual, så testene kan lave en
// NSubstitute-kopi af Basket og sætte Id (se BasketBuilder).
// Basket.API må ikke referere ApplicationCore (ellers er den ikke selvstændig), så de to små basistyper (denne her og IAggregateRoot) kopieres ind.
public abstract class BaseEntity
{
    public virtual int Id { get; protected set; }
}
