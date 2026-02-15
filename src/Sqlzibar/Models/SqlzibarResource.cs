namespace Sqlzibar.Models;

/// <summary>
/// A resource in the hierarchy. Grants on parent resources cascade to all descendants.
/// </summary>
public class SqlzibarResource
{
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ResourceTypeId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public SqlzibarResource? Parent { get; set; }
    public ICollection<SqlzibarResource> Children { get; set; } = new List<SqlzibarResource>();
    public SqlzibarResourceType? ResourceType { get; set; }
    public ICollection<SqlzibarGrant> Grants { get; set; } = new List<SqlzibarGrant>();
}
