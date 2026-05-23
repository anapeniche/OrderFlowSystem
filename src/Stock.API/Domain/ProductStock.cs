namespace Stock.API.Domain;

public class ProductStock
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}