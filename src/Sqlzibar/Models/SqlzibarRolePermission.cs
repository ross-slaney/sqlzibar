namespace Sqlzibar.Models;

/// <summary>
/// Junction table linking roles to permissions.
/// </summary>
public class SqlzibarRolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public string PermissionId { get; set; } = string.Empty;

    // Navigation
    public SqlzibarRole? Role { get; set; }
    public SqlzibarPermission? Permission { get; set; }
}
