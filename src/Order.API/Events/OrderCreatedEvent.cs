namespace Order.API.Events;

public class OrderCreatedEvent
{
    public int OrderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderItemEventDto> Items { get; set; } = new();
}

public class OrderItemEventDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
