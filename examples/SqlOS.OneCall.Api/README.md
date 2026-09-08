# Notes: one application, browser + API + MCP

This runnable application uses one `AddSqlOS` description for its auth server, hosted sign-in, API/MCP audiences, tools, branding, and permission model. ASP.NET Core's OIDC handler supplies the browser client's PKCE, callback validation, and encrypted HttpOnly session cookie.

## Run

Use .NET 9 and a SQL Server or PostgreSQL database. From the repository root, supply the connection through your environment or secret store:

```bash
export ConnectionStrings__DefaultConnection="Server=localhost,1434;Database=sqlos-notes;User Id=sa;Password=<your-password>;TrustServerCertificate=True"
dotnet run --project examples/SqlOS.OneCall.Api
```

For PostgreSQL instead:

```bash
export Notes__DatabaseProvider=PostgreSQL
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=sqlos_notes;Username=postgres;Password=<your-password>"
dotnet run --project examples/SqlOS.OneCall.Api
```

The launch profile uses Development and `http://localhost:5085`. Use `Notes:Origin` to change the public origin, and make the listening URL match. This sample creates its application tables with `EnsureCreatedAsync` on a fresh database; production applications use EF migrations for their tables. SqlOS initializes its own schema separately.

## Walk through the working app

1. Open `http://localhost:5085/` and choose **Sign in or create an account**. Complete hosted signup/sign-in. The OIDC handler processes `/auth/callback` and returns to the Notes page.
2. Add a note. The browser submits a CSRF-protected form; the backend calls `/api/notes` with its saved access token. Tokens are not rendered into the page or stored in browser JavaScript.
3. Connect a compatible MCP client to `http://localhost:5085/mcp`, sign in as the same user, and call `list_notes`. Call `add_note` and refresh the browser to see it. Internet-hosted clients need a publicly reachable HTTPS deployment. See [MCP client setup](https://sqlos.dev/docs/authserver/mcp-server).
4. Sign in as another user in a separate browser session. That user gets a different notebook and cannot read the first user's notes.
5. In the dashboard's FGA grants view, remove the first user's `notebook_owner` grant. Their browser/API reads and writes now fail, and both MCP tools return errors. Ordinary requests do not restore the grant. Explicitly restore it to grant access again.
6. Use **Sign out**. The sample revokes the browser client's OAuth session using its refresh token, clears the application cookie, and navigates through SqlOS logout to end the issuer session too. The next sign-in shows the login form. Other independently authenticated MCP sessions remain separate.

The cookie lasts ten minutes, without automatic token refresh. Sign in again when it expires or the API rejects the saved token. This keeps the sample's session lifecycle explicit; use the [sessions guide](https://sqlos.dev/docs/authserver/sessions-and-tokens) when adding renewal.

## What each piece does

| Configuration or file | Effect |
| --- | --- |
| [NotesApplication.cs](NotesApplication.cs) | Complete host registration, browser OIDC client, middleware order, API routes, and sample database setup |
| `app.Api = "/api"` | Requires a token for `http://localhost:5085/api` throughout the API prefix |
| `app.Mcp("/mcp", mcp => mcp.WithTools<NotesMcpTools>())` | Hosts and audits the MCP tools, protects the distinct MCP audience, and enables CIMD/resource indicators |
| `app.Brand(...)` | Seeds code-owned title, copy, and colors for the hosted pages |
| `app.Authorization(...)` | Seeds notebook resource type, read/write permissions, and the owner role; it grants nobody access by itself |
| [Notes.cs](Notes.cs) | Domain entities, transactional first-use provisioning, FGA enforcement, and MCP tools |
| [NotesBrowser.cs](NotesBrowser.cs) | Browser UI, CSRF-protected forms, server-side API calls, and logout |
| [AUTHORIZATION.md](AUTHORIZATION.md) | Intended resource/subject/grant model and revocation behavior |

Both surfaces call the same `NotesService`. First use commits the unique notebook row, subject, resource, and initial grant in one transaction. Every subsequent operation checks the current grant, so revocation persists. Concurrent first requests cannot duplicate the notebook or its owner grant.

`UseSqlOSSurfaceProtection()` runs after cookie authentication and before authorization or private handlers. The configured bearer identity controls `/api` and `/mcp`; a browser cookie alone cannot access either. There is no separate `MapSqlOS`, `AddMcpServer`, or `MapMcp` call.

## Inspect protocol behavior

```bash
curl -i http://localhost:5085/api/notes
curl -i -X POST http://localhost:5085/mcp -H 'content-type: application/json' -d '{}'
curl http://localhost:5085/.well-known/oauth-protected-resource
curl http://localhost:5085/.well-known/oauth-protected-resource/mcp
```

The first two return `401` with the appropriate Bearer challenge. The latter two describe distinct resources and the shared authorization server. Compatible portable clients use a client ID metadata document and request the MCP resource. DCR is not enabled by the MCP declaration.

The dashboard at `/sqlos` is open without a password in Development only. Configure dashboard authentication before enabling it in production.

`NotesJourneyIntegrationTests` runs the real application against SQL Server and PostgreSQL in CI. It covers browser callback, API/MCP success, cross-user isolation, wrong audience, revocation and explicit restoration, logout, audit events, and concurrent provisioning. See [multiple applications](https://sqlos.dev/docs/authserver/multiple-applications) for retaining this host while adding explicit clients.
