using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SqlOS.Configuration;

namespace SqlOS.Hosting;

/// <summary>
/// Extension point for companion packages that host additional surfaces from the
/// single-application description without adding <c>Map*</c> calls to application code.
/// </summary>
/// <remarks>
/// <c>AddSqlOS</c> calls <see cref="ConfigureServices"/> after the options callback has run, and
/// the SqlOS startup filter calls <see cref="MapEndpoints"/> inside the routing pass it owns, after
/// the path-scoped access-token validation for the declared API and MCP surfaces has been installed.
/// Application code does not implement this interface; it is the contract between <c>SqlOS</c> and
/// packages such as <c>SqlOS.Mcp</c>.
/// </remarks>
public interface ISqlOSHostExtension
{
    /// <summary>Registers the services the extension needs.</summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="options">The fully configured SqlOS options.</param>
    void ConfigureServices(IServiceCollection services, SqlOSOptions options);

    /// <summary>Maps the extension's endpoints. Requests under a declared surface have already been validated.</summary>
    /// <param name="endpoints">The SqlOS-owned endpoint route builder.</param>
    /// <param name="options">The fully configured SqlOS options.</param>
    void MapEndpoints(IEndpointRouteBuilder endpoints, SqlOSOptions options);
}
