using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SqlOS.AuthServer.Configuration;
using SqlOS.AuthServer.Extensions;
using SqlOS.AuthServer.Services;
using SqlOS.Configuration;

namespace SqlOS.Hosting;

/// <summary>
/// Guards the API/MCP surfaces declared on the application description. <c>AddSqlOS</c> installs it
/// ahead of the application's pipeline, so every request under a declared prefix carries a validated
/// bearer token for that surface's audience before any application middleware, branch, or endpoint
/// runs. Application code places nothing.
/// </summary>
internal sealed class SqlOSSurfaceProtectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<(string Path, SqlOSAccessTokenValidationMiddleware Validator)> _surfaces;

    public SqlOSSurfaceProtectionMiddleware(RequestDelegate next, IOptions<SqlOSOptions> options)
    {
        _next = next;
        _surfaces = SqlOSSingleApplicationSurfaces.Describe(options.Value.AuthServer.Application)
            .Select(surface => (surface.Path, new SqlOSAccessTokenValidationMiddleware(next,
                new SqlOSAccessTokenValidationOptions
                {
                    ExpectedAudience = surface.Audience,
                    Realm = surface.Realm,
                    ResourceMetadataUrl = surface.MetadataUrl
                })))
            .ToArray();
    }

    public Task InvokeAsync(HttpContext context, SqlOSAuthService authService)
    {
        var path = context.Request.Path;
        foreach (var (surfacePath, validator) in _surfaces)
        {
            if (!SqlOSSingleApplicationSurfaces.Matches(path, surfacePath))
            {
                continue;
            }

            // A CORS preflight carries no credentials, so it cannot be authenticated. When the host
            // registered CORS, let its middleware answer the preflight; the actual request that
            // follows is validated like any other. Without CORS services it is an ordinary OPTIONS
            // request and is challenged.
            if (IsCorsPreflight(context.Request) && context.RequestServices.GetService<ICorsService>() != null)
            {
                return _next(context);
            }

            return validator.InvokeAsync(context, authService);
        }

        return _next(context);
    }

    private static bool IsCorsPreflight(HttpRequest request)
        => HttpMethods.IsOptions(request.Method)
           && request.Headers.ContainsKey(HeaderNames.Origin)
           && request.Headers.ContainsKey(HeaderNames.AccessControlRequestMethod);
}
