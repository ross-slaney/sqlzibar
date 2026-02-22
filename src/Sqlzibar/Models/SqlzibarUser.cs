namespace Sqlzibar.Models;

/// <summary>
/// Human user principal extension.
/// </summary>
public class SqlzibarUser
{
    public string Id { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public SqlzibarPrincipal? Principal { get; set; }
}
