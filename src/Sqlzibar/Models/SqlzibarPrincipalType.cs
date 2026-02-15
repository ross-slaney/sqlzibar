namespace Sqlzibar.Models;

/// <summary>
/// Defines the type of principal (user, service_account, group).
/// </summary>
public class SqlzibarPrincipalType
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation
    public ICollection<SqlzibarPrincipal> Principals { get; set; } = new List<SqlzibarPrincipal>();
}
