namespace Sqlzibar.Models;

/// <summary>
/// Type of resource in the hierarchy (e.g., root, agency, team, project).
/// </summary>
public class SqlzibarResourceType
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation
    public ICollection<SqlzibarResource> Resources { get; set; } = new List<SqlzibarResource>();
    public ICollection<SqlzibarPermission> Permissions { get; set; } = new List<SqlzibarPermission>();
}
