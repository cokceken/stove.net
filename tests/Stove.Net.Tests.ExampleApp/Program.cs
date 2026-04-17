using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Stove.Net.Tests.ExampleApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Kafka bootstrap servers from configuration (injected by Stove)
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new Confluent.Kafka.ProducerConfig
    {
        BootstrapServers = config["Kafka:BootstrapServers"] ?? "localhost:9092"
    };
});

// Register Redis connection lazily — config is resolved at service resolution time
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return ConnectionMultiplexer.Connect(config["Redis:ConnectionString"]!);
});

// Register named HttpClient for external notification service
builder.Services.AddHttpClient("NotificationService", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["ExternalApis:NotificationUrl"] ?? "http://localhost:9999";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();
await app.RunAsync();

public partial class Program;
