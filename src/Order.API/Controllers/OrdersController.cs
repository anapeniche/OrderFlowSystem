using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Order.API.Data;
using Order.API.Domain.Entities;
using System.Text.Json;

namespace Order.API.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderDbContext _context;

    public OrdersController(OrderDbContext context)
    {
        _context = context;
    }


    [HttpGet]
    public async Task<ActionResult<IEnumerable<Order.API.Domain.Entities.Order>>> GetOrders()
    {
        return await _context.Orders
            .Include(o => o.Items)
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<Order.API.Domain.Entities.Order>> PostOrder(Order.API.Domain.Entities.Order orderEntity)
    {
        if (orderEntity == null) return BadRequest();


        orderEntity.CreatedAt = DateTime.UtcNow;


        var outboxEvent = new OutboxEvent
        {
            Type = "OrderCreated",
            Payload = JsonSerializer.Serialize(orderEntity),
            CreatedAt = DateTime.UtcNow,
            Published = false
        };


        _context.Orders.Add(orderEntity);
        _context.OutboxEvents.Add(outboxEvent);

        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOrders), new { id = orderEntity.Id }, orderEntity);
    }
}