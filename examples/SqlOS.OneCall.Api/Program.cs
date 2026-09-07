using Microsoft.EntityFrameworkCore;
using SqlOS.AuthServer.Extensions;
using SqlOS.Extensions;
using SqlOS.Mcp;
using SqlOS.OneCall.Api;

var builder = WebApplication.CreateBuilder(args);

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
    (DbContextOptionsBuilder db) => db.UseSqlServer(connectionString),
    options =>
    {
        options.UseSingleApplication("Notes", app =>
        {
            app.Origin = origin;
            app.Api = "/api";
            app.Mcp("/mcp", mcp => mcp.WithTools<NotesMcpTools>());

            app.Brand(page =>
            {
                page.PageTitle = "Notes";
                page.PageSubtitle = "Sign in to read and write your notes from the web or an MCP client.";
                page.PrimaryColor = "#14532d";
                page.AccentColor = "#16a34a";
            });

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

var app = builder.Build();

app.MapGet("/", () => Results.Text(
    "Notes sample. Sign in at /sqlos/auth, browse the dashboard at /sqlos, call the API under /api, connect an MCP client to /mcp.",
    "text/plain"));

// Already protected: SqlOS validated the bearer token for {origin}/api before these handlers run.
var api = app.MapGroup("/api");
api.MapGet("/notes", async (HttpContext http, NotesService notes, CancellationToken ct)
    => Results.Ok(await notes.ListAsync(http.GetSqlOSValidatedToken()!.UserId!, ct)));
api.MapPost("/notes", async (HttpContext http, NoteRequest request, NotesService notes, CancellationToken ct)
    => Results.Ok(await notes.AddAsync(http.GetSqlOSValidatedToken()!.UserId!, request.Text, ct)));

// Sample only: create the application tables. SqlOS creates and upgrades its own schema at startup.
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Database.EnsureCreatedAsync();
}

app.Run();
