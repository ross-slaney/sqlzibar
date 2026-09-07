using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace SqlOS.Hosting;

/// <summary>
/// Minimal <see cref="IEndpointRouteBuilder"/> the startup filter maps SqlOS endpoints against. Its
/// data sources are then added to <c>RouteOptions.EndpointDataSources</c>, which the application's
/// own endpoint routing dispatches.
/// </summary>
internal sealed class SqlOSEndpointRouteBuilder : IEndpointRouteBuilder
{
    public SqlOSEndpointRouteBuilder(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public IServiceProvider ServiceProvider { get; }

    public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

    public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
}
