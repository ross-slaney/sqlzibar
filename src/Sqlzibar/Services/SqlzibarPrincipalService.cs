using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sqlzibar.Interfaces;
using Sqlzibar.Models;

namespace Sqlzibar.Services;

public class SqlzibarPrincipalService : ISqlzibarPrincipalService
{
    private readonly ISqlzibarDbContext _context;
    private readonly ILogger<SqlzibarPrincipalService> _logger;

    public SqlzibarPrincipalService(
        ISqlzibarDbContext context,
        ILogger<SqlzibarPrincipalService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<SqlzibarPrincipal> CreatePrincipalAsync(
        string displayName,
        string principalTypeId,
        string? organizationId = null,
        string? externalRef = null,
        CancellationToken cancellationToken = default)
    {
        var principal = new SqlzibarPrincipal
        {
            Id = $"prin_{Guid.NewGuid():N}"[..30],
            PrincipalTypeId = principalTypeId,
            DisplayName = displayName,
            OrganizationId = organizationId,
            ExternalRef = externalRef,
        };

        _context.Set<SqlzibarPrincipal>().Add(principal);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created principal {PrincipalId} ({DisplayName}) of type {Type}",
            principal.Id, displayName, principalTypeId);

        return principal;
    }

    public async Task<SqlzibarUserGroup> CreateGroupAsync(
        string name,
        string? description = null,
        string? groupType = null,
        CancellationToken cancellationToken = default)
    {
        var principal = await CreatePrincipalAsync(name, "group", cancellationToken: cancellationToken);

        var group = new SqlzibarUserGroup
        {
            Id = $"grp_{Guid.NewGuid():N}"[..30],
            Name = name,
            Description = description,
            GroupType = groupType,
            PrincipalId = principal.Id,
        };

        _context.Set<SqlzibarUserGroup>().Add(group);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created group {GroupId} ({Name})", group.Id, name);

        return group;
    }

    public async Task<SqlzibarUser> CreateUserAsync(
        string displayName,
        string? email = null,
        bool isActive = true,
        string? organizationId = null,
        string? externalRef = null,
        CancellationToken cancellationToken = default)
    {
        var principal = await CreatePrincipalAsync(displayName, "user", organizationId, externalRef, cancellationToken);

        var user = new SqlzibarUser
        {
            Id = $"usr_{Guid.NewGuid():N}"[..30],
            PrincipalId = principal.Id,
            Email = email,
            IsActive = isActive,
        };

        _context.Set<SqlzibarUser>().Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created user {UserId} ({DisplayName})", user.Id, displayName);

        return user;
    }

    public async Task<SqlzibarAgent> CreateAgentAsync(
        string displayName,
        string? agentType = null,
        string? description = null,
        string? organizationId = null,
        string? externalRef = null,
        CancellationToken cancellationToken = default)
    {
        var principal = await CreatePrincipalAsync(displayName, "agent", organizationId, externalRef, cancellationToken);

        var agent = new SqlzibarAgent
        {
            Id = $"agt_{Guid.NewGuid():N}"[..30],
            PrincipalId = principal.Id,
            AgentType = agentType,
            Description = description,
        };

        _context.Set<SqlzibarAgent>().Add(agent);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created agent {AgentId} ({DisplayName})", agent.Id, displayName);

        return agent;
    }

    public async Task<SqlzibarServiceAccount> CreateServiceAccountAsync(
        string displayName,
        string clientId,
        string clientSecretHash,
        string? description = null,
        DateTime? expiresAt = null,
        string? organizationId = null,
        string? externalRef = null,
        CancellationToken cancellationToken = default)
    {
        var principal = await CreatePrincipalAsync(displayName, "service_account", organizationId, externalRef, cancellationToken);

        var serviceAccount = new SqlzibarServiceAccount
        {
            Id = $"sa_{Guid.NewGuid():N}"[..30],
            PrincipalId = principal.Id,
            ClientId = clientId,
            ClientSecretHash = clientSecretHash,
            Description = description,
            ExpiresAt = expiresAt,
        };

        _context.Set<SqlzibarServiceAccount>().Add(serviceAccount);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created service account {ServiceAccountId} ({DisplayName})", serviceAccount.Id, displayName);

        return serviceAccount;
    }

    public async Task AddToGroupAsync(string principalId, string userGroupId, CancellationToken cancellationToken = default)
    {
        // Validate principal is NOT a group type (no nested groups allowed)
        var principal = await _context.Set<SqlzibarPrincipal>()
            .FirstOrDefaultAsync(p => p.Id == principalId, cancellationToken)
            ?? throw new InvalidOperationException($"Principal '{principalId}' not found");

        if (principal.PrincipalTypeId == "group")
        {
            throw new InvalidOperationException("Groups cannot be members of other groups");
        }

        var exists = await _context.Set<SqlzibarUserGroupMembership>()
            .AnyAsync(m => m.PrincipalId == principalId && m.UserGroupId == userGroupId, cancellationToken);

        if (exists) return;

        var membership = new SqlzibarUserGroupMembership
        {
            PrincipalId = principalId,
            UserGroupId = userGroupId,
        };

        _context.Set<SqlzibarUserGroupMembership>().Add(membership);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added principal {PrincipalId} to group {GroupId}", principalId, userGroupId);
    }

    public async Task RemoveFromGroupAsync(string principalId, string userGroupId, CancellationToken cancellationToken = default)
    {
        var membership = await _context.Set<SqlzibarUserGroupMembership>()
            .FirstOrDefaultAsync(m => m.PrincipalId == principalId && m.UserGroupId == userGroupId, cancellationToken);

        if (membership == null) return;

        _context.Set<SqlzibarUserGroupMembership>().Remove(membership);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Removed principal {PrincipalId} from group {GroupId}", principalId, userGroupId);
    }

    public async Task<List<string>> ResolvePrincipalIdsAsync(string principalId, CancellationToken cancellationToken = default)
    {
        var principals = new List<string> { principalId };

        var groupPrincipalIds = await _context.Set<SqlzibarUserGroupMembership>()
            .Where(m => m.PrincipalId == principalId)
            .Join(_context.Set<SqlzibarUserGroup>(),
                m => m.UserGroupId,
                g => g.Id,
                (m, g) => g.PrincipalId)
            .ToListAsync(cancellationToken);

        principals.AddRange(groupPrincipalIds);
        return principals;
    }

    public async Task<List<SqlzibarUserGroup>> GetGroupsForPrincipalAsync(string principalId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<SqlzibarUserGroupMembership>()
            .Where(m => m.PrincipalId == principalId)
            .Join(_context.Set<SqlzibarUserGroup>(),
                m => m.UserGroupId,
                g => g.Id,
                (m, g) => g)
            .ToListAsync(cancellationToken);
    }
}
