using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SqlOS.Todo.E2eTests;

/// <summary>
/// Browser end-to-end coverage for the Todo sample on PostgreSQL. Boots the
/// real Todo app host (Postgres container + Todo API + Razor client) on
/// alternate ports so a manually running demo on 5080/5090 is left untouched,
/// then drives Chromium through hosted AuthPage signup and the signed-in
/// ASP.NET Core session. Success screenshots land in TestResults for the PR.
/// </summary>
[TestClass]
public sealed class TodoPostgresE2eTests
{
    // Deliberately different from the demo's 5080/5090.
    private const int ApiPort = 5180;
    private const int WebPort = 5190;
    private const string Password = "TodoE2ePassword123!";

    private static readonly string ApiOrigin = $"http://localhost:{ApiPort}";
    private static readonly string WebOrigin = $"http://localhost:{WebPort}";
    private static readonly TimeSpan StartupBudget = TimeSpan.FromMinutes(
        int.TryParse(Environment.GetEnvironmentVariable("TODO_E2E_STARTUP_MINUTES"), out var minutes) ? minutes : 6);

    private static DistributedApplication? _app;
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SqlOS_Todo_AppHost>(
        [
            $"--Todo:ApiPort={ApiPort}",
            $"--Todo:WebPort={WebPort}",
            "--Todo:EphemeralSql=true",
            "--SqlOS:DatabaseProvider=PostgreSql"
        ]);
        _app = await builder.BuildAsync();
        await _app.StartAsync();

        try
        {
            using var startup = new CancellationTokenSource(StartupBudget);
            await _app.ResourceNotifications.WaitForResourceAsync("todo-api", KnownResourceStates.Running, startup.Token);
            await _app.ResourceNotifications.WaitForResourceAsync("aspnet-web", KnownResourceStates.Running, startup.Token);
            await WaitForOkAsync($"{ApiOrigin}/sqlos/auth/.well-known/openid-configuration", StartupBudget);
            await WaitForOkAsync($"{ApiOrigin}/sample/config", StartupBudget);
            await WaitForOkAsync($"{WebOrigin}/", StartupBudget);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The Todo app host did not become ready within {StartupBudget}.\n{await DumpResourceLogsAsync(_app)}", ex);
        }

        (_playwright, _browser) = await LaunchChromiumAsync();
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static async Task ClassCleanup()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [TestMethod]
    public Task HostedSignup_CreatesAccountAndLandsSignedInOnPostgres() => RunWithBrowserAsync(async page =>
    {
        var email = NewEmail();

        await page.GotoAsync(WebOrigin);
        await page.GetByRole(AriaRole.Link, new() { Name = "Sign in with SqlOS" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/sqlos/auth/"), new() { Timeout = 60_000 });

        await page.GetByRole(AriaRole.Link, new() { Name = "Get started" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Ship the Todo app first." })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".panel-kicker")).ToHaveTextAsync("Create account");
        await Assertions.Expect(page.GetByLabel("Display name")).ToBeVisibleAsync();
        await CaptureScreenshotAsync(page, "authpage-signup");

        await page.GetByLabel("Display name").FillAsync("Todo E2E");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(Password);
        await page.GetByLabel("Organization name").FillAsync($"Todo Org {Guid.NewGuid():N}"[..24]);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await CompleteOrganizationSelectionIfNeededAsync(page);
        await ExpectSignedInAsync(page, email);
        await CaptureScreenshotAsync(page, "signed-in-razor");
    });

    // ---- journey helpers -------------------------------------------------

    private static string NewEmail() => $"e2e-{Guid.NewGuid():N}@todo.test";

    private static async Task CompleteOrganizationSelectionIfNeededAsync(IPage page)
    {
        var organization = page.Locator("button.organization-option").First;
        try
        {
            await organization.WaitForAsync(new() { Timeout = 5_000 });
            await organization.ClickAsync();
        }
        catch (TimeoutException)
        {
            // First-party clients often skip the picker when signup already
            // selected the new organization.
        }
    }

    private static async Task ExpectSignedInAsync(IPage page, string email)
    {
        await page.WaitForURLAsync(
            url => url.StartsWith(WebOrigin, StringComparison.Ordinal),
            new() { Timeout = 90_000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign out and revoke" }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Assertions.Expect(page.Locator(".identity-card").GetByText(email, new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".status-dot")).ToHaveTextAsync("200");
        await Assertions.Expect(page.Locator("pre code")).ToContainTextAsync("tenantResourceId");
        StringAssert.Contains(
            await page.Locator("pre code").InnerTextAsync(),
            email,
            "GET /api/me should echo the signed-in email from PostgreSQL-backed SqlOS.");
    }

    // ---- infrastructure --------------------------------------------------

    private async Task RunWithBrowserAsync(Func<IPage, Task> test)
    {
        Assert.IsNotNull(_browser, "browser fixture was not initialized");
        await using var context = await _browser.NewContextAsync();
        context.SetDefaultTimeout(30_000);
        context.SetDefaultNavigationTimeout(60_000);
        var page = await context.NewPageAsync();

        try
        {
            await test(page);
        }
        catch
        {
            await CaptureScreenshotAsync(page, TestContext.TestName ?? "failure");
            throw;
        }
    }

    private async Task CaptureScreenshotAsync(IPage page, string name)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{name}.png");
            await page.ScreenshotAsync(new() { Path = path, FullPage = true });
            TestContext.WriteLine($"Screenshot: {path} (page URL: {page.Url})");
        }
        catch (Exception ex)
        {
            TestContext.WriteLine($"Could not capture screenshot: {ex.Message}");
        }
    }

    private static async Task<(IPlaywright Playwright, IBrowser Browser)> LaunchChromiumAsync()
    {
        var playwright = await Playwright.CreateAsync();
        try
        {
            return (playwright, await playwright.Chromium.LaunchAsync(new() { Headless = true }));
        }
        catch (PlaywrightException)
        {
            var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exitCode != 0)
            {
                playwright.Dispose();
                throw new InvalidOperationException($"'playwright install chromium' failed with exit code {exitCode}.");
            }

            return (playwright, await playwright.Chromium.LaunchAsync(new() { Headless = true }));
        }
    }

    private static async Task<string> DumpResourceLogsAsync(DistributedApplication app)
    {
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        var report = new System.Text.StringBuilder();
        foreach (var resource in new[] { "sql", "todo-api", "aspnet-web" })
        {
            var lines = new List<string>();
            try
            {
                using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await foreach (var batch in logs.GetAllAsync(resource).WithCancellation(drain.Token))
                {
                    lines.AddRange(batch.Select(line => line.Content));
                }
            }
            catch (OperationCanceledException)
            {
                // drained
            }
            catch (Exception ex)
            {
                lines.Add($"(could not read logs: {ex.Message})");
            }

            report.AppendLine($"===== {resource} (last {Math.Min(lines.Count, 80)} of {lines.Count} lines) =====");
            foreach (var line in lines.TakeLast(80))
            {
                report.AppendLine(line);
            }
        }

        return report.ToString();
    }

    private static async Task WaitForOkAsync(string url, TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastFailure = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastFailure = new InvalidOperationException($"{url} returned {(int)response.StatusCode}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastFailure = ex;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException($"{url} did not return a success status within {timeout}.", lastFailure);
    }
}
