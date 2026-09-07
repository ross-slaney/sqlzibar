using System.Security.Claims;

namespace SqlOS.Mcp;

/// <summary>
/// The SqlOS identity behind the current MCP request, resolved from the access token SqlOS already
/// validated for the declared MCP surface. Inject it into tools (via the request-scoped
/// <see cref="IServiceProvider"/>) to act as the connecting user. It never exposes the raw token.
/// </summary>
public interface ISqlOSMcpUserContext
{
    /// <summary>Gets whether the current request carried a validated SqlOS access token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Gets the SqlOS user ID (<c>sub</c>) when the token belongs to a user.</summary>
    string? UserId { get; }

    /// <summary>Gets the active organization ID when the token carries one.</summary>
    string? OrganizationId { get; }

    /// <summary>Gets the OAuth client that obtained the token, for example a CIMD-registered MCP client.</summary>
    string? ClientId { get; }

    /// <summary>Gets the SqlOS session ID the token was issued under.</summary>
    string? SessionId { get; }

    /// <summary>Gets the validated token audience (the MCP surface identifier).</summary>
    string? Audience { get; }

    /// <summary>Gets the scopes granted to the token.</summary>
    IReadOnlyList<string> Scopes { get; }

    /// <summary>Gets the claims principal built from the validated token, for FGA checks and claim lookups.</summary>
    ClaimsPrincipal Principal { get; }

    /// <summary>Returns whether <paramref name="scope"/> was granted to the token.</summary>
    bool HasScope(string scope);
}
