namespace Order.API.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Status { get; set; } = 0;

    public List<OrderItem> Items { get; set; } = new();
}
