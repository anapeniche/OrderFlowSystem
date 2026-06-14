using System.Collections.Generic;
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
        private IConnection? _connection;
        private IModel? _channel;

        public StockResponseWorker(ILogger<StockResponseWorker> logger, IConfiguration configuration)
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
                    _channel.QueueDeclare(queue: "order_approved_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    _channel.QueueDeclare(queue: "order_rejected_queue", durable: true, exclusive: false, autoDelete: false, arguments: null);
                    _logger.LogInformation("[SAGA] Conectado ao RabbitMQ com sucesso!");
                    break;
                }
                catch
                {
                    _logger.LogWarning("[SAGA] Aguardando RabbitMQ...");
                    Thread.Sleep(2000);
                }
            }
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            InitRabbitMQ();
            stoppingToken.Register(() => _connection?.Close());

            // Consumer para pedidos APROVADOS
            var approvedConsumer = new EventingBasicConsumer(_channel);
            approvedConsumer.Received += async (model, ea) =>
            {
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                try
                {
                    using var doc = JsonDocument.Parse(message);
                    int orderId = doc.RootElement.GetProperty("OrderId").GetInt32();

                    if (orderId > 0)
                    {
                        var connectionString = _configuration.GetConnectionString("DefaultConnection")
                            ?? throw new InvalidOperationException("Connection string not found.");

                        using var conn = new NpgsqlConnection(connectionString);
                        await conn.OpenAsync(stoppingToken);
                        using var cmd = new NpgsqlCommand("UPDATE \"Orders\" SET \"Status\" = 1 WHERE \"Id\" = @id", conn);
                        cmd.Parameters.AddWithValue("id", orderId);
                        await cmd.ExecuteNonQueryAsync(stoppingToken);
                        _logger.LogInformation($"[SAGA] Pedido {orderId} APROVADO.");
                    }
                    _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
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
                        _logger.LogError($"[DLQ] Aprovação enviada para DLQ após {retryCount} tentativas: {ex.Message}");
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                    }
                    else
                    {
                        _logger.LogWarning($"[RETRY] Tentativa {retryCount + 1}/3: {ex.Message}");
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                    }
                }
            };

            // Consumer para pedidos REJEITADOS
            var rejectedConsumer = new EventingBasicConsumer(_channel);
            rejectedConsumer.Received += async (model, ea) =>
            {
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                try
                {
                    using var doc = JsonDocument.Parse(message);
                    int orderId = doc.RootElement.GetProperty("OrderId").GetInt32();

                    if (orderId > 0)
                    {
                        var connectionString = _configuration.GetConnectionString("DefaultConnection")
                            ?? throw new InvalidOperationException("Connection string not found.");

                        using var conn = new NpgsqlConnection(connectionString);
                        await conn.OpenAsync(stoppingToken);
                        using var cmd = new NpgsqlCommand("UPDATE \"Orders\" SET \"Status\" = 2 WHERE \"Id\" = @id", conn);
                        cmd.Parameters.AddWithValue("id", orderId);
                        await cmd.ExecuteNonQueryAsync(stoppingToken);
                        _logger.LogInformation($"[SAGA] Pedido {orderId} REJEITADO.");
                    }
                    _channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
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
                        _logger.LogError($"[DLQ] Rejeição enviada para DLQ após {retryCount} tentativas: {ex.Message}");
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
                    }
                    else
                    {
                        _logger.LogWarning($"[RETRY] Tentativa {retryCount + 1}/3: {ex.Message}");
                        _channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
                    }
                }
            };

            _channel.BasicConsume(queue: "order_approved_queue", autoAck: false, consumer: approvedConsumer);
            _channel.BasicConsume(queue: "order_rejected_queue", autoAck: false, consumer: rejectedConsumer);

            return Task.CompletedTask;
        }
    }
}
