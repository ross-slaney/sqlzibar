using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.IntegrationTests.Infrastructure;
using Sqlzibar.Services;

namespace Sqlzibar.IntegrationTests;

[TestClass]
public class PrincipalResolutionIntegrationTests : IntegrationTestBase
{
    private SqlzibarPrincipalService _principalService = null!;

    [TestInitialize]
    public void TestInit()
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _principalService = new SqlzibarPrincipalService(
            Context,
            loggerFactory.CreateLogger<SqlzibarPrincipalService>());
    }

    [TestMethod]
    public async Task ResolvePrincipalIds_UserWithGroups_ReturnsUserAndGroupPrincipals()
    {
        var ids = await _principalService.ResolvePrincipalIdsAsync(TestDataSeeder.GroupMemberPrincipalId);
        Assert.IsTrue(ids.Count >= 2);
        Assert.IsTrue(ids.Contains(TestDataSeeder.GroupMemberPrincipalId));
        Assert.IsTrue(ids.Contains(TestDataSeeder.TestGroupPrincipalId));
    }

    [TestMethod]
    public async Task ResolvePrincipalIds_UserWithoutGroups_ReturnsOnlyUser()
    {
        var ids = await _principalService.ResolvePrincipalIdsAsync(TestDataSeeder.UnauthorizedPrincipalId);
        Assert.AreEqual(1, ids.Count);
        Assert.AreEqual(TestDataSeeder.UnauthorizedPrincipalId, ids[0]);
    }

    [TestMethod]
    public async Task AddToGroup_GroupPrincipal_ThrowsInvalidOperation()
    {
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await _principalService.AddToGroupAsync(
                TestDataSeeder.TestGroupPrincipalId, TestDataSeeder.TestGroupId);
        });
    }

    [TestMethod]
    public async Task GetGroupsForPrincipal_ReturnsCorrectGroups()
    {
        var groups = await _principalService.GetGroupsForPrincipalAsync(TestDataSeeder.GroupMemberPrincipalId);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(TestDataSeeder.TestGroupId, groups[0].Id);
    }
}
