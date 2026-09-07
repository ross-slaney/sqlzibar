using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SqlOS.AuthServer.Contracts;
using SqlOS.AuthServer.Models;
using SqlOS.AuthServer.Services;
using SqlOS.Extensions;
using SqlOS.Fga.Models;
using SqlOS.IntegrationTests.Infrastructure;
using SqlOS.OneCall.Api;

namespace SqlOS.IntegrationTests;

[TestClass]
public sealed class NotesJourneyIntegrationTests
{
    private const string Origin = HostedAuthorizeTokenFixture.TrustedOrigin;

    [TestMethod]
    public async Task BrowserLogin_CreateNote_McpReadsSameData_AndRevocationSurvivesBothSurfaces()
    {
        await using var host = await NotesHost.StartAsync();
        var alice = await host.CreateUser("Alice");
        var bob = await host.CreateUser("Bob");
        using var browser = host.Browser();
        (await browser.GetStringAsync("/")).Should().Contain("Sign in or create an account");
        await host.LoginBrowser(browser, alice.Email);
        var home = await browser.GetStringAsync("/");
        home.Should().Contain("New note");
        (await browser.PostAsync("/notes", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["text"] = "Alice's <private> note",
            ["__RequestVerificationToken"] = Input(home, "__RequestVerificationToken")
        }))).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await browser.GetStringAsync("/")).Should().Contain("Alice&#x27;s &lt;private&gt; note");

        var aliceApi = await host.Token(alice, "/api");
        var aliceMcp = await host.Token(alice, "/mcp");
        var bobApi = await host.Token(bob, "/api");
        var bobMcp = await host.Token(bob, "/mcp");
        (await host.Api(HttpMethod.Get, aliceApi)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await host.Api(HttpMethod.Get, bobApi)).Content.ReadAsStringAsync()).Should().Be("[]");
        (await host.Api(HttpMethod.Get, aliceMcp)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var aliceTools = await host.Mcp(aliceMcp);
        await using var bobTools = await host.Mcp(bobMcp);
        var aliceNotes = await aliceTools.CallToolAsync("list_notes", new Dictionary<string, object?>());
        aliceNotes.IsError.Should().NotBe(true);
        JsonSerializer.Deserialize<string[]>(Text(aliceNotes)).Should().Contain("Alice's <private> note");
        Text(await bobTools.CallToolAsync("list_notes", new Dictionary<string, object?>())).Should().NotContain("Alice");
        var added = await aliceTools.CallToolAsync("add_note", new Dictionary<string, object?> { ["text"] = "Added through MCP" });
        added.IsError.Should().NotBe(true);
        (await browser.GetStringAsync("/")).Should().Contain("Added through MCP");

        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            await db.RevokeRoleAsync(alice.Id, NotesAuthorization.NotebookId(alice.Id), NotesAuthorization.OwnerRole);
            await db.SaveChangesAsync();
        }
        (await host.Api(HttpMethod.Get, aliceApi)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.Api(HttpMethod.Post, aliceApi)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await aliceTools.CallToolAsync("list_notes", new Dictionary<string, object?>())).IsError.Should().Be(true);
        (await aliceTools.CallToolAsync("add_note", new Dictionary<string, object?> { ["text"] = "denied" })).IsError.Should().Be(true);
        (await browser.GetStringAsync("/")).Should().Contain("access has been removed");
        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
            (await db.Set<SqlOSFgaGrant>().CountAsync(x => x.SubjectId == alice.Id)).Should().Be(0);
            await db.GrantRoleAsync(alice.Id, NotesAuthorization.NotebookId(alice.Id), NotesAuthorization.OwnerRole);
            await db.SaveChangesAsync();
        }
        (await host.Api(HttpMethod.Get, aliceApi)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await aliceTools.CallToolAsync("list_notes", new Dictionary<string, object?>())).IsError.Should().NotBe(true);
        home = await browser.GetStringAsync("/");
        (await browser.PostAsync("/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Input(home, "__RequestVerificationToken")
        }))).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await browser.GetStringAsync("/")).Should().Contain("Sign in or create an account");
        await using var finalScope = host.App.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<NotesDbContext>();
        (await finalDb.Set<SqlOSSession>().CountAsync(x => x.UserId == alice.Id && x.RevokedAt != null)).Should().BeGreaterThan(0);
        (await finalDb.Set<SqlOSAuditEvent>().CountAsync(x => x.Action == "mcp.tool.called" && x.UserId == alice.Id)).Should().BeGreaterThanOrEqualTo(5);
    }

    [TestMethod]
    public async Task ConcurrentFirstUse_CommitsOneNotebookAndGrant()
    {
        await using var host = await NotesHost.StartAsync();
        var user = await host.CreateUser("Concurrent");
        var tasks = Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var scope = host.App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<NotesService>().ListAsync(user.Id, CancellationToken.None);
        });
        (await Task.WhenAll(tasks)).Should().OnlyContain(notes => notes.Count == 0);
        await using var check = host.App.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<NotesDbContext>();
        (await db.Notebooks.CountAsync(x => x.UserId == user.Id)).Should().Be(1);
        (await db.Set<SqlOSFgaGrant>().CountAsync(x => x.SubjectId == user.Id)).Should().Be(1);
        (await db.Set<SqlOSFgaResource>().CountAsync(x => x.Id == NotesAuthorization.NotebookId(user.Id))).Should().Be(1);
    }

    private static string Text(CallToolResult result) => string.Join("", result.Content.OfType<TextContentBlock>().Select(x => x.Text));
    private static string Input(string html, string name)
    {
        var match = Regex.Match(html, "<input\\b(?=[^>]*\\bname=['\"]" + Regex.Escape(name) + "['\"])[^>]*\\bvalue=['\"](?<value>[^'\"]*)", RegexOptions.IgnoreCase);
        match.Success.Should().BeTrue($"form must contain {name}");
        return WebUtility.HtmlDecode(match.Groups["value"].Value);
    }

    private sealed record User(string Id, string Email);

    private sealed class NotesHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public static async Task<NotesHost> StartAsync()
        {
            var database = "NotesJourney_" + Guid.NewGuid().ToString("N");
            await TestDatabase.CreateDatabaseAsync(AspireFixture.SqlConnectionString, database);
            WebApplication? app = null;
            app = await NotesApplication.BuildAsync([], builder =>
            {
                builder.WebHost.UseTestServer();
                builder.Environment.EnvironmentName = "Development";
                builder.Logging.ClearProviders();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestDatabase.CreateIsolatedConnectionString(AspireFixture.SqlConnectionString, database),
                    ["Notes:Origin"] = Origin,
                    ["Notes:DatabaseProvider"] = TestDatabase.IsPostgreSql ? "PostgreSQL" : "SqlServer"
                });
                builder.Services.AddHttpClient("notes-api").ConfigurePrimaryHttpMessageHandler(() => app!.GetTestServer().CreateHandler());
                builder.Services.PostConfigure<OpenIdConnectOptions>("SqlOS", options =>
                    options.Backchannel = new HttpClient(new DeferredHandler(() => app!.GetTestServer().CreateHandler())));
            });
            await app.StartAsync();
            var client = app.GetTestClient();
            client.BaseAddress = new Uri(Origin);
            return new NotesHost(app, client);
        }
        public HttpClient Browser() => new(new CookieHandler(App.GetTestServer().CreateHandler())) { BaseAddress = new Uri(Origin) };
        public async Task<User> CreateUser(string name)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var email = name.ToLowerInvariant() + Guid.NewGuid().ToString("N") + "@example.test";
            var user = await scope.ServiceProvider.GetRequiredService<SqlOSAdminService>().CreateUserAsync(
                new SqlOSCreateUserRequest(name, email, HostedAuthorizeTokenFixture.Password));
            return new User(user.Id, email);
        }
        public async Task LoginBrowser(HttpClient browser, string email)
        {
            var challenge = await browser.GetAsync("/login");
            challenge.StatusCode.Should().Be(HttpStatusCode.Redirect);
            var authorize = await browser.GetAsync(challenge.Headers.Location);
            var html = await authorize.Content.ReadAsStringAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/sqlos/auth/login/password")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["requestId"] = Input(html, "requestId"), ["email"] = email,
                    ["password"] = HostedAuthorizeTokenFixture.Password,
                    ["__RequestVerificationToken"] = Input(html, "__RequestVerificationToken")
                })
            };
            request.Headers.Add("Origin", Origin);
            var login = await browser.SendAsync(request);
            var callback = await HostedAuthorizeTokenFixture.ReadClientRedirectAsync(login);
            (await browser.GetAsync(callback)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        }
        public async Task<string> Token(User user, string resource)
        {
            using var browser = Browser();
            var fixture = new HostedAuthorizeTokenFixture { App = App, Client = browser, Email = user.Email, UserId = user.Id };
            var start = await fixture.StartAuthorizeAsync("openid profile email", clientId: "notes", redirectUri: Origin + "/auth/callback",
                extraParameters: new Dictionary<string, string> { ["resource"] = Origin + resource });
            var code = await fixture.SubmitPasswordLoginAsync(start);
            var result = await fixture.ExchangeAuthorizationCodeAsync(code, start.CodeVerifier, clientId: "notes", redirectUri: Origin + "/auth/callback", resource: Origin + resource);
            return result.RootElement.GetProperty("access_token").GetString()!;
        }
        public Task<HttpResponseMessage> Api(HttpMethod method, string token)
        {
            var request = new HttpRequestMessage(method, "/api/notes");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (method == HttpMethod.Post) request.Content = JsonContent.Create(new NoteRequest("denied"));
            return client.SendAsync(request);
        }
        public Task<McpClient> Mcp(string token) => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(Origin + "/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token }
        }, client, LoggerFactory.Create(_ => { }), ownsHttpClient: false));
        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await App.StopAsync();
            await using (var scope = App.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Database.EnsureDeletedAsync();
            await App.DisposeAsync();
        }
    }

    private sealed class DeferredHandler(Func<HttpMessageHandler> create) : HttpMessageHandler
    {
        private HttpMessageInvoker? _inner;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => (_inner ??= new HttpMessageInvoker(create())).SendAsync(request, cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) _inner?.Dispose(); base.Dispose(disposing); }
    }
    private sealed class CookieHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly CookieContainer _cookies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Headers.Contains("Cookie")) request.Headers.TryAddWithoutValidation("Cookie", _cookies.GetCookieHeader(request.RequestUri!));
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (var cookie in cookies) _cookies.SetCookies(request.RequestUri!, cookie);
            return response;
        }
    }
}
