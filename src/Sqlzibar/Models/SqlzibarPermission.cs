namespace Sqlzibar.Models;

/// <summary>
/// A permission that can be granted. Permissions are capabilities that gate
/// whether a principal can access a feature/endpoint.
/// </summary>
public class SqlzibarPermission
{
    public string Id { get; set; } = string.Empty;
    public string? ResourceTypeId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation
    public SqlzibarResourceType? ResourceType { get; set; }
    public ICollection<SqlzibarRolePermission> RolePermissions { get; set; } = new List<SqlzibarRolePermission>();
}
