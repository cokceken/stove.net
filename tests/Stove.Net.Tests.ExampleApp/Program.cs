using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Stove.Net.Tests.ExampleApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// OpenTelemetry tracing — always register instrumentation sources.
// The OTLP exporter endpoint is read from IConfiguration at DI time via
// IConfigureOptions so it works with WebApplicationFactory.ConfigureAppConfiguration.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ExampleApp"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
        Npgsql.TracerProviderBuilderExtensions.AddNpgsql(tracing);
    });

// Override the OTLP exporter endpoint at DI time (after WebApplicationFactory's
// ConfigureAppConfiguration has run). This uses IConfigureOptions to late-bind
// the endpoint from IConfiguration.
builder.Services.AddOptions<OpenTelemetry.Exporter.OtlpExporterOptions>()
    .Configure<IConfiguration>((opts, config) =>
    {
        var endpoint = config["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrEmpty(endpoint))
        {
            opts.Endpoint = new Uri(endpoint);
            opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        }
    });

// Register Kafka bootstrap servers from configuration (injected by Stove)
// builder.Services.AddSingleton(sp =>
// {
//     var config = sp.GetRequiredService<IConfiguration>();
//     return new Confluent.Kafka.ProducerConfig
//     {
//         BootstrapServers = config["Kafka:BootstrapServers"] ?? "localhost:9092"
//     };
// });

// // Register Redis connection lazily — config is resolved at service resolution time
// builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
// {
//     var config = sp.GetRequiredService<IConfiguration>();
//     return ConnectionMultiplexer.Connect(config["Redis:ConnectionString"]!);
// });

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
