using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.Example.Api.Data;

namespace Sqlzibar.Example.IntegrationTests.Infrastructure;

[TestClass]
public static class ExampleApiFixture
{
    private static DistributedApplication? _app;
    private static WebApplicationFactory<Program>? _factory;
    private static string _testDbName = string.Empty;
    private static string _connectionString = string.Empty;

    public static HttpClient Client { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        // Start Aspire AppHost to get SQL Server
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Sqlzibar_IntegrationTests_AppHost>();

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        using var sqlCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("sql", sqlCts.Token);

        var baseConnectionString = await _app.GetConnectionStringAsync("sqlzibar-test")
            ?? throw new InvalidOperationException("Could not get SQL connection string from Aspire");

        _testDbName = $"ExampleRetail_{Guid.NewGuid():N}"[..30];
        _connectionString = baseConnectionString
            .Replace("Database=sqlzibar-test", $"Database={_testDbName}");

        context.WriteLine($"Using test database: {_testDbName}");

        // Create WebApplicationFactory with overridden connection string
        // Use UseSetting to inject connection string before Program.cs reads configuration
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
            });

        Client = _factory.CreateClient();

        // Verify the app started and seeded correctly
        var response = await Client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();

        context.WriteLine("Example API integration test fixture initialized.");
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        Client?.Dispose();

        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }

        if (!string.IsNullOrEmpty(_connectionString))
        {
            try
            {
                var options = new DbContextOptionsBuilder<RetailDbContext>()
                    .UseSqlServer(_connectionString)
                    .Options;
                await using var ctx = new RetailDbContext(options);
                await ctx.Database.EnsureDeletedAsync();
            }
            catch { /* Best effort cleanup */ }
        }

        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
