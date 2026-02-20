using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.Configuration;
using Sqlzibar.IntegrationTests.Infrastructure;
using Sqlzibar.Models;
using Sqlzibar.Services;

namespace Sqlzibar.IntegrationTests;

[TestClass]
public class PrincipalTypesIntegrationTests : IntegrationTestBase
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
    public async Task User_CanBeCreatedAndQueried()
    {
        var user = await _principalService.CreateUserAsync("Integration Test User", "integration@test.com");
        Assert.IsNotNull(user);
        Assert.IsTrue(user.Id.StartsWith("usr_"));
        Assert.AreEqual("integration@test.com", user.Email);

        var principal = await Context.Set<SqlzibarPrincipal>()
            .FirstOrDefaultAsync(p => p.Id == user.PrincipalId);
        Assert.IsNotNull(principal);
        Assert.AreEqual("user", principal.PrincipalTypeId);
    }

    [TestMethod]
    public async Task Agent_CanBeAddedToGroup()
    {
        var agent = await _principalService.CreateAgentAsync("New Agent", "worker");
        var group = await _principalService.CreateGroupAsync("New Group");

        await _principalService.AddToGroupAsync(agent.PrincipalId, group.Id);

        var groups = await _principalService.GetGroupsForPrincipalAsync(agent.PrincipalId);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(group.Id, groups[0].Id);
    }

    [TestMethod]
    public async Task Agent_InheritsGrantsViaGroup()
    {
        // TestAgent is in TestGroup (seeded). TestGroup has AgencyMemberRole at TestAgencyResourceId.
        // AgencyMember has TEST_VIEW permission. So agent should have TEST_VIEW on TestTeamResourceId.
        var authService = new SqlzibarAuthService(
            Context,
            Options.Create(new SqlzibarOptions()),
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<SqlzibarAuthService>());

        var result = await authService.CheckAccessAsync(
            TestDataSeeder.TestAgentPrincipalId, "TEST_VIEW", TestDataSeeder.TestTeamResourceId);
        Assert.IsTrue(result.Allowed, "Agent should inherit TEST_VIEW from group membership");
    }

    [TestMethod]
    public async Task ServiceAccount_CanBeCreatedWithCredentials()
    {
        var sa = await _principalService.CreateServiceAccountAsync(
            "Integration SA", "int_client", "int_hash", "Integration test service account");
        Assert.IsNotNull(sa);
        Assert.IsTrue(sa.Id.StartsWith("sa_"));
        Assert.AreEqual("int_client", sa.ClientId);
        Assert.AreEqual("int_hash", sa.ClientSecretHash);

        var principal = await Context.Set<SqlzibarPrincipal>()
            .FirstOrDefaultAsync(p => p.Id == sa.PrincipalId);
        Assert.IsNotNull(principal);
        Assert.AreEqual("service_account", principal.PrincipalTypeId);
    }

    [TestMethod]
    public async Task AllPrincipalTypes_ResolveCorrectly()
    {
        var userIds = await _principalService.ResolvePrincipalIdsAsync(TestDataSeeder.TestUserPrincipalId);
        Assert.AreEqual(1, userIds.Count);
        Assert.AreEqual(TestDataSeeder.TestUserPrincipalId, userIds[0]);

        var agentIds = await _principalService.ResolvePrincipalIdsAsync(TestDataSeeder.TestAgentPrincipalId);
        Assert.IsTrue(agentIds.Count >= 2);
        Assert.IsTrue(agentIds.Contains(TestDataSeeder.TestAgentPrincipalId));
        Assert.IsTrue(agentIds.Contains(TestDataSeeder.TestGroupPrincipalId));

        var saIds = await _principalService.ResolvePrincipalIdsAsync(TestDataSeeder.TestServiceAccountPrincipalId);
        Assert.AreEqual(1, saIds.Count);
        Assert.AreEqual(TestDataSeeder.TestServiceAccountPrincipalId, saIds[0]);
    }
}
