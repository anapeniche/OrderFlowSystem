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

namespace Stock.API
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private IConnection _connection;
        private IModel _channel;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
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
                    
                    _channel.QueueDeclare(queue: "order_created_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    _channel.QueueDeclare(queue: "order_approved_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    _channel.QueueDeclare(queue: "order_rejected_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    
                    _logger.LogInformation("[ESTOQUE] Conectado ao RabbitMQ com sucesso!");
                    break;
                }
                catch
                {
                    _logger.LogWarning("[ESTOQUE] RabbitMQ ainda não está pronto. Aguardando 3 segundos...");
                    Thread.Sleep(3000);
                }
            }
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            InitRabbitMQ();
            stoppingToken.Register(() => _connection?.Close());

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += async (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                _logger.LogInformation($"[ESTOQUE] Mensagem recebida: {message}");

                try
                {
                    using var doc = JsonDocument.Parse(message);
                    int orderId = doc.RootElement.GetProperty("OrderId").GetInt32();
                    var firstItem = doc.RootElement.GetProperty("Items")[0];
                    int productId = doc.RootElement.GetProperty("ProductId").GetInt32();
                    int quantity = doc.RootElement.GetProperty("Quantity").GetInt32();

                    var connectionString = _configuration.GetConnectionString("DefaultConnection")
                        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

                    using (var conn = new NpgsqlConnection(connectionString))
                    {
                        await conn.OpenAsync(stoppingToken);

                        string checkSql = "SELECT \"Quantity\" FROM \"ProductStocks\" WHERE \"ProductId\" = @id";
                        int currentQuantity = 0;

                        using (var checkCmd = new NpgsqlCommand(checkSql, conn))
                        {
                            checkCmd.Parameters.AddWithValue("id", productId);
                            var result = await checkCmd.ExecuteScalarAsync(stoppingToken);
                            if (result != null) currentQuantity = Convert.ToInt32(result);
                        }

                        if (currentQuantity >= quantity)
                        {
                            string updateSql = "UPDATE \"ProductStocks\" SET \"Quantity\" = \"Quantity\" - @qty WHERE \"ProductId\" = @id";
                            using (var updateCmd = new NpgsqlCommand(updateSql, conn))
                            {
                                updateCmd.Parameters.AddWithValue("qty", quantity);
                                updateCmd.Parameters.AddWithValue("id", productId);
                                await updateCmd.ExecuteNonQueryAsync(stoppingToken);
                            }

                            _logger.LogInformation($"[ESTOQUE] Sucesso: Estoque reduzido para o produto {productId}.");

                            var approvedPayload = JsonSerializer.Serialize(new { OrderId = orderId });
                            var approvedBody = Encoding.UTF8.GetBytes(approvedPayload);
                            

                            _channel.BasicPublish(exchange: "", routingKey: "order_approved_queue", basicProperties: null, body: approvedBody);
                        }
                        else
                        {
                            _logger.LogWarning($"[ESTOQUE] Falha: Saldo insuficiente para o produto {productId}.");

                            var rejectedPayload = JsonSerializer.Serialize(new { OrderId = orderId, Reason = "Estoque insuficiente" });
                            var rejectedBody = Encoding.UTF8.GetBytes(rejectedPayload);
                            

                            _channel.BasicPublish(exchange: "", routingKey: "order_rejected_queue", basicProperties: null, body: rejectedBody);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[ERRO CRÍTICO ESTOQUE]: {ex.Message}");
                    _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                    return;
                }

                _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            };

            _channel.BasicConsume(queue: "order_created_queue", autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }
    }
}
