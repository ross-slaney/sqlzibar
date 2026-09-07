using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using SqlOS.AuthServer.Configuration;
using SqlOS.AuthServer.Extensions;
using SqlOS.AuthServer.Services;
using SqlOS.Configuration;

namespace SqlOS.Hosting;

/// <summary>Enforces the declared surface before either middleware or endpoints can serve it.</summary>
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
        // Match the same application-relative path the router and middleware branches use.
        // UsePathBase may expose the same route with or without its mount prefix.
        var path = context.Request.Path;
        foreach (var surface in _surfaces)
        {
            if (SqlOSSingleApplicationSurfaces.Matches(path, surface.Path))
            {
                return surface.Validator.InvokeAsync(context, authService);
            }
        }

        return _next(context);
    }
}

internal sealed class SqlOSSurfaceProtectionState
{
    public bool ExplicitlyPlaced { get; set; }
    public IApplicationBuilder? RootApplicationBuilder { get; set; }
}
