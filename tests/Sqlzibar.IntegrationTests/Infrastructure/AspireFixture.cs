using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.Configuration;
using Sqlzibar.Services;

namespace Sqlzibar.IntegrationTests.Infrastructure;

[TestClass]
public static class AspireFixture
{
    private static DistributedApplication? _app;

    public static string SqlConnectionString { get; private set; } = string.Empty;
    public static bool IsInitialized { get; private set; }
    public static TestSqlzibarDbContext? SharedContext { get; private set; }

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext context)
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Sqlzibar_IntegrationTests_AppHost>();

        _app = await appHost.BuildAsync();
        await _app.StartAsync();

        // Wait for SQL Server to be healthy
        using var sqlCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("sql", sqlCts.Token);

        SqlConnectionString = await _app.GetConnectionStringAsync("sqlzibar-test")
            ?? throw new InvalidOperationException("Could not get SQL connection string from Aspire");

        IsInitialized = true;
        context.WriteLine("Aspire infrastructure initialized. SQL connection available.");

        // Create and seed the shared test database
        var dbName = $"Test_{Guid.NewGuid():N}"[..30];
        var connectionString = SqlConnectionString
            .Replace("Database=sqlzibar-test", $"Database={dbName}");

        var options = new DbContextOptionsBuilder<TestSqlzibarDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        SharedContext = new TestSqlzibarDbContext(options);

        // EnsureCreated creates the DB (Sqlzibar tables are excluded from EF via
        // ExcludeFromMigrations, so they are created by SchemaInitializer below)
        await SharedContext.Database.EnsureCreatedAsync();

        var sqlzibarOptions = Options.Create(new SqlzibarOptions());
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

        // Create Sqlzibar tables via raw SQL (matches UseSqlzibarAsync() pattern)
        var schemaInitializer = new SqlzibarSchemaInitializer(
            SharedContext,
            sqlzibarOptions,
            loggerFactory.CreateLogger<SqlzibarSchemaInitializer>());
        await schemaInitializer.EnsureSchemaAsync();

        var initializer = new SqlzibarFunctionInitializer(
            SharedContext,
            sqlzibarOptions,
            loggerFactory.CreateLogger<SqlzibarFunctionInitializer>());
        await initializer.EnsureFunctionsExistAsync();

        var seeder = new SqlzibarSeedService(
            SharedContext,
            sqlzibarOptions,
            loggerFactory.CreateLogger<SqlzibarSeedService>());
        await seeder.SeedCoreAsync();

        await TestDataSeeder.SeedAsync(SharedContext);

        context.WriteLine($"Test database '{dbName}' initialized.");
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (SharedContext != null)
        {
            await SharedContext.Database.EnsureDeletedAsync();
            await SharedContext.DisposeAsync();
        }

        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
