using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Npgsql;

namespace Order.API
{
    public class StockResponseWorker : BackgroundService
    {
        private readonly ILogger<StockResponseWorker> _logger;
        private readonly IConfiguration _configuration;
        private IConnection _connection;
        private IModel _channel;

        public StockResponseWorker(ILogger<StockResponseWorker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            InitRabbitMQ();
        }

        private void InitRabbitMQ()
        {
            var factory = new ConnectionFactory() { HostName = "rabbitmq_broker" };
            while (true)
            {
                try
                {
                    _connection = factory.CreateConnection();
                    _channel = _connection.CreateModel();
                    _channel.QueueDeclare(queue: "order_rejected_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    break;
                }
                catch
                {
                    _logger.LogWarning("Aguardando RabbitMQ...");
                    Thread.Sleep(2000);
                }
            }
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.Register(() => _connection?.Close());

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                
                try
                {
                    using var doc = JsonDocument.Parse(message);
                    int orderId = 0;
                    
                    if (doc.RootElement.TryGetProperty("OrderId", out var orderIdProp) || 
                        doc.RootElement.TryGetProperty("orderId", out orderIdProp))
                    {
                        orderId = orderIdProp.GetInt32();
                    }

                    if (orderId > 0)
                    {

                        var connectionString = _configuration.GetConnectionString("DefaultConnection")
                            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

                        using (var conn = new NpgsqlConnection(connectionString))
                        {
                            await conn.OpenAsync(stoppingToken);
                            string sql = "UPDATE \"Orders\" SET \"Status\" = 2 WHERE \"Id\" = @id";
                            
                            using (var cmd = new NpgsqlCommand(sql, conn))
                            {
                                cmd.Parameters.AddWithValue("id", orderId);
                                int rowsAffected = await cmd.ExecuteNonQueryAsync(stoppingToken);
                                _logger.LogInformation($"[SAGA] Pedido {orderId} REJEITADO processado com sucesso de forma segura.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[ERRO CRÍTICO SAGA]: {ex.Message}");
                }

                _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            };

            _channel.BasicConsume(queue: "order_rejected_queue", autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }
    }
}
