using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using SqlOS.AuthServer.Configuration;

namespace SqlOS.AuthServer.Endpoints;

/// <summary>
/// Serves RFC 9728 OAuth protected-resource metadata for the API and MCP surfaces declared on the
/// single-application description. Nothing is mapped when no surface is declared.
/// </summary>
internal static class ProtectedResourceEndpoints
{
    public static void MapSqlOSProtectedResourceMetadata(
        this IEndpointRouteBuilder endpoints,
        SqlOSAuthServerOptions authOptions)
    {
        var surfaces = SqlOSSingleApplicationSurfaces.Describe(authOptions.SingleApplication);
        if (surfaces.Count == 0)
        {
            return;
        }

        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var surface in surfaces)
        {
            // The path advertised in WWW-Authenticate challenges for this surface.
            MapDocument(endpoints, surface.MetadataPath, surface, mapped);

            // RFC 9728 §3: a resource with a path component is also discoverable at
            // /.well-known/oauth-protected-resource{path}; serve it for the API too.
            MapDocument(endpoints, $"{SqlOSSingleApplicationSurfaces.ProtectedResourceWellKnownPath}{surface.Path}", surface, mapped);
        }

        // An MCP-only application still answers the well-known root so clients that fall back
        // from the path-suffixed document find the one resource this host protects.
        if (!mapped.Contains(SqlOSSingleApplicationSurfaces.ProtectedResourceWellKnownPath))
        {
            MapDocument(endpoints, SqlOSSingleApplicationSurfaces.ProtectedResourceWellKnownPath, surfaces[0], mapped);
        }
    }

    private static void MapDocument(
        IEndpointRouteBuilder endpoints,
        string path,
        SqlOSSingleApplicationSurface surface,
        HashSet<string> mapped)
    {
        if (!mapped.Add(path))
        {
            return;
        }

        endpoints.MapGet(path, (IOptions<SqlOSAuthServerOptions> options) =>
            {
                var authOptions = options.Value;
                var scopes = (authOptions.SingleApplication?.AllowedScopes ?? [])
                    .Where(static scope => !string.IsNullOrWhiteSpace(scope))
                    .Select(static scope => scope.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                return Results.Json(new SqlOSProtectedResourceMetadata(
                    surface.Audience,
                    [authOptions.Issuer.TrimEnd('/')],
                    scopes,
                    ["header"]));
            })
            .ExcludeFromDescription()
            .WithMetadata(new SqlOSProtectedResourceMetadataEndpoint(surface.Kind, surface.Path, surface.Audience));
    }
}

/// <summary>RFC 9728 protected-resource metadata document.</summary>
internal sealed record SqlOSProtectedResourceMetadata(
    [property: System.Text.Json.Serialization.JsonPropertyName("resource")] string Resource,
    [property: System.Text.Json.Serialization.JsonPropertyName("authorization_servers")] string[] AuthorizationServers,
    [property: System.Text.Json.Serialization.JsonPropertyName("scopes_supported")] string[] ScopesSupported,
    [property: System.Text.Json.Serialization.JsonPropertyName("bearer_methods_supported")] string[] BearerMethodsSupported);

/// <summary>Endpoint metadata identifying which declared surface a protected-resource document describes.</summary>
internal sealed record SqlOSProtectedResourceMetadataEndpoint(
    SqlOSSingleApplicationSurfaceKind Kind,
    string SurfacePath,
    string Resource);
