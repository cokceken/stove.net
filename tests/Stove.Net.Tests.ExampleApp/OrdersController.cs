using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Stove.Net.Tests.ExampleApp;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;

    public OrdersController(AppDbContext db) => _db = db;

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
