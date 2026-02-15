using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.Configuration;
using Sqlzibar.IntegrationTests.Infrastructure;
using Sqlzibar.Services;

namespace Sqlzibar.IntegrationTests;

[TestClass]
public class FunctionInitializerIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task EnsureFunctionsExist_Idempotent_CanRunMultipleTimes()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var initializer = new SqlzibarFunctionInitializer(
            Context,
            Options.Create(new SqlzibarOptions()),
            loggerFactory.CreateLogger<SqlzibarFunctionInitializer>());

        // Should not throw when run multiple times
        await initializer.EnsureFunctionsExistAsync();
        await initializer.EnsureFunctionsExistAsync();
    }
}
