namespace Sqlzibar.Models;

/// <summary>
/// A role that groups permissions.
/// </summary>
public class SqlzibarRole
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsVirtual { get; set; } = false;

    // Navigation
    public ICollection<SqlzibarRolePermission> RolePermissions { get; set; } = new List<SqlzibarRolePermission>();
    public ICollection<SqlzibarGrant> Grants { get; set; } = new List<SqlzibarGrant>();
}
