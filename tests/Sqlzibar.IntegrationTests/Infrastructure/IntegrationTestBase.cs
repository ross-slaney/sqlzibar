using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.Configuration;
using Sqlzibar.Services;

namespace Sqlzibar.IntegrationTests.Infrastructure;

public abstract class IntegrationTestBase
{
    /// <summary>
    /// Shared context initialized once per assembly by AspireFixture.
    /// All test classes share the same database for performance and reliability.
    /// </summary>
    protected static TestSqlzibarDbContext Context => AspireFixture.SharedContext
        ?? throw new InvalidOperationException("Test database not initialized.");
}
