using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Stove.Net.Tests.ExampleApp;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;

    // private readonly ProducerConfig _kafkaConfig;
    // private readonly IDatabase? _redis;
    private readonly IHttpClientFactory _httpClientFactory;

    public OrdersController(
        AppDbContext db,
        IHttpClientFactory httpClientFactory)
    {
        _db = db;
        // _kafkaConfig = kafkaConfig;
        _httpClientFactory = httpClientFactory;
        // _redis = redis?.GetDatabase();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var order = await _db.Orders.FindAsync(id);
        if (order is null) return NotFound();

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