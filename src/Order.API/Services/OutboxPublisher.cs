using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Order.API.Data;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace Order.API.Services
{
    public class OutboxPublisher : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OutboxPublisher> _logger;
        private IConnection _connection;
        private IModel _channel;

        public OutboxPublisher(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<OutboxPublisher> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        private void InitRabbitMQ()
        {
            var factory = new ConnectionFactory()
            {
                HostName = _configuration["RabbitMQ__Host"] ?? "rabbitmq_broker",
                Port = int.Parse(_configuration["RabbitMQ__Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ__Username"] ?? "guest",
                Password = _configuration["RabbitMQ__Password"] ?? "guest"
            };

            while (true)
            {
                try
                {
                    _connection = factory.CreateConnection();
                    _channel = _connection.CreateModel();

                    _channel.QueueDeclare(
                        queue: "order_created_queue",
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: null
                    );

                    _logger.LogInformation("[OUTBOX] Conectado ao RabbitMQ com sucesso.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[OUTBOX] Aguardando RabbitMQ... {ex.Message}");
                    Thread.Sleep(3000);
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            InitRabbitMQ();

            while (!stoppingToken.IsCancellationRequested)
            {
                await PublishOutboxMessagesAsync();
                await Task.Delay(5000, stoppingToken);
            }
        }

        private async Task PublishOutboxMessagesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            var pendingEvents = await db.OutboxEvents
                .Where(e => !e.Published)
                .ToListAsync();

            foreach (var evt in pendingEvents)
            {
                try
                {
                    var body = Encoding.UTF8.GetBytes(evt.Payload);

                    _channel.BasicPublish(
                        exchange: "",
                        routingKey: "order_created_queue",
                        basicProperties: null,
                        body: body
                    );

                    evt.Published = true;
                    _logger.LogInformation($"[OUTBOX] Evento {evt.Id} publicado com sucesso.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[OUTBOX] Falha ao publicar evento {evt.Id}: {ex.Message}");
                }
            }

            if (pendingEvents.Any(e => e.Published))
                await db.SaveChangesAsync();
        }

        public override void Dispose()
        {
            _channel?.Close();
            _connection?.Close();
            base.Dispose();
        }
    }
}