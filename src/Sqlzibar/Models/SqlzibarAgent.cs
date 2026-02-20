namespace Sqlzibar.Models;

/// <summary>
/// Automated agent principal (job, worker, AI).
/// </summary>
public class SqlzibarAgent
{
    public string Id { get; set; } = string.Empty;
    public string PrincipalId { get; set; } = string.Empty;
    public string? AgentType { get; set; }
    public string? Description { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public SqlzibarPrincipal? Principal { get; set; }
}
