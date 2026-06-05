using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Order.API.Controllers;
using Order.API.Data;
using Order.API.Domain.Entities;

namespace OrderFlow.IntegrationTests;

public class OrdersControllerTests
{
    private OrderDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new OrderDbContext(options);
    }

    [Fact]
    public async Task PostOrder_ShouldReturn201_WhenOrderIsValid()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var controller = new OrdersController(context);
        var order = new Order.API.Domain.Entities.Order
        {
            CustomerName = "Ana Peniche",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = 99, Quantity = 1, Price = 29.90m }
            }
        };

        // Act
        var result = await controller.PostOrder(order);

        // Assert
        var createdResult = result.Result as CreatedAtActionResult;
        createdResult.Should().NotBeNull();
        createdResult!.StatusCode.Should().Be(201);

        var returnedOrder = createdResult.Value as Order.API.Domain.Entities.Order;
        returnedOrder!.CustomerName.Should().Be("Ana Peniche");
        returnedOrder.Status.Should().Be(0);
    }

    [Fact]
    public async Task PostOrder_ShouldReturn400_WhenOrderIsNull()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var controller = new OrdersController(context);

        // Act
        var result = await controller.PostOrder(null!);

        // Assert
        result.Result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task PostOrder_ShouldPersistOrder_InDatabase()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var controller = new OrdersController(context);
        var order = new Order.API.Domain.Entities.Order
        {
            CustomerName = "Test Customer",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = 99, Quantity = 2, Price = 15.00m }
            }
        };

        // Act
        await controller.PostOrder(order);

        // Assert
        var savedOrder = await context.Orders.FirstOrDefaultAsync();
        savedOrder.Should().NotBeNull();
        savedOrder!.CustomerName.Should().Be("Test Customer");
        savedOrder.Status.Should().Be(0);
        savedOrder.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PostOrder_ShouldCreateOutboxEvent_WithCorrectPayload()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var controller = new OrdersController(context);
        var order = new Order.API.Domain.Entities.Order
        {
            CustomerName = "Outbox Test",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = 99, Quantity = 1, Price = 10.00m }
            }
        };

        // Act
        await controller.PostOrder(order);

        // Assert
        var outboxEvent = await context.OutboxEvents.FirstOrDefaultAsync();
        outboxEvent.Should().NotBeNull();
        outboxEvent!.Type.Should().Be("OrderCreated");
        outboxEvent.Published.Should().BeFalse();
        outboxEvent.Payload.Should().Contain("OrderId");
        outboxEvent.Payload.Should().Contain("ProductId");
        outboxEvent.Payload.Should().Contain("Quantity");
    }

    [Fact]
    public async Task PostOrder_ShouldSetCreatedAt_ToUtcNow()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var controller = new OrdersController(context);
        var before = DateTime.UtcNow;
        var order = new Order.API.Domain.Entities.Order
        {
            CustomerName = "Timestamp Test",
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = 1, Quantity = 1, Price = 5.00m }
            }
        };

        // Act
        await controller.PostOrder(order);
        var after = DateTime.UtcNow;

        // Assert
        var savedOrder = await context.Orders.FirstOrDefaultAsync();
        savedOrder!.CreatedAt.Should().BeOnOrAfter(before);
        savedOrder.CreatedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task GetOrders_ShouldReturnAllOrders()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        context.Orders.AddRange(
            new Order.API.Domain.Entities.Order
            {
                CustomerName = "Customer 1",
                CreatedAt = DateTime.UtcNow,
                Items = new List<OrderItem>()
            },
            new Order.API.Domain.Entities.Order
            {
                CustomerName = "Customer 2",
                CreatedAt = DateTime.UtcNow,
                Items = new List<OrderItem>()
            }
        );
        await context.SaveChangesAsync();
        var controller = new OrdersController(context);

        // Act
        var result = await controller.GetOrders();

        // Assert
        var okResult = result.Value;
        okResult.Should().NotBeNull();
        okResult!.Count().Should().Be(2);
    }
}
