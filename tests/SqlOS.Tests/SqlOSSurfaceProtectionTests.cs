using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SqlOS.AuthServer.Extensions;
using SqlOS.Configuration;
using SqlOS.Extensions;
using SqlOS.Tests.Infrastructure;

namespace SqlOS.Tests;

/// <summary>
/// Declaring <c>Api</c>/<c>Mcp</c> is the only thing an application does. No test here calls a
/// placement method, orders middleware for SqlOS, or handles a startup exception about it.
/// </summary>
[TestClass]
public sealed class SqlOSSurfaceProtectionTests
{
    private const string Origin = SingleApplicationTestHost.Origin;

    [TestMethod]
    public async Task Guard_ProtectsMiddlewareBranchesAndUnknownPaths_AndExposesIdentityToHandlers()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
        {
            app.Map("/api/legacy", branch => branch.Run(context =>
                context.Response.WriteAsync(context.User.Identity!.IsAuthenticated.ToString())));
            app.MapGet("/apiary", () => "public");
        });
        (await host.Client.GetAsync("/api/legacy")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Client.GetAsync("/api/unknown")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Client.GetAsync("/API/legacy")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Client.GetAsync("/apiary")).StatusCode.Should().Be(HttpStatusCode.OK);
        var token = await host.MintAccessTokenAsync(Origin + "/api");
        var response = await Send(host, "/api/legacy", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("True");
    }

    [DataTestMethod]
    [DataRow("api", "mcp")]
    [DataRow("mcp", "api")]
    public async Task SharedRoute_UsesRequestedSurfaceRegardlessOfWarmupOrder(string first, string second)
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure,
            app => app.MapGet("/{surface}/me", (HttpContext http) => http.GetSqlOSValidatedToken()!.Audience));
        var firstToken = await host.MintAccessTokenAsync($"{Origin}/{first}");
        var secondToken = await host.MintAccessTokenAsync($"{Origin}/{second}");
        (await Send(host, $"/{first}/me", firstToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(host, $"/{second}/me", firstToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Send(host, $"/{second}/me", secondToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(host, $"/{first}/me", secondToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => i % 2 == 0
            ? Send(host, $"/{first}/me", firstToken) : Send(host, $"/{second}/me", secondToken)));
        results.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Challenge_NamesRealmAndResourceMetadata()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app => app.MapGet("/api/me", () => "private"));
        var response = await host.Client.GetAsync("/api/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var challenge = response.Headers.WwwAuthenticate.ToString();
        challenge.Should().Contain("realm=\"Review API\"");
        challenge.Should().Contain($"resource_metadata=\"{Origin}/.well-known/oauth-protected-resource\"");
    }

    [TestMethod]
    public async Task HostWithAspNetAuthentication_NeedsNothingExtra()
    {
        // The host registers its own authentication and authorization and adds nothing for SqlOS.
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
        {
            app.MapGet("/api/me", (HttpContext http) => http.GetSqlOSValidatedToken()!.UserId).RequireAuthorization();
            app.MapGet("/public", () => "public");
        }, configureServices: services =>
        {
            services.AddAuthentication("cookie").AddCookie("cookie");
            services.AddAuthorization();
        });
        (await host.Client.GetAsync("/public")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var token = await host.MintAccessTokenAsync(Origin + "/api");
        var response = await Send(host, "/api/me", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().StartWith("usr_");
    }

    [TestMethod]
    public async Task ConventionalStartupHost_IsProtectedWithoutPlacement()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        using var server = new TestServer(new WebHostBuilder().UseEnvironment("Development")
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddDbContext<TestSqlOSInMemoryDbContext>(db => db.UseInMemoryDatabase(databaseName));
                services.AddSqlOS<TestSqlOSInMemoryDbContext>(Configure);
                services.RemoveAll<IHostedService>();
                services.AddAuthentication("cookie").AddCookie("cookie");
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.Map("/api/legacy", branch => branch.Run(http => http.Response.WriteAsync("private")));
                app.UseEndpoints(endpoints => endpoints.MapGet("/public", () => "public"));
            }));
        using var client = server.CreateClient();
        (await client.GetAsync("/api/legacy")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/public")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/sqlos/auth/.well-known/openid-configuration")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CorsPreflight_IsAnsweredByHostCors_AndTheActualRequestIsStillValidated(bool endpointPolicy)
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
        {
            if (endpointPolicy) app.UseCors(); else app.UseCors("browser");
            var route = app.MapGet("/api/me", () => "private");
            if (endpointPolicy) route.RequireCors("browser");
        }, configureServices: services => services.AddCors(cors => cors.AddPolicy("browser", policy =>
            policy.WithOrigins("https://browser.example").WithMethods("GET").WithHeaders("Authorization"))));
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/me");
        preflight.Headers.Add("Origin", "https://browser.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "Authorization");
        var cors = await host.Client.SendAsync(preflight);
        cors.StatusCode.Should().Be(HttpStatusCode.NoContent);
        cors.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("https://browser.example");
        using var get = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        get.Headers.Add("Origin", "https://browser.example");
        (await host.Client.SendAsync(get)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var token = await host.MintAccessTokenAsync(Origin + "/api");
        (await Send(host, "/api/me", token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task PreflightShape_WithoutCorsRegistered_IsChallenged()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
            app.MapMethods("/api/options", ["OPTIONS"], () => "private"));
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/options");
        request.Headers.Add("Origin", "https://browser.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        (await host.Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task NoSurfaceDeclared_InstallsNoGuard()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(
            options => options.UseSingleApplication("Review", app => app.Origin = Origin),
            app => app.MapGet("/api/me", () => "open"));
        (await host.Client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static void Configure(SqlOSOptions options) => options.UseSingleApplication("Review", app =>
    {
        app.Origin = Origin;
        app.Api = "/api";
        app.Mcp = "/mcp";
    });

    private static Task<HttpResponseMessage> Send(SingleApplicationTestHost host, string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return host.Client.SendAsync(request);
    }
}
