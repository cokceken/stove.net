using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Stove.Net.Tests.ExampleApp;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ProducerConfig _kafkaConfig;

    public OrdersController(AppDbContext db, ProducerConfig kafkaConfig)
    {
        _db = db;
        _kafkaConfig = kafkaConfig;
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var orders = await _db.Orders.ToListAsync();
        return Ok(orders);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var order = new Order
        {
            ProductName = request.ProductName,
            Quantity = request.Quantity,
            Status = "Confirmed"
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // Publish event to Kafka
        try
        {
            using var producer = new ProducerBuilder<string?, string>(_kafkaConfig).Build();
            var @event = new OrderCreatedEvent(order.Id, order.ProductName, order.Quantity, order.Status);
            var value = System.Text.Json.JsonSerializer.Serialize(@event);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await producer.ProduceAsync("order-events",
                new Message<string?, string> { Key = order.Id.ToString(), Value = value }, cts.Token);
            producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Kafka publish failure shouldn't break order creation
        }

        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order is null) return NotFound();

        _db.Orders.Remove(order);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
