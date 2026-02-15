namespace Sqlzibar.Models;

/// <summary>
/// Junction table for principal-to-group membership.
/// Only principals of type 'user' or 'service_account' can be members — no nested groups.
/// </summary>
public class SqlzibarUserGroupMembership
{
    public string PrincipalId { get; set; } = string.Empty;
    public string UserGroupId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public SqlzibarPrincipal? Principal { get; set; }
    public SqlzibarUserGroup? UserGroup { get; set; }
}
