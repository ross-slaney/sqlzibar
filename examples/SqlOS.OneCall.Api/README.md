# SqlOS one-call sample (Notes)

The smallest complete SqlOS host: one `AddSqlOS` call describes the application, and SqlOS derives everything else.

```csharp
builder.AddSqlOS<NotesDbContext>(
    (DbContextOptionsBuilder db) => db.UseSqlServer(connectionString),
    options => options.UseSingleApplication("Notes", app =>
    {
        app.Origin = origin;                                    // http://localhost:5085
        app.Api = "/api";                                       // protected REST surface
        app.Mcp("/mcp", mcp => mcp.WithTools<NotesMcpTools>()); // protected MCP surface (SqlOS.Mcp)
        app.Brand(page => { /* AuthPage colors and copy */ });
        app.Authorization(fga => { /* FGA resource types, permissions, roles */ });
    }));

var app = builder.Build();
app.MapGet("/api/notes", ...);   // already protected
app.Run();
```

`Program.cs` contains no `MapSqlOS`, `RequireSqlOSAccessToken`, `AddMcpServer`, `MapMcp`, or hand-written protected-resource document. Compare with the [Todo sample](../SqlOS.Todo.Api/README.md), which shows the explicit multi-client configuration of the same building blocks.

## What SqlOS derives

| Path | Behavior |
| --- | --- |
| `/sqlos/auth/*`, `/sqlos` | Auth server, hosted sign-in, and dashboard, mapped at startup |
| `/api/*` | Bearer token required for the audience `http://localhost:5085/api`; `401` with `WWW-Authenticate: Bearer realm="Notes API", resource_metadata=...` otherwise |
| `/mcp` | Bearer token required for the audience `http://localhost:5085/mcp`; the MCP server (stateless Streamable HTTP) runs here |
| `/.well-known/oauth-protected-resource` and `/.well-known/oauth-protected-resource/mcp` | RFC 9728 documents for the two surfaces |
| Authorization-server metadata | Advertises client ID metadata documents and resource indicators because an MCP surface is declared |

Both surfaces call the same `NotesService`, which provisions the user's notebook on first use and checks FGA (`NOTES_READ` / `NOTES_WRITE`) before every read or write. The MCP tools inject `ISqlOSMcpUserContext` and act as the connecting user; every tool call is recorded in Audit Logs.

## Run

Start SQL Server, point the sample at it, and run it. The connection string is not checked in; pass it through the environment (or `dotnet user-secrets`):

```bash
docker run -d --name sqlos-notes-sql -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=<choose-a-password>" \
  -p 1434:1433 mcr.microsoft.com/mssql/server:2022-latest
export ConnectionStrings__DefaultConnection="Server=localhost,1434;Database=sqlos-notes;User Id=sa;Password=<choose-a-password>;TrustServerCertificate=True"
dotnet run --project examples/SqlOS.OneCall.Api
```

Then:

- `http://localhost:5085/sqlos/auth/login` — hosted sign-in (create an account with a password)
- `http://localhost:5085/sqlos` — dashboard (open without a login in the Development environment)
- `curl -i http://localhost:5085/api/notes` — `401` with the API challenge
- `curl -i -X POST http://localhost:5085/mcp -H 'content-type: application/json' -d '{}'` — `401` with the MCP challenge
- `curl http://localhost:5085/.well-known/oauth-protected-resource/mcp` — the MCP protected-resource document

To connect an MCP client, point it at `http://localhost:5085/mcp`. Portable clients register with a client ID metadata document and request `resource=http://localhost:5085/mcp`; see [MCP server](https://sqlos.dev/docs/authserver/mcp-server).
