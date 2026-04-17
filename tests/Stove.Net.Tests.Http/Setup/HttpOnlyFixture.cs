using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stove.Net.Core;
using Stove.Net.Http;
using Stove.Net.Tests.ExampleApp;
using Stove.Net.Xunit;

namespace Stove.Net.Tests.Http.Setup;

/// <summary>
/// Fixture that boots the ExampleApp with only the HTTP system.
/// PostgreSQL is replaced with an in-memory database so no container is needed.
/// </summary>
public class HttpOnlyFixture : StoveFixture<Program>
{
    protected override StoveBuilder Configure(StoveBuilder builder)
    {
        return builder.WithHttpClient();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove all EF Core / Npgsql registrations for AppDbContext
            var descriptorsToRemove = services
                .Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                .ToList();

            foreach (var d in descriptorsToRemove)
                services.Remove(d);

            // Replace with in-memory database
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseInMemoryDatabase("StoveHttpTests"));
        });
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        Stove.GetSystem<HttpClientSystem>().SetHttpClient(CreateClient());

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
