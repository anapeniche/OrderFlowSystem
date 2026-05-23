using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Order.API.Data;
using Order.API.Domain;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using RabbitMQ.Client;
using System;
using System.Linq;

namespace Order.API.Services
{
    public class OutboxPublisher : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConnection _connection;
        private readonly IModel _channel;

        public OutboxPublisher(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

             var factory = new ConnectionFactory()
{
    HostName = configuration["RabbitMQ:Host"] ?? "rabbitmq_broker",
    Port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672"),
    UserName = configuration["RabbitMQ:UserName"] ?? "guest",
    Password = configuration["RabbitMQ:Password"] ?? "guest"
};

            int retryCount = 0;
            while (retryCount < 10)
            {
                try
                {
                    _connection = factory.CreateConnection();
                    _channel = _connection.CreateModel();

                    _channel.QueueDeclare(
                        queue: "order-placed",
                        durable: false,
                        exclusive: false,
                        autoDelete: false,
                        arguments: null
                    );

                    Console.WriteLine("RabbitMQ connection established.");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Waiting for RabbitMQ... attempt {retryCount + 1}. Error: {ex.Message}");
                    Task.Delay(5000).Wait();
                    retryCount++;
                }
            }

            if (_connection == null || _channel == null)
                throw new Exception("rabbitmq_broker connection could not be established.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
                var body = System.Text.Encoding.UTF8.GetBytes(evt.Payload);

                _channel.BasicPublish(
                    exchange: "",
                    routingKey: "order-placed",
                    basicProperties: null,
                    body: body
                );

                evt.Published = true;
            }

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
