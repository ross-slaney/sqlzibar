using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlOS.AuditLogs;
using SqlOS.AuthServer.Configuration;
using SqlOS.AuthServer.Extensions;
using SqlOS.Calendar.Extensions;
using SqlOS.Configuration;
using SqlOS.Email.Extensions;
using SqlOS.Hosting;

namespace SqlOS.Extensions;

/// <summary>
/// Provides endpoint-mapping extensions for applications that host SqlOS.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps the SqlOS auth-server, audit-log administration, and transactional-email
    /// administration endpoints, plus calendar endpoints when calendar integration is enabled.
    /// </summary>
    /// <param name="app">The built ASP.NET Core application.</param>
    /// <returns>The same <paramref name="app"/> instance.</returns>
    /// <remarks>
    /// <para>
    /// This call is no longer required. <c>AddSqlOS</c> registers a startup filter that maps the
    /// same endpoints before the application's own routing runs, so an application that omits
    /// <c>MapSqlOS()</c> serves the OAuth, hosted login, admin API, and dashboard routes.
    /// </para>
    /// <para>
    /// Existing callers keep working: the method is idempotent, the startup filter detects that the
    /// application mapped the endpoints and does not register them a second time, and one warning
    /// is logged at startup.
    /// </para>
    /// </remarks>
    [Obsolete("MapSqlOS() is no longer required. AddSqlOS maps the SqlOS endpoints at startup; remove this call. Calling it remains safe and idempotent.", error: false)]
    public static WebApplication MapSqlOS(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var state = app.Services.GetRequiredService<SqlOSEndpointMappingState>();
        if (state.MappedByApplication)
        {
            return app;
        }

        // MapAuthServer records the application mapping and logs the obsolete-call warning.
        var sqlosOptions = app.Services.GetRequiredService<IOptions<SqlOSOptions>>().Value;
        var authOptions = app.Services.GetRequiredService<IOptions<SqlOSAuthServerOptions>>().Value;
        MapSqlOSCoreEndpoints(app, sqlosOptions, authOptions);
        return app;
    }

    /// <summary>
    /// Maps the auth-server, admin, email, and calendar endpoints under the SqlOS-owned prefixes.
    /// Shared by the startup filter and the obsolete <see cref="MapSqlOS"/>.
    /// </summary>
    internal static void MapSqlOSCoreEndpoints(
        IEndpointRouteBuilder endpoints,
        SqlOSOptions sqlosOptions,
        SqlOSAuthServerOptions authOptions)
    {
        endpoints.MapAuthServer(authOptions.BasePath);
        endpoints.MapSqlOSAuditLogsAdmin(sqlosOptions.DashboardBasePath);
        endpoints.MapSqlOSEmailAdmin(sqlosOptions.DashboardBasePath);
        if (sqlosOptions.Calendar.Enabled)
        {
            endpoints.MapSqlOSCalendarConnect(authOptions.BasePath);
            endpoints.MapSqlOSCalendarAdmin(sqlosOptions.DashboardBasePath);
        }
    }
}
