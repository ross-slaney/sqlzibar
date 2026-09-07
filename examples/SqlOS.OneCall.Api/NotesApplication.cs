using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SqlOS.AuthServer.Extensions;
using SqlOS.Extensions;
using SqlOS.Mcp;

namespace SqlOS.OneCall.Api;

/// <summary>The runnable Notes host; tests exercise these same registrations and routes.</summary>
public static class NotesApplication
{
    public static async Task<WebApplication> BuildAsync(string[] args, Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder?.Invoke(builder);

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not configured.");
        var origin = (builder.Configuration["Notes:Origin"] ?? "http://localhost:5085").TrimEnd('/');

        // One call describes the application. SqlOS derives the rest:
        //  - the auth server, hosted sign-in, and dashboard are mapped at startup (no MapSqlOS),
        //  - bearer tokens are validated under /api (audience {origin}/api) and /mcp (audience {origin}/mcp),
        //  - RFC 9728 documents are served for both surfaces,
        //  - the MCP server is registered and mapped on /mcp with CIMD + resource indicators enabled,
        //  - the AuthPage branding and the FGA model are seeded.
        builder.AddSqlOS<NotesDbContext>(
            (DbContextOptionsBuilder db) =>
            {
                switch (builder.Configuration["Notes:DatabaseProvider"] ?? "SqlServer")
                {
                    case "SqlServer": db.UseSqlServer(connectionString); break;
                    case "PostgreSQL": db.UseNpgsql(connectionString); break;
                    default: throw new InvalidOperationException("Notes:DatabaseProvider must be SqlServer or PostgreSQL.");
                }
            },
            options =>
            {
                options.UseSingleApplication("Notes", app =>
                {
                    app.Origin = origin;
                    app.Api = "/api";
                    app.ClientId = "notes";
                    app.RedirectPath = "/auth/callback";
                    app.Mcp("/mcp", mcp => mcp.WithTools<NotesMcpTools>());

                    app.Brand(page =>
                    {
                        page.PageTitle = "Notes";
                        page.PageSubtitle = "Sign in to read and write your notes from the web or an MCP client.";
                        page.PrimaryColor = "#14532d";
                        page.AccentColor = "#16a34a";
                    });

                    // To draw the sign-in screens yourself instead of using the hosted pages, add one line and
                    // serve your UI at {Origin}/auth/authorize (see docs/guides/custom-login-ui):
                    // app.Headless("/auth/authorize");

                    app.Authorization(fga =>
                    {
                        fga.ResourceType(NotesAuthorization.NotebookType, "Notebook", "One notebook per user.");
                        fga.Permission(NotesAuthorization.ReadPermission, "Read notes", NotesAuthorization.NotebookType);
                        fga.Permission(NotesAuthorization.WritePermission, "Write notes", NotesAuthorization.NotebookType);
                        fga.Role(NotesAuthorization.OwnerRole, "Notebook owner")
                            .Can(NotesAuthorization.ReadPermission, NotesAuthorization.WritePermission);
                    });
                });
            });

        builder.Services.AddScoped<NotesService>();

        // This is the browser client of our embedded provider. ASP.NET owns PKCE, state,
        // nonce, callback validation, and the encrypted HttpOnly application cookie.
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = "SqlOS";
        }).AddCookie(options =>
        {
            options.Cookie.Name = "notes.session";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            options.SlidingExpiration = false;
        }).AddOpenIdConnect("SqlOS", options =>
        {
            options.Authority = origin + "/sqlos/auth";
            options.ClientId = "notes";
            options.CallbackPath = "/auth/callback";
            options.ResponseType = "code";
            options.ResponseMode = "query";
            options.UsePkce = true;
            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            if (builder.Environment.IsDevelopment())
            {
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            }
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery();
        builder.Services.AddHttpClient("notes-api", client => client.BaseAddress = new Uri(origin));

        var app = builder.Build();
        app.UseExceptionHandler(errors => errors.Run(async http =>
        {
            var error = http.Features.Get<IExceptionHandlerFeature>()?.Error;
            http.Response.StatusCode = error switch
            {
                UnauthorizedAccessException => StatusCodes.Status403Forbidden,
                ArgumentException or AntiforgeryValidationException => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            };
            await http.Response.WriteAsJsonAsync(new { error = http.Response.StatusCode == 403
                ? "Your notebook access has been removed." : "The request could not be completed." });
        }));
        app.UseRouting();
        app.UseAuthentication();
        app.UseSqlOSSurfaceProtection();
        app.UseAuthorization();
        NotesBrowser.Map(app);

        var api = app.MapGroup("/api");
        api.MapGet("/notes", async (HttpContext http, NotesService notes, CancellationToken ct)
            => Results.Ok(await notes.ListAsync(http.GetSqlOSValidatedToken()!.UserId!, ct)));
        api.MapPost("/notes", async (HttpContext http, NoteRequest request, NotesService notes, CancellationToken ct)
            => Results.Ok(await notes.AddAsync(http.GetSqlOSValidatedToken()!.UserId!, request.Text, ct)));

        // Sample-only application schema setup; production apps use their own EF migrations.
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Database.EnsureCreatedAsync();
        return app;
    }
}
