using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
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

[TestClass]
public sealed class SqlOSSurfaceProtectionTests
{
    private const string Origin = SingleApplicationTestHost.Origin;

    [TestMethod]
    public async Task ConventionalHost_CanPlaceProtectionWithAuthentication()
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
                app.UseSqlOSSurfaceProtection();
                app.UseAuthorization();
                app.Map("/api/legacy", branch => branch.Run(http => http.Response.WriteAsync("private")));
                app.UseEndpoints(endpoints => endpoints.MapGet("/public", () => "public"));
            }));
        using var client = server.CreateClient();
        (await client.GetAsync("/api/legacy")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/public")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/sqlos/auth/.well-known/openid-configuration")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task BranchPlacement_CannotDisableRootProtection()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
        {
            app.Map("/public", branch =>
            {
                var place = () => branch.UseSqlOSSurfaceProtection();
                place.Should().Throw<InvalidOperationException>().WithMessage("*root application*");
                branch.Run(http => http.Response.WriteAsync("public"));
            });
            app.Map("/api/legacy", branch => branch.Run(http => http.Response.WriteAsync("private")));
        });
        (await host.Client.GetAsync("/api/legacy")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Client.GetAsync("/public")).StatusCode.Should().Be(HttpStatusCode.OK);
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
    public async Task AutomaticGuard_ProtectsMiddlewareAndUnknownPaths_AndExposesIdentityEarly()
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

    [TestMethod]
    public async Task AuthenticationWithoutExplicitPlacement_FailsAtStartupWithActionableMessage()
    {
        var start = () => SingleApplicationTestHost.StartAsync(Configure,
            configureServices: services => services.AddAuthentication("cookie").AddCookie("cookie"));
        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseAuthentication*UseSqlOSSurfaceProtection*UseAuthorization*");
    }

    [TestMethod]
    public async Task ExplicitGuard_BearerIdentityWinsOverCookie_AndStandardAuthorizationWorks()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
        {
            app.UseAuthentication();
            app.UseSqlOSSurfaceProtection();
            app.UseSqlOSSurfaceProtection(); // One placement, no duplicated work.
            app.UseAuthorization();
            app.MapGet("/cookie", async (HttpContext http) =>
            {
                await http.SignInAsync("cookie", new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", "cookie-user")], "cookie")));
                return Results.Ok();
            });
            app.MapGet("/public/me", (HttpContext http) => http.User.FindFirst("sub")?.Value).RequireAuthorization();
            app.MapGet("/api/me", (HttpContext http) => new
            {
                subject = http.User.FindFirst("sub")?.Value,
                tokenSubject = http.GetSqlOSValidatedToken()!.UserId
            }).RequireAuthorization();
        }, configureServices: services =>
        {
            services.AddAuthentication("cookie").AddCookie("cookie");
            services.AddAuthorization();
        });
        var cookie = (await host.Client.GetAsync("/cookie")).Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        host.Client.DefaultRequestHeaders.Add("Cookie", cookie);
        (await host.Client.GetStringAsync("/public/me")).Should().Be("cookie-user");
        (await host.Client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a cookie is not an API bearer token");
        var token = await host.MintAccessTokenAsync(Origin + "/api");
        var response = await Send(host, "/api/me", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("subject").GetString().Should().Be(body.GetProperty("tokenSubject").GetString()).And.NotBe("cookie-user");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExplicitGuard_ComposesWithNamedAndEndpointCorsPolicies(bool endpointPolicy)
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
        {
            app.UseRouting();
            if (endpointPolicy) app.UseCors(); else app.UseCors("browser");
            app.UseSqlOSSurfaceProtection();
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
        var denied = await host.Client.SendAsync(get);
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        denied.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain("https://browser.example");
        denied.Headers.WwwAuthenticate.ToString().Should().Contain("resource_metadata");
    }

    [TestMethod]
    public async Task PreflightShapeWithoutCorsPolicy_DoesNotBypassProtection()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
            app.MapMethods("/api/options", ["OPTIONS"], () => "private"));
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/options");
        request.Headers.Add("Origin", "https://browser.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        (await host.Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task ExplicitGuard_RunsAfterRewriteAndInsideExceptionHandler()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(Configure, app =>
        {
            app.Use(async (context, next) =>
            {
                try { await next(context); }
                catch (InvalidOperationException) { context.Response.StatusCode = 503; }
            });
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/alias") context.Request.Path = "/api/legacy";
                await next(context);
            });
            app.UseRouting();
            app.UseSqlOSSurfaceProtection();
            app.Map("/api/legacy", branch => branch.Run(_ => throw new InvalidOperationException("sample failure")));
        });
        (await host.Client.GetAsync("/alias")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var token = await host.MintAccessTokenAsync(Origin + "/api");
        (await Send(host, "/alias", token)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [TestMethod]
    public async Task ExplicitGuard_PathBaseProtectsMountedAndUnmountedRoutes()
    {
        await using var host = await SingleApplicationTestHost.StartAsync(options => options.UseSingleApplication("Review", app =>
        {
            app.Origin = Origin;
            app.Api = "/api";
        }), app =>
        {
            app.UsePathBase("/tenant");
            app.UseRouting();
            app.UseSqlOSSurfaceProtection();
            app.MapGet("/api/me", () => "protected");
        });
        (await host.Client.GetAsync("/tenant/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await host.Client.GetAsync("/api/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var token = await host.MintAccessTokenAsync(Origin + "/api");
        (await Send(host, "/tenant/api/me", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(host, "/api/me", token)).StatusCode.Should().Be(HttpStatusCode.OK);
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
