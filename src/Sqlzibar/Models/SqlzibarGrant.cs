namespace Sqlzibar.Models;

/// <summary>
/// A grant assigns a role to a principal for a specific resource.
/// </summary>
public class SqlzibarGrant
{
    public string Id { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public SqlzibarPrincipal? Principal { get; set; }
    public SqlzibarResource? Resource { get; set; }
    public SqlzibarRole? Role { get; set; }
}
