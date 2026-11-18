namespace BasketApi.Domain;

// Markør: kun aggregate roots må have et repository.
// Basket.API må ikke referere ApplicationCore (ellers er den ikke selvstændig), så de to små basistyper (denne her og BaseEntity) kopieres ind.
public interface IAggregateRoot
{
}
