using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using SqlOS.AuthServer.Configuration;
using SqlOS.AuthServer.Contracts;
using SqlOS.AuthServer.Models;
using SqlOS.AuthServer.Services;
using SqlOS.Configuration;
using SqlOS.Tests.Infrastructure;

namespace SqlOS.Tests;

[TestClass]
public sealed class SqlOSSingleApplicationTests
{
    [TestMethod]
    public async Task SingleApplication_Defaults_SeedsPublicPkceApplication()
    {
        await using var harness = CreateHarness(options =>
            options.UseSingleApplication("Todo", app => app.Origin = "https://todo.example.com"));

        await harness.Admin.UpsertSeededClientsAsync();

        var client = await harness.Context.Set<SqlOSClientApplication>().SingleAsync();
        client.ClientType.Should().Be("public_pkce");
        client.RequirePkce.Should().BeTrue();
        client.IsFirstParty.Should().BeTrue();
        client.AccessMode.Should().Be(SqlOSApplicationAccessModes.AllOrganizations);
        client.AllowDeviceAuthorization.Should().BeFalse();
    }

    [TestMethod]
    public async Task SingleApplication_Defaults_DerivesClientIdAudienceRedirectAndScopes()
    {
        await using var harness = CreateHarness(options =>
            options.UseSingleApplication("Todo", app => app.Origin = "https://todo.example.com"));

        await harness.Admin.UpsertSeededClientsAsync();

        var client = await harness.Context.Set<SqlOSClientApplication>().SingleAsync();
        client.ClientId.Should().Be("todo");
        client.Audience.Should().Be("todo");
        DeserializeJsonList(client.RedirectUrisJson).Should().Equal("https://todo.example.com/auth/callback");
        DeserializeJsonList(client.AllowedScopesJson).Should().BeEquivalentTo("openid", "profile", "email", "offline_access");
    }

    [TestMethod]
    public async Task SingleApplication_Overrides_AreApplied()
    {
        await using var harness = CreateHarness(options =>
            options.UseSingleApplication("Todo", app =>
            {
                app.Origin = "https://todo.example.com/base";
                app.ClientId = "todo-web";
                app.Audience = "https://todo.example.com/api";
                app.RedirectPath = "/signin/callback";
                app.AllowedScopes = ["openid", "profile", "todos.read", "todos.write"];
            }));

        await harness.Admin.UpsertSeededClientsAsync();

        var client = await harness.Context.Set<SqlOSClientApplication>().SingleAsync();
        client.ClientId.Should().Be("todo-web");
        client.Audience.Should().Be("https://todo.example.com/api");
        DeserializeJsonList(client.RedirectUrisJson).Should().Equal("https://todo.example.com/base/signin/callback");
        DeserializeJsonList(client.AllowedScopesJson).Should().BeEquivalentTo("openid", "profile", "todos.read", "todos.write");
    }

    [TestMethod]
    public async Task SingleApplication_InvalidOrigin_ThrowsClearStartupError()
    {
        await using var harness = CreateHarness(options =>
            options.UseSingleApplication("Todo", app => app.Origin = "not-a-url"));

        var act = async () => await harness.Admin.UpsertSeededClientsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Single-application Origin must be an absolute http(s) origin without query or fragment.");
    }

    [TestMethod]
    public async Task SingleApplication_DuplicateClientId_ThrowsClearStartupError()
    {
        await using var harness = CreateHarness(options =>
            options.UseSingleApplication("Todo", app => app.Origin = "https://todo.example.com"));
        harness.Context.Set<SqlOSClientApplication>().Add(new SqlOSClientApplication
        {
            Id = "cli_existing",
            ClientId = "todo",
            Name = "Existing Manual",
            Audience = "todo",
            RedirectUrisJson = "[\"https://todo.example.com/auth/callback\"]",
            RegistrationSource = "manual",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await harness.Context.SaveChangesAsync();

        var act = async () => await harness.Admin.UpsertSeededClientsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*owned by 'dashboard'*");
    }

    [TestMethod]
    public async Task SingleApplication_ConflictingManualSeed_FollowsDocumentedBehavior()
    {
        await using var harness = CreateHarness(options =>
        {
            options.UseSingleApplication("Todo", app => app.Origin = "https://todo.example.com");
            options.SeedBrowserClient("admin", "Admin", "https://admin.example.com/auth/callback");
        });

        var act = async () => await harness.Admin.UpsertSeededClientsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Single-application mode cannot be combined with explicit startup client seeds.*");
    }

    [TestMethod]
    public void SingleApplication_AuthPageBranding_IsSeededWhenEnabled()
    {
        var options = new SqlOSAuthServerOptions();
        options.UseSingleApplication("Todo", app => app.Origin = "https://todo.example.com");

        options.AuthPageSeed.Should().NotBeNull();
        options.AuthPageSeed!.PageTitle.Should().Be("Sign in to Todo");
        options.AuthPageSeed.EnablePasswordSignup.Should().BeTrue();
        options.AuthPageSeed.EnabledCredentialTypes.Should().Equal("password");
    }

    [TestMethod]
    public void SingleApplication_EmailBranding_IsSeededWhenEnabled()
    {
        var options = new SqlOSAuthServerOptions();
        options.UseSingleApplication("Todo", app => app.Origin = "https://todo.example.com");

        options.AuthEmailSeed.Should().NotBeNull();
        options.AuthEmailSeed!.ApplicationName.Should().Be("Todo");
    }

    [TestMethod]
    public void SingleApplication_DoesNotEnableDcrOrCimdByDefault()
    {
        var options = new SqlOSAuthServerOptions();
        options.UseSingleApplication("Todo", app => app.Origin = "https://todo.example.com");

        options.ClientRegistration.Dcr.Enabled.Should().BeFalse();
        options.ClientRegistration.Cimd.Enabled.Should().BeFalse();
        options.ResourceIndicators.Enabled.Should().BeFalse();
    }

    [TestMethod]
    public void SingleApplication_ApiSurface_DerivesAudienceWithoutEnablingCimdOrResourceIndicators()
    {
        var options = new SqlOSAuthServerOptions();
        options.UseSingleApplication("Todo", app =>
        {
            app.Origin = "https://todo.example.com";
            app.Api = "/api/";
        });

        SqlOSSingleApplicationSurfaces.ResolveApiAudience(options.SingleApplication).Should().Be("https://todo.example.com/api");
        SqlOSSingleApplicationSurfaces.ResolveClientAudience(options.SingleApplication!, "todo").Should().Be("https://todo.example.com/api");
        options.ClientRegistration.Cimd.Enabled.Should().BeFalse();
        options.ClientRegistration.Dcr.Enabled.Should().BeFalse();
        options.ResourceIndicators.Enabled.Should().BeFalse();
        options.DefaultAudience.Should().Be("sqlos");

        var surface = SqlOSSingleApplicationSurfaces.Describe(options.SingleApplication).Should().ContainSingle().Subject;
        surface.Kind.Should().Be(SqlOSSingleApplicationSurfaceKind.Api);
        surface.Path.Should().Be("/api");
        surface.Realm.Should().Be("Todo API");
        surface.MetadataUrl.Should().Be("https://todo.example.com/.well-known/oauth-protected-resource");
    }

    [TestMethod]
    public async Task SingleApplication_ApiSurface_SeedsFirstPartyClientWithApiAudience()
    {
        await using var harness = CreateHarness(options =>
            options.UseSingleApplication("Todo", app =>
            {
                app.Origin = "https://todo.example.com";
                app.Api = "/api";
            }));

        await harness.Admin.UpsertSeededClientsAsync();

        var client = await harness.Context.Set<SqlOSClientApplication>().SingleAsync();
        client.Audience.Should().Be("https://todo.example.com/api");
    }

    [TestMethod]
    public void SingleApplication_McpSurface_EnablesCimdAndResourceIndicatorsButNotDcr()
    {
        var options = new SqlOSAuthServerOptions();
        options.UseSingleApplication("Todo", app =>
        {
            app.Origin = "https://todo.example.com";
            app.Mcp = "/mcp";
        });

        options.ClientRegistration.Cimd.Enabled.Should().BeTrue();
        options.ResourceIndicators.Enabled.Should().BeTrue();
        options.ClientRegistration.Dcr.Enabled.Should().BeFalse();
        options.DefaultAudience.Should().Be("https://todo.example.com/mcp");
        SqlOSSingleApplicationSurfaces.ResolveMcpAudience(options.SingleApplication).Should().Be("https://todo.example.com/mcp");

        var surface = SqlOSSingleApplicationSurfaces.Describe(options.SingleApplication).Should().ContainSingle().Subject;
        surface.Kind.Should().Be(SqlOSSingleApplicationSurfaceKind.Mcp);
        surface.Realm.Should().Be("Todo MCP");
        surface.MetadataUrl.Should().Be("https://todo.example.com/.well-known/oauth-protected-resource/mcp");
    }

    [TestMethod]
    public void SingleApplication_McpSurface_KeepsExplicitDefaultAudience()
    {
        var options = new SqlOSAuthServerOptions { DefaultAudience = "custom-audience" };
        options.UseSingleApplication("Todo", app =>
        {
            app.Origin = "https://todo.example.com";
            app.Mcp = "/mcp";
        });

        options.DefaultAudience.Should().Be("custom-audience");
    }

    [TestMethod]
    public void SingleApplication_BrandAndAuthorization_ForwardToAuthPageAndFgaSeeds()
    {
        var options = new SqlOSOptions();
        options.AuthServer.UseSingleApplication("Todo", app =>
        {
            app.Origin = "https://todo.example.com";
            app.Brand(brand =>
            {
                brand.PrimaryColor = "#123456";
                brand.PageSubtitle = "Branded subtitle";
            });
            app.Authorization(fga => fga
                .ResourceType("todo", "Todo")
                .Permission("todo.read", "Read todos", "todo"));
        });
        SqlOSSingleApplicationSurfaces.ApplyHostConfiguration(options);

        options.AuthServer.AuthPageSeed.Should().NotBeNull();
        options.AuthServer.AuthPageSeed!.PrimaryColor.Should().Be("#123456");
        options.AuthServer.AuthPageSeed.PageSubtitle.Should().Be("Branded subtitle");
        options.AuthServer.AuthPageSeed.PageTitle.Should().Be("Sign in to Todo");
        options.Fga.StartupSeedData.Should().NotBeNull();
        options.Fga.StartupSeedData!.Permissions.Should().ContainSingle(permission => permission.Key == "todo.read");
        options.AuthServer.SingleApplication!.AuthorizationConfigurations.Should().BeEmpty("ApplyHostConfiguration is idempotent");
    }

    [TestMethod]
    public void SingleApplication_ConfigurationBinding_ReadsApiAndMcp()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlOS:Application:Name"] = "Todo",
                ["SqlOS:Application:Origin"] = "https://todo.example.com",
                ["SqlOS:Application:Api"] = "/api",
                ["SqlOS:Application:Mcp"] = "/mcp"
            })
            .Build();

        var options = new SqlOSAuthServerOptions();
        options.UseSingleApplication(configuration);

        options.SingleApplication!.Api.Should().Be("/api");
        options.SingleApplication.Mcp.Should().Be("/mcp");
        options.ClientRegistration.Cimd.Enabled.Should().BeTrue();
        options.ResourceIndicators.Enabled.Should().BeTrue();
    }

    [DataTestMethod]
    [DataRow("api", null, "must be an absolute path prefix starting with '/'")]
    [DataRow("/", null, "cannot be '/'")]
    [DataRow("", null, "cannot be empty")]
    [DataRow("/api?x=1", null, "without query string or fragment")]
    [DataRow("/.well-known/api", null, "cannot be under '/.well-known'")]
    [DataRow("/sqlos/api", null, "must not be equal to or under DashboardBasePath")]
    [DataRow("/sqlos/auth/api", null, "must not be equal to or under DashboardBasePath")]
    [DataRow("/api", "/api", "must be distinct, non-nested path prefixes")]
    [DataRow("/api", "/api/mcp", "must be distinct, non-nested path prefixes")]
    [DataRow("/api/v1", "/api", "must be distinct, non-nested path prefixes")]
    public void SingleApplication_SurfaceValidation_RejectsInvalidPaths(string api, string? mcp, string expectedMessage)
    {
        var act = () => ValidateHost(app =>
        {
            app.Origin = "https://todo.example.com";
            app.Api = api;
            app.Mcp = mcp;
        });

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(expectedMessage);
    }

    [TestMethod]
    public void SingleApplication_SurfaceValidation_McpUnderDashboardBasePath_NamesMcp()
    {
        var act = () => ValidateHost(app =>
        {
            app.Origin = "https://todo.example.com";
            app.Mcp = "/sqlos/mcp";
        });

        act.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Contain("AuthServer.SingleApplication.Mcp ('/sqlos/mcp') must not be equal to or under DashboardBasePath");
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("todo.example.com")]
    [DataRow("https://todo.example.com/base")]
    [DataRow("https://todo.example.com?x=1")]
    public void SingleApplication_SurfaceValidation_RequiresAbsoluteOriginWithoutPath(string? origin)
    {
        var act = () => ValidateHost(app =>
        {
            app.Origin = origin;
            app.Api = "/api";
        });

        act.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Contain("AuthServer.SingleApplication.Origin must be an absolute http(s) origin");
    }

    [TestMethod]
    public void SingleApplication_SurfaceValidation_RejectsAudienceThatDisagreesWithApi()
    {
        var act = () => ValidateHost(app =>
        {
            app.Origin = "https://todo.example.com";
            app.Api = "/api";
            app.Audience = "todo";
        });

        act.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Contain("AuthServer.SingleApplication.Audience must be 'https://todo.example.com/api'");
    }

    [TestMethod]
    public void SingleApplication_SurfaceValidation_AcceptsMatchingExplicitAudience()
    {
        var act = () => ValidateHost(app =>
        {
            app.Origin = "https://todo.example.com";
            app.Api = "/api";
            app.Audience = "https://todo.example.com/api";
        });

        act.Should().NotThrow();
    }

    [TestMethod]
    public void SingleApplication_SurfaceValidation_RejectsCimdDisabledAfterMcpDeclared()
    {
        var act = () => ValidateHost(
            app =>
            {
                app.Origin = "https://todo.example.com";
                app.Mcp = "/mcp";
            },
            options => options.AuthServer.ClientRegistration.Cimd.Enabled = false);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Contain("AuthServer.SingleApplication.Mcp is set but AuthServer.ClientRegistration.Cimd.Enabled is false");
    }

    [TestMethod]
    public void SingleApplication_SurfaceValidation_RejectsResourceIndicatorsDisabledAfterMcpDeclared()
    {
        var act = () => ValidateHost(
            app =>
            {
                app.Origin = "https://todo.example.com";
                app.Mcp = "/mcp";
            },
            options => options.AuthServer.ResourceIndicators.Enabled = false);

        act.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Contain("AuthServer.SingleApplication.Mcp is set but AuthServer.ResourceIndicators.Enabled is false");
    }

    [TestMethod]
    public void SingleApplication_SurfaceValidation_AcceptsApiAndMcpTogether()
    {
        var act = () => ValidateHost(app =>
        {
            app.Origin = "https://todo.example.com";
            app.Api = "/api";
            app.Mcp = "/mcp";
        });

        act.Should().NotThrow();
    }

    [TestMethod]
    public void SingleApplication_NoSurfaces_DoesNotRequireOrigin()
    {
        var act = () => ValidateHost(app => app.Origin = "https://todo.example.com/base");

        act.Should().NotThrow();
    }

    private static void ValidateHost(
        Action<SqlOSSingleApplicationOptions> configureApplication,
        Action<SqlOSOptions>? configureAfter = null)
    {
        var options = new SqlOSOptions();
        options.AuthServer.UseSingleApplication("Todo", configureApplication);
        configureAfter?.Invoke(options);
        SqlOSPathDefaults.Apply(options);
        SqlOSSingleApplicationSurfaces.ApplyHostConfiguration(options);
        SqlOSOptionsValidator.ValidateOrThrow(options);
    }

    [TestMethod]
    public async Task SingleApplication_ExistingAdvancedSeeds_StillWork()
    {
        await using var harness = CreateHarness(options =>
        {
            options.SeedBrowserClient("web", "Web", "https://web.example.com/auth/callback");
            options.SeedCliClient("cli", "CLI", "cli", "openid");
        });

        await harness.Admin.UpsertSeededClientsAsync();

        var clients = await harness.Context.Set<SqlOSClientApplication>().OrderBy(x => x.ClientId).ToListAsync();
        clients.Select(x => x.ClientId).Should().Equal("cli", "web");
    }

    [TestMethod]
    public async Task SingleApplication_DefaultAccess_AllOrganizationsUntilAssignmentsEnabled()
    {
        await using var harness = CreateHarness(options =>
            options.UseSingleApplication("Todo", app => app.Origin = "https://todo.example.com"));
        await harness.Admin.UpsertSeededClientsAsync();
        var client = await harness.Context.Set<SqlOSClientApplication>().SingleAsync();

        var check = await harness.Admin.CheckApplicationAccessAsync(client, userId: "usr_any", organizationId: "org_any");

        check.Allowed.Should().BeTrue();
        check.Source.Should().Be("all_organizations");
    }

    [TestMethod]
    public async Task SingleApplication_ConfigurationBinding_SeedsApplication()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SqlOS:Application:Name"] = "Todo",
                ["SqlOS:Application:Origin"] = "https://todo.example.com",
                ["SqlOS:Application:ClientId"] = "todo-web",
                ["SqlOS:Application:Audience"] = "https://todo.example.com/api",
                ["SqlOS:Application:RedirectPath"] = "/auth/callback"
            })
            .Build();
        await using var harness = CreateHarness(options => options.UseSingleApplication(configuration));

        await harness.Admin.UpsertSeededClientsAsync();

        var client = await harness.Context.Set<SqlOSClientApplication>().SingleAsync();
        client.ClientId.Should().Be("todo-web");
        client.Audience.Should().Be("https://todo.example.com/api");
    }

    private static Harness CreateHarness(Action<SqlOSAuthServerOptions> configure)
    {
        var context = new TestSqlOSInMemoryDbContext(
            new DbContextOptionsBuilder<TestSqlOSInMemoryDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var optionsValue = new SqlOSAuthServerOptions();
        configure(optionsValue);
        var options = Options.Create(optionsValue);
        var crypto = TestCryptoService.Create(context, options);
        var admin = new SqlOSAdminService(context, options, crypto);
        return new Harness(context, admin);
    }

    private static List<string> DeserializeJsonList(string json)
        => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(TestSqlOSInMemoryDbContext context, SqlOSAdminService admin)
        {
            Context = context;
            Admin = admin;
        }

        public TestSqlOSInMemoryDbContext Context { get; }
        public SqlOSAdminService Admin { get; }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }
}
