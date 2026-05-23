namespace Stock.API.Events;

public class OrderCreatedEvent
{
    public int Id { get; set; }
    public string CustomerName { get; set; }
    public List<OrderItemValue> Items { get; set; }
}

public class OrderItemValue
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}