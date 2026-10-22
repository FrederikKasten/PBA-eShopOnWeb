namespace Ordering.API.Domain;

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string PictureUri { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}
