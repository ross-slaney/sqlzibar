using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SqlOS.AuthServer.Contracts;
using SqlOS.AuthServer.Extensions;

namespace SqlOS.Mcp;

internal sealed class SqlOSMcpUserContext : ISqlOSMcpUserContext
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private readonly SqlOSValidatedToken? _token;

    public SqlOSMcpUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _token = httpContextAccessor.HttpContext?.GetSqlOSValidatedToken();
        Scopes = ParseScopes(_token?.Scope);
    }

    public bool IsAuthenticated => _token != null;

    public string? UserId => _token?.UserId;

    public string? OrganizationId => _token?.OrganizationId;

    public string? ClientId => _token?.ClientId;

    public string? SessionId => _token?.SessionId;

    public string? Audience => _token?.Audience;

    public IReadOnlyList<string> Scopes { get; }

    public ClaimsPrincipal Principal => _token?.Principal ?? Anonymous;

    public bool HasScope(string scope)
        => !string.IsNullOrWhiteSpace(scope)
           && Scopes.Contains(scope.Trim(), StringComparer.Ordinal);

    private static IReadOnlyList<string> ParseScopes(string? scope)
        => string.IsNullOrWhiteSpace(scope)
            ? []
            : scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}
