using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Sqlzibar.Extensions;
using Sqlzibar.Interfaces;
using Sqlzibar.Models;
using Sqlzibar.Services;

namespace Sqlzibar.Tests;

public class TestInMemoryDbContext : DbContext, ISqlzibarDbContext
{
    public TestInMemoryDbContext(DbContextOptions<TestInMemoryDbContext> options) : base(options) { }

    public IQueryable<SqlzibarAccessibleResource> IsResourceAccessible(
        string resourceId, string principalIds, string permissionId)
        => throw new NotSupportedException("TVF not supported with InMemory provider");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Apply model config WITHOUT TVF registration (InMemory doesn't support it)
        Sqlzibar.Configuration.SqlzibarModelConfiguration.Configure(modelBuilder, new Sqlzibar.Configuration.SqlzibarOptions());
    }
}

[TestClass]
public class SqlzibarPrincipalServiceTests
{
    private TestInMemoryDbContext _context = null!;
    private SqlzibarPrincipalService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<TestInMemoryDbContext>()
            .UseInMemoryDatabase(databaseName: $"Test_{Guid.NewGuid()}")
            .Options;

        _context = new TestInMemoryDbContext(options);

        // Seed principal types
        _context.Set<SqlzibarPrincipalType>().AddRange(
            new SqlzibarPrincipalType { Id = "user", Name = "User" },
            new SqlzibarPrincipalType { Id = "group", Name = "Group" },
            new SqlzibarPrincipalType { Id = "service_account", Name = "Service Account" }
        );
        _context.SaveChanges();

        _service = new SqlzibarPrincipalService(
            _context,
            Mock.Of<ILogger<SqlzibarPrincipalService>>());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _context.Dispose();
    }

    [TestMethod]
    public async Task CreatePrincipal_CreatesWithGeneratedId()
    {
        var result = await _service.CreatePrincipalAsync("Test User", "user");
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Id.StartsWith("prin_"));
        Assert.AreEqual("Test User", result.DisplayName);
        Assert.AreEqual("user", result.PrincipalTypeId);
    }

    [TestMethod]
    public async Task CreateGroup_CreatesGroupAndPrincipal()
    {
        var group = await _service.CreateGroupAsync("Test Group", "A test group");
        Assert.IsNotNull(group);
        Assert.IsTrue(group.Id.StartsWith("grp_"));
        Assert.AreEqual("Test Group", group.Name);

        var principal = await _context.Set<SqlzibarPrincipal>()
            .FirstOrDefaultAsync(p => p.Id == group.PrincipalId);
        Assert.IsNotNull(principal);
        Assert.AreEqual("group", principal.PrincipalTypeId);
    }

    [TestMethod]
    public async Task AddToGroup_ValidUser_Succeeds()
    {
        var user = await _service.CreatePrincipalAsync("User", "user");
        var group = await _service.CreateGroupAsync("Group");

        await _service.AddToGroupAsync(user.Id, group.Id);

        var groups = await _service.GetGroupsForPrincipalAsync(user.Id);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(group.Id, groups[0].Id);
    }

    [TestMethod]
    public async Task AddToGroup_GroupPrincipal_ThrowsInvalidOperation()
    {
        var group1 = await _service.CreateGroupAsync("Group 1");
        var group2 = await _service.CreateGroupAsync("Group 2");

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
        {
            await _service.AddToGroupAsync(group1.PrincipalId, group2.Id);
        });
    }

    [TestMethod]
    public async Task AddToGroup_Idempotent_DoesNotDuplicate()
    {
        var user = await _service.CreatePrincipalAsync("User", "user");
        var group = await _service.CreateGroupAsync("Group");

        await _service.AddToGroupAsync(user.Id, group.Id);
        await _service.AddToGroupAsync(user.Id, group.Id); // second call

        var memberships = await _context.Set<SqlzibarUserGroupMembership>()
            .Where(m => m.PrincipalId == user.Id)
            .ToListAsync();
        Assert.AreEqual(1, memberships.Count);
    }

    [TestMethod]
    public async Task RemoveFromGroup_RemovesMembership()
    {
        var user = await _service.CreatePrincipalAsync("User", "user");
        var group = await _service.CreateGroupAsync("Group");

        await _service.AddToGroupAsync(user.Id, group.Id);
        await _service.RemoveFromGroupAsync(user.Id, group.Id);

        var groups = await _service.GetGroupsForPrincipalAsync(user.Id);
        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public async Task ResolvePrincipalIds_ReturnsUserAndGroupPrincipals()
    {
        var user = await _service.CreatePrincipalAsync("User", "user");
        var group = await _service.CreateGroupAsync("Group");

        await _service.AddToGroupAsync(user.Id, group.Id);

        var ids = await _service.ResolvePrincipalIdsAsync(user.Id);
        Assert.AreEqual(2, ids.Count);
        Assert.IsTrue(ids.Contains(user.Id));
        Assert.IsTrue(ids.Contains(group.PrincipalId));
    }
}
