using Sqlzibar.Models;

namespace Sqlzibar.Interfaces;

/// <summary>
/// Service for managing principals, groups, and group membership.
/// </summary>
public interface ISqlzibarPrincipalService
{
    /// <summary>
    /// Create a new principal.
    /// </summary>
    Task<SqlzibarPrincipal> CreatePrincipalAsync(
        string displayName,
        string principalTypeId,
        string? organizationId = null,
        string? externalRef = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new user group with its associated principal.
    /// </summary>
    Task<SqlzibarUserGroup> CreateGroupAsync(
        string name,
        string? description = null,
        string? groupType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a principal to a group.
    /// Only principals of type 'user' or 'service_account' can be added — groups cannot contain other groups.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the principal is a group type.</exception>
    Task AddToGroupAsync(string principalId, string userGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a principal from a group.
    /// </summary>
    Task RemoveFromGroupAsync(string principalId, string userGroupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve all principal IDs for a given principal (the principal itself plus any groups it belongs to).
    /// Single-level lookup — no recursion needed since groups can't contain groups.
    /// </summary>
    Task<List<string>> ResolvePrincipalIdsAsync(string principalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all groups a principal belongs to.
    /// </summary>
    Task<List<SqlzibarUserGroup>> GetGroupsForPrincipalAsync(string principalId, CancellationToken cancellationToken = default);
}
