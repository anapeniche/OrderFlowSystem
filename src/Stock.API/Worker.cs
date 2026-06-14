using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Npgsql;
using Stock.API.Data;
using Stock.API.Domain;

namespace Stock.API
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private IConnection? _connection;
        private IModel? _channel;

        public Worker(ILogger<Worker> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
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

                    _channel.ExchangeDeclare(exchange: "dlx", type: "direct", durable: true);

                    _channel.QueueDeclare(queue: "order_created_queue.dlq", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    _channel.QueueBind("order_created_queue.dlq", "dlx", "order_created_queue");

                    var queueArgs = new Dictionary<string, object>
                    {
                        { "x-dead-letter-exchange", "dlx" },
                        { "x-dead-letter-routing-key", "order_created_queue" }
                    };

                    _channel.QueueDeclare(queue: "order_created_queue", durable: true, exclusive: false, autoDelete: false, arguments: queueArgs);
                    _channel.QueueDeclare(queue: "order_approved_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    _channel.QueueDeclare(queue: "order_rejected_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

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
                    // Idempotência
                    var messageId = ea.BasicProperties.MessageId
                        ?? $"{ea.DeliveryTag}-{ea.RoutingKey}";

                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<StockDbContext>();

                    var alreadyProcessed = db.ProcessedMessages
                        .Any(m => m.MessageId == messageId);

                    if (alreadyProcessed)
                    {
                        _logger.LogWarning($"[IDEMPOTÊNCIA] Mensagem {messageId} já processada. Ignorando.");
                        _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
                        return;
                    }

                    // Processa a mensagem
                    using var doc = JsonDocument.Parse(message);
                    int orderId = doc.RootElement.GetProperty("OrderId").GetInt32();
                    int productId = doc.RootElement.GetProperty("ProductId").GetInt32();
                    int quantity = doc.RootElement.GetProperty("Quantity").GetInt32();

                    var connectionString = _configuration.GetConnectionString("DefaultConnection")
                        ?? throw new InvalidOperationException("Connection string not found.");

                    using var conn = new NpgsqlConnection(connectionString);
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

                        // Registra como processada
                        db.ProcessedMessages.Add(new ProcessedMessage
                        {
                            MessageId = messageId,
                            ProcessedAt = DateTime.UtcNow
                        });
                        await db.SaveChangesAsync();

                        _logger.LogInformation($"[ESTOQUE] Sucesso: Estoque reduzido para o produto {productId}.");

                        var approvedPayload = JsonSerializer.Serialize(new { OrderId = orderId });
                        _channel.BasicPublish(exchange: "", routingKey: "order_approved_queue", basicProperties: null, body: Encoding.UTF8.GetBytes(approvedPayload));
                    }
                    else
                    {
                        _logger.LogWarning($"[ESTOQUE] Falha: Saldo insuficiente para o produto {productId}.");

                        var rejectedPayload = JsonSerializer.Serialize(new { OrderId = orderId, Reason = "Estoque insuficiente" });
                        _channel.BasicPublish(exchange: "", routingKey: "order_rejected_queue", basicProperties: null, body: Encoding.UTF8.GetBytes(rejectedPayload));
                    }
                }
                catch (Exception ex)
                {
                    var retryCount = 0;
                    if (ea.BasicProperties.Headers != null &&
                        ea.BasicProperties.Headers.TryGetValue("x-death", out var xDeath) &&
                        xDeath is List<object> deaths && deaths.Count > 0)
                    {
                        var death = deaths[0] as Dictionary<string, object?>;
                        if (death != null && death.TryGetValue("count", out var count))
                            retryCount = Convert.ToInt32(count);
                    }

                    if (retryCount >= 3)
                    {
                        _logger.LogError($"[DLQ] Mensagem enviada para DLQ após {retryCount} tentativas: {ex.Message}");
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                    }
                    else
                    {
                        _logger.LogWarning($"[RETRY] Tentativa {retryCount + 1}/3: {ex.Message}");
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                    }
                    return;
                }

                _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            };

            _channel.BasicConsume(queue: "order_created_queue", autoAck: false, consumer: consumer);
            return Task.CompletedTask;
        }
    }
}
