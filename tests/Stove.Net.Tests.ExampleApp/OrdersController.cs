using System.Text.Json;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Stove.Net.Tests.ExampleApp;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ProducerConfig _kafkaConfig;
    private readonly IDatabase? _redis;
    private readonly IHttpClientFactory _httpClientFactory;

    public OrdersController(
        AppDbContext db,
        ProducerConfig kafkaConfig,
        IHttpClientFactory httpClientFactory,
        IConnectionMultiplexer? redis = null)
    {
        _db = db;
        _kafkaConfig = kafkaConfig;
        _httpClientFactory = httpClientFactory;
        _redis = redis?.GetDatabase();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        // Try Redis cache first
        if (_redis != null)
        {
            var cached = await _redis.StringGetAsync($"order:{id}");
            if (cached.HasValue)
            {
                var cachedOrder = JsonSerializer.Deserialize<Order>(cached.ToString());
                return Ok(cachedOrder);
            }
        }

        var order = await _db.Orders.FindAsync(id);
        if (order is null) return NotFound();

        // Cache the result
        if (_redis != null)
        {
            var json = JsonSerializer.Serialize(order);
            await _redis.StringSetAsync($"order:{order.Id}", json);
            await _redis.KeyExpireAsync($"order:{order.Id}", TimeSpan.FromMinutes(5));
        }

        return Ok(order);
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

        // Cache in Redis
        if (_redis != null)
        {
            var json = JsonSerializer.Serialize(order);
            await _redis.StringSetAsync($"order:{order.Id}", json);
            await _redis.KeyExpireAsync($"order:{order.Id}", TimeSpan.FromMinutes(5));
        }

        // Notify external service
        try
        {
            var client = _httpClientFactory.CreateClient("NotificationService");
            var notification = new { OrderId = order.Id, order.ProductName, order.Quantity };
            await client.PostAsJsonAsync("/api/notifications", notification);
        }
        catch
        {
            // Notification failure shouldn't break order creation
        }

        // Publish event to Kafka
        try
        {
            using var producer = new ProducerBuilder<string?, string>(_kafkaConfig).Build();
            var @event = new OrderCreatedEvent(order.Id, order.ProductName, order.Quantity, order.Status);
            var value = JsonSerializer.Serialize(@event);
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

        // Remove from cache
        if (_redis != null)
            await _redis.KeyDeleteAsync($"order:{id}");

        return NoContent();
    }
}
