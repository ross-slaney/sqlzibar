using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SqlOS.Hosting;

namespace SqlOS.Extensions;

/// <summary>Controls placement of the protection derived from the application description.</summary>
public static class SqlOSSurfaceProtectionExtensions
{
    /// <summary>
    /// Places API/MCP bearer validation in the root application's pipeline instead of using the
    /// automatic early guard. Call after exception handling, path rewriting, routing, CORS, and
    /// authentication, and before authorization or any middleware that serves protected content.
    /// No audiences or metadata URLs need to be repeated. Multiple calls are idempotent.
    /// </summary>
    /// <remarks>
    /// Required when the host registers ASP.NET authentication schemes, so a cookie principal
    /// cannot replace the surface's bearer identity. Policies under a surface should use the
    /// current principal rather than explicitly authenticating a different scheme.
    /// Only the root application can opt out of the automatic guard; a middleware branch cannot.
    /// </remarks>
    public static WebApplication UseSqlOSSurfaceProtection(this WebApplication app)
    {
        PlaceProtection(app);
        return app;
    }

    /// <summary>
    /// Places the surface guard in a conventional Startup.Configure root pipeline. Use the same
    /// ordering as the WebApplication overload. Calling this on a middleware branch is rejected.
    /// </summary>
    public static IApplicationBuilder UseSqlOSSurfaceProtection(this IApplicationBuilder app)
    {
        PlaceProtection(app);
        return app;
    }

    private static void PlaceProtection(IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var state = app.ApplicationServices.GetRequiredService<SqlOSSurfaceProtectionState>();
        if (app is not WebApplication && !ReferenceEquals(state.RootApplicationBuilder, app))
        {
            throw new InvalidOperationException(
                "UseSqlOSSurfaceProtection must be called on the root application, before protected middleware branches.");
        }
        if (!state.ExplicitlyPlaced)
        {
            state.ExplicitlyPlaced = true;
            app.UseMiddleware<SqlOSSurfaceProtectionMiddleware>();
        }
    }
}
