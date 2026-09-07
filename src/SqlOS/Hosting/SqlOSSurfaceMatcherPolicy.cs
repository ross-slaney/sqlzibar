using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.Options;
using SqlOS.AuthServer.Configuration;
using SqlOS.AuthServer.Extensions;
using SqlOS.Configuration;

namespace SqlOS.Hosting;

/// <summary>
/// Enforces the single-application <c>Api</c>/<c>Mcp</c> surfaces at endpoint dispatch: every
/// endpoint matched for a request under a declared surface path is replaced with a wrapper that
/// runs SqlOS bearer-token validation for that surface's audience before the original handler.
/// </summary>
/// <remarks>
/// Running inside endpoint routing rather than as early middleware keeps the application's own
/// pipeline (CORS, exception handling, rate limiting, HTTPS redirection) in front of the
/// challenge, so cross-origin preflight requests and error responses behave exactly as they do
/// for the application's unprotected routes. The wrapper preserves the endpoint's metadata and
/// display name; a valid token is exposed through <c>HttpContext.GetSqlOSValidatedToken()</c>
/// and reused by <c>RequireSqlOSAccessToken</c> on nested groups.
/// </remarks>
internal sealed class SqlOSSurfaceMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
{
    private readonly IReadOnlyList<(SqlOSSingleApplicationSurface Surface, RequestDelegate Pipeline)> _surfaces;
    private readonly ConcurrentDictionary<Endpoint, Endpoint> _wrapped = new(ReferenceEqualityComparer.Instance);

    public SqlOSSurfaceMatcherPolicy(IServiceProvider services, IOptions<SqlOSOptions> options)
    {
        _surfaces = SqlOSSingleApplicationSurfaces.Describe(options.Value.AuthServer.SingleApplication)
            .Select(surface => (surface, BuildPipeline(services, surface)))
            .ToArray();
    }

    // Run after the built-in HTTP-method and host policies so only real candidates are wrapped.
    public override int Order => int.MaxValue - 100;

    public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints) => _surfaces.Count > 0;

    public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
    {
        var surface = Resolve(httpContext.Request.Path);
        if (surface == null)
        {
            return Task.CompletedTask;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            if (!candidates.IsValidCandidate(i))
            {
                continue;
            }

            var candidate = candidates[i];
            if (candidate.Endpoint.Metadata.GetMetadata<SqlOSSurfaceWrappedEndpoint>() != null)
            {
                continue;
            }

            candidates.ReplaceEndpoint(i, Wrap(candidate.Endpoint, surface.Value), candidate.Values);
        }

        return Task.CompletedTask;
    }

    private (SqlOSSingleApplicationSurface Surface, RequestDelegate Pipeline)? Resolve(PathString path)
    {
        foreach (var entry in _surfaces)
        {
            if (SqlOSSingleApplicationSurfaces.Matches(path, entry.Surface.Path))
            {
                return entry;
            }
        }

        return null;
    }

    private Endpoint Wrap(Endpoint endpoint, (SqlOSSingleApplicationSurface Surface, RequestDelegate Pipeline) surface)
        => _wrapped.GetOrAdd(endpoint, static (original, state) =>
        {
            var inner = original.RequestDelegate
                ?? throw new InvalidOperationException($"Endpoint '{original.DisplayName}' has no request delegate.");
            var metadata = new EndpointMetadataCollection(
                original.Metadata.Append(new SqlOSSurfaceWrappedEndpoint(original, state.Surface.Path)));
            var pipeline = state.Pipeline;
            RequestDelegate wrapped = context => pipeline(context);

            return original is RouteEndpoint route
                ? new RouteEndpoint(wrapped, route.RoutePattern, route.Order, metadata, route.DisplayName)
                : new Endpoint(wrapped, metadata, original.DisplayName);
        }, surface);

    private static RequestDelegate BuildPipeline(IServiceProvider services, SqlOSSingleApplicationSurface surface)
    {
        var branch = new ApplicationBuilder(services);
        branch.UseSqlOSAccessTokenValidation(validation =>
        {
            validation.ExpectedAudience = surface.Audience;
            validation.Realm = surface.Realm;
            validation.ResourceMetadataUrl = surface.MetadataUrl;
        });
        branch.Run(context =>
        {
            var wrapped = context.GetEndpoint()?.Metadata.GetMetadata<SqlOSSurfaceWrappedEndpoint>()
                ?? throw new InvalidOperationException("SqlOS surface validation ran outside a wrapped endpoint.");
            return wrapped.Original.RequestDelegate!(context);
        });
        return branch.Build();
    }
}

/// <summary>
/// Marks an endpoint that SqlOS wrapped with surface token validation and keeps the original so
/// the validation pipeline can continue into it.
/// </summary>
/// <param name="Original">The application's (or companion package's) endpoint.</param>
/// <param name="SurfacePath">The declared surface path the request matched.</param>
internal sealed record SqlOSSurfaceWrappedEndpoint(Endpoint Original, string SurfacePath);
