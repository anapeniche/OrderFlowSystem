using RabbitMQ.Client;
using Microsoft.Extensions.Hosting;
using System.Text;
using System.Text.Json;
using StockFlowSystem.Data;
using Microsoft.Extensions.DependencyInjection;
using StockFlowSystem.Domain;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;


public record OrderCreatedEvent(Guid OrderId, Guid CustomerId, List<OrderItemDto> Items);
public record OrderItemDto(Guid ProductId, int Quantity);

public class OrderCreatedConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private IConnection _connection;
    private IModel _channel;
    private const string ExchangeName = "orderflow_exchange";
    private const string QueueName = "stock_queue_order_created";
    private const string RoutingKey = "OrderCreatedEvent"; 

    public OrderCreatedConsumer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        InitializeRabbitMq();
    }

    private void InitializeRabbitMq()
    {
        var factory = new ConnectionFactory() { HostName = "rabbitmq", Port = 5672, UserName = "guest", Password = "guest" };
        
        
        int retryCount = 0;
        while (retryCount < 10) 
        {
            try 
            {
                _connection = factory.CreateConnection();
                _channel = _connection.CreateModel();
                _channel.ExchangeDeclare(exchange: ExchangeName, type: ExchangeType.Direct);
                _channel.QueueDeclare(queue: QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
                _channel.QueueBind(queue: QueueName, exchange: ExchangeName, routingKey: RoutingKey);
                break;
            }
            catch 
            {
                retryCount++;
                Task.Delay(3000).Wait();
            }
        }
        if (_connection == null) throw new InvalidOperationException("Não foi possível conectar ao RabbitMQ.");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.ThrowIfCancellationRequested();

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            
            try
            {
                var orderEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(message, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (orderEvent != null)
                {
                    await ProcessOrder(orderEvent);
                    
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
            }
            catch (Exception ex)
            {
                
                _channel.BasicNack(ea.DeliveryTag, false, true); 
                Console.WriteLine($"Erro ao processar OrderCreatedEvent: {ex.Message}");
            }
        };

        _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }

    private async Task ProcessOrder(OrderCreatedEvent orderEvent)
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<StockDbContext>();

            
            [cite_start]
            foreach (var item in orderEvent.Items)
            {
                var stockItem = await context.StockItems.FindAsync(item.ProductId);

                if (stockItem == null || stockItem.Quantity < item.Quantity)
                {
                    [cite_start]
                    Console.WriteLine($"Estoque insuficiente ou produto {item.ProductId} inexistente para o pedido {orderEvent.OrderId}.");
                    return; 
                }

                
                stockItem.Quantity -= item.Quantity; 
                Console.WriteLine($"Baixado {item.Quantity} unidades do produto {item.ProductId}. Novo estoque: {stockItem.Quantity}");
            }

            await context.SaveChangesAsync();

            [cite_start]
            Console.WriteLine($"Estoque confirmado e atualizado para o pedido {orderEvent.OrderId}.");
        }
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        base.Dispose();
    }
}
