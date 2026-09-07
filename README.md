# SqlOS

**A complete auth stack for .NET B2B SaaS — inside your app, on the SQL Server or PostgreSQL database you already run. No identity service to deploy.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/SqlOS)](https://www.nuget.org/packages/SqlOS)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com)

One NuGet package adds authentication *and* authorization to an ASP.NET Core app: an OAuth 2.0 / OpenID Connect server, a branded login and signup UI, organizations and sessions, hierarchical role-based access control that filters your EF Core queries in SQL, and an admin dashboard. Everything runs in your process and stores in your database — nothing extra to stand up, pay for, or keep in sync with your data.

SqlOS has two shapes. **The standard one is one application, described in one call** — that is what almost every product needs and where you should start. The second shape, an identity provider for many applications, builds on the same host and is covered [after it](#second-shape-many-applications-identity-provider).

## The standard flow: one app, one call

`builder.AddSqlOS<TContext>(…)` describes your application once. SqlOS derives every protocol consequence — routes, token validation, metadata documents, client registration, seeds — so `Program.cs` contains no `MapSqlOS`, `RequireSqlOSAccessToken`, `AddMcpServer`, or `MapMcp` calls.

```csharp
builder.AddSqlOS<AppDbContext>(
    db => db.UseSqlServer(connectionString), // or db.UseNpgsql(connectionString)
    options => options.UseSingleApplication("Acme", app =>
    {
        app.Origin = "https://acme.example.com";               // everything else derives from this
        app.Api = "/api";                                      // bearer tokens for {Origin}/api required under /api
        app.Mcp("/mcp", mcp => mcp.WithTools<AcmeTools>());    // SqlOS.Mcp: hosted, OAuth-protected MCP server
        app.Brand(page => page.PrimaryColor = "#0f172a");      // hosted sign-in colors, logo, copy
        // app.Headless("/auth/authorize");                    // ...or your own sign-in UI instead of the hosted pages
        app.Authorization(fga => fga                           // your permission model, reconciled at startup
            .ResourceType("project", "Project")
            .Permission("PROJECT_READ", "Read projects", "project")
            .Role("project_viewer", "Viewer").Can("PROJECT_READ"));
    }));
```

That is a working auth server with hosted login at `/sqlos/auth/login`, a dashboard at `/sqlos`, a token-protected API under `/api`, and an OAuth-protected MCP server at `/mcp` that Codex, ChatGPT, Claude, and Cursor can connect to.

### Every option you need, on one screen

Inside `UseSingleApplication("Name", app => …)`:

| Option | Default | What SqlOS does with it |
| --- | --- | --- |
| `app.Origin` | required | Public origin of your app. Issuer (`{Origin}/sqlos/auth`), redirect URI (`{Origin}/auth/callback`), and audiences derive from it. |
| `app.Api = "/api"` | off | Requires a bearer token for the audience `{Origin}/api` on every request under `/api` (401 + `WWW-Authenticate` otherwise) and serves `/.well-known/oauth-protected-resource`. Handlers read the user with `http.GetSqlOSValidatedToken()`. |
| `app.Mcp("/mcp", mcp => …)` | off | Hosts an MCP server at `/mcp` with the same token protection, a distinct audience, its own protected-resource document, and portable-client registration (CIMD + resource indicators) turned on. Needs the `SqlOS.Mcp` package; the lambda is the MCP SDK's `IMcpServerBuilder`. |
| `app.Brand(page => …)` | `Sign in to {Name}` | Brands the hosted login, signup, OTP, MFA, and consent pages. Same options as `AuthServer.SeedAuthPage`. |
| `app.Headless("/auth/authorize")` | hosted pages | Switches to **your** sign-in UI: SqlOS redirects browser interaction to `{Origin}/auth/authorize` with the standard `request`, `view`, `email`, `pendingToken`, `mfaToken`, … parameters that `@sqlos/headless` reads. Same as `AuthServer.UseHeadlessAuthPage`. |
| `app.Authorization(fga => …)` | none | Declares resource types, permissions, and roles; reconciled idempotently at startup. Same as `Fga.Seed`. |
| `app.AllowedScopes`, `ClientId`, `RedirectPath`, `EnablePasswordSignup`, `EnabledCredentialTypes` | sensible | Fine-tuning of the single first-party PKCE client that SqlOS seeds for you. |

Outside the `app` block, two things matter on day one: `options.Dashboard.AuthMode` / `Password` (the admin dashboard defaults to development-only access) and the sign-in methods you turn on — passwords, email OTP, magic links, social login, SAML — which are `options.AuthServer` seeds or dashboard settings, not code changes.

### Hosted or headless: one line

The hosted pages are the default and need nothing. When your product wants to own every screen, keep the same call and add one line:

```csharp
app.Headless("/auth/authorize");   // your page at {Origin}/auth/authorize drives login, signup, OTP, MFA, consent
```

Your frontend then talks to a typed state machine (`npm install @sqlos/headless`) while SqlOS still owns OAuth, PKCE, sessions, and tokens. `app.Brand(...)` still applies; the headless view model exposes it. For full control over the redirect use `app.Headless(headless => headless.BuildUiUrl = …)`. → [Build your own login UI](https://sqlos.dev/docs/guides/custom-login-ui)

### Add it to a project

You'll need **.NET 9**, **EF Core 9**, and a **SQL Server or PostgreSQL** database your application can reach.

```bash
dotnet add package SqlOS --version 4.2.0
dotnet add package SqlOS.Mcp --version 4.2.0   # only if you host an MCP server
npm install @sqlos/headless@4.2.0              # only if you build your own login UI
```

Derive your `DbContext` from `SqlOSDbContext<TContext>` so SqlOS can register its EF Core model, then declare your application:

```csharp
using Microsoft.EntityFrameworkCore;
using SqlOS;
using SqlOS.AuthServer.Extensions;
using SqlOS.Configuration;
using SqlOS.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not configured.");

const string appOrigin = "http://localhost:5050";
var dashboardPassword = builder.Configuration["SqlOS:Dashboard:Password"]
    ?? throw new InvalidOperationException(
        "Configure SqlOS:Dashboard:Password with user secrets or your secret store.");

builder.AddSqlOS<AppDbContext>(
    db => db.UseSqlServer(connectionString), // or db.UseNpgsql(connectionString)
    options =>
    {
        options.UseSingleApplication("Acme", app =>
        {
            app.Origin = appOrigin;
            app.Api = "/api";
            app.Brand(page => page.PrimaryColor = "#0f172a");
            // app.Headless("/auth/authorize");                       // your own sign-in UI instead
            // app.Mcp("/mcp", mcp => mcp.WithTools<AcmeTools>());    // SqlOS.Mcp package
            app.Authorization(fga => fga
                .ResourceType("project", "Project")
                .Permission("PROJECT_READ", "Read projects", "project")
                .Role("project_viewer", "Viewer").Can("PROJECT_READ"));
        });

        options.Dashboard.AuthMode = SqlOSDashboardAuthMode.Password;
        options.Dashboard.Password = dashboardPassword;
    });

var app = builder.Build();

app.MapGet("/", () => "SqlOS is running");
app.MapGet("/api/me", (HttpContext http) => http.GetSqlOSValidatedToken()!.UserId); // already protected

app.Run();

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : SqlOSDbContext<AppDbContext>(options)
{
}
```

Run it on the origin you declared (`dotnet run --urls http://localhost:5050`) and you have:

| What | Where |
| --- | --- |
| Admin dashboard | `http://localhost:5050/sqlos` |
| Hosted login | `http://localhost:5050/sqlos/auth/login` |
| OAuth / OIDC metadata | `http://localhost:5050/sqlos/auth/.well-known/oauth-authorization-server` |
| Protected-resource metadata | `http://localhost:5050/.well-known/oauth-protected-resource` |
| Your API | `http://localhost:5050/api/…` — 401 without a token for `http://localhost:5050/api` |

SqlOS creates and upgrades its own tables at startup — your EF migrations keep owning only your application's tables. Signing-key protection is configured automatically. **SQL Server or PostgreSQL — you choose.** Switch `UseSqlServer` for `UseNpgsql` and SqlOS loads the matching schema, locks, and FGA functions; there is no second package or dashboard toggle.

Runnable version: [`examples/SqlOS.OneCall.Api`](examples/SqlOS.OneCall.Api). Docs: [Getting started](https://sqlos.dev/docs/getting-started) · [Single application](https://sqlos.dev/docs/authserver/single-application) · [Protect an API](https://sqlos.dev/docs/quickstarts/protect-api) · [Host an MCP server](https://sqlos.dev/docs/authserver/mcp-server) · [Sign in from ASP.NET Core](https://sqlos.dev/docs/quickstarts/aspnet-core-login)

## Second shape: many applications (identity provider)

When other applications should sign in with your accounts — a separate SPA and API, partner apps, CLIs, or "Sign in with Acme" — the same host becomes an identity provider. Drop `UseSingleApplication` and declare each client explicitly:

```csharp
builder.AddSqlOS<AppDbContext>(
    db => db.UseSqlServer(connectionString),
    options =>
    {
        options.AuthServer.PublicOrigin = "https://id.acme.example.com";
        options.AuthServer.Issuer = "https://id.acme.example.com/sqlos/auth";
        options.AuthServer.SeedClient(client =>
        {
            client.ClientId = "acme-web";
            client.Name = "Acme Web";
            client.ClientType = "public_pkce";
            client.RedirectUris = ["https://app.acme.example.com/auth/callback"];
            client.AllowedScopes = ["openid", "profile", "email", "offline_access"];
            client.IsFirstParty = true;
        });
        options.AuthServer.SeedClient(client => { client.ClientId = "partner-portal"; /* third party: consent, audience, scopes */ });
    });
```

Everything from the standard flow still applies (hosted or headless login, branding, FGA, dashboard); you additionally get per-client consent, audiences, [Sign in with X](https://sqlos.dev/docs/guides/sign-in-with-x), [CLI device flow](https://sqlos.dev/docs/guides/terminal-auth), [machine clients](https://sqlos.dev/docs/authserver/machine-clients), and portable-client registration ([CIMD](https://sqlos.dev/docs/authserver/client-id-metadata-documents) / [DCR](https://sqlos.dev/docs/authserver/dynamic-client-registration)). Clients can also be created from the dashboard or admin API — all three control planes share one validation and audit path. → [Multiple applications](https://sqlos.dev/docs/authserver/multiple-applications) · [Clients](https://sqlos.dev/docs/authserver/clients)

## What's in the box

### 1. An auth server

A full OAuth 2.0 authorization server and OpenID Connect Provider mounted inside your app: authorization code + PKCE, refresh tokens, device flow, client credentials, discovery, ID tokens, UserInfo, and a consent screen for third-party clients — verified against the OpenID Foundation conformance suite in CI.

Sign-in methods are configuration, not projects: passwords, email OTP, magic links, SMS, social login (Google, Microsoft, GitHub, Apple, any OIDC provider), SAML enterprise SSO, SCIM directory sync, and TOTP MFA. B2B primitives — organizations, memberships, invitations, per-application access rules — are built in. Other apps can even use yours as their identity provider ([Sign in with X](https://sqlos.dev/docs/guides/sign-in-with-x)).

<p align="center">
  <img src="https://sqlos.dev/docs/dashboard-home.png" alt="SqlOS admin dashboard home showing Auth Server and Fine-Grained Auth counts" width="900" />
</p>

<p align="center">
  <img src="https://sqlos.dev/docs/guides-sign-in-with-x-consent.png" alt="SqlOS consent screen for Sign in with X, listing scopes by display name" width="560" />
</p>

→ [Auth server overview](https://sqlos.dev/docs/authserver/overview) · [OpenID Provider](https://sqlos.dev/docs/authserver/openid-provider) · [Organizations](https://sqlos.dev/docs/authserver/organizations)

### 2. AuthPage — hosted login UI

Login, signup, OTP entry, MFA, organization selection, and consent ship as hosted pages, ready on day one. Brand them with your name, logo, and colors — from code seeds, the Admin API, or the dashboard — and the same identity carries into the built-in OTP, invitation, and password-reset emails.

<p align="center">
  <img src="https://sqlos.dev/docs/guides-social-sign-in.png" alt="Hosted AuthPage with email continue plus GitHub and Microsoft social providers" width="560" />
</p>

→ [Brand hosted auth and email](https://sqlos.dev/docs/guides/auth-branding) · [Hosted vs. headless](https://sqlos.dev/docs/authserver/hosted-vs-headless)

### 3. SHRBAC — authorization inside your EF Core queries

SqlOS's hierarchical role-based access control models your resources as a tree (org → workspace → project), defines permissions and roles, and grants them to users, groups, service accounts, or agents. Point checks answer "can this user do X to this resource?", and — the part that changes how you write code — list queries get an authorization filter that runs **in SQL**, so users only ever receive rows they're allowed to see:

```csharp
var filter = await authorization.BuildFilterAsync<Project>(userId, "project.read");
var projects = await db.Projects.Where(filter).ToListAsync();
```

No sidecar, no policy service round-trips, no post-filtering in memory. The same grants shape product UI — a company admin and a store clerk hit the same endpoints and see different rows:

<table>
  <tr>
    <td width="50%" valign="top">
      <p><strong>Company Admin</strong> — five chains visible</p>
      <img src="https://sqlos.dev/docs/retail-app-admin-dashboard.png" alt="Retail app as Company Admin with five chains and multi-store inventory" />
    </td>
    <td width="50%" valign="top">
      <p><strong>Store Clerk</strong> — one store, filtered in SQL</p>
      <img src="https://sqlos.dev/docs/retail-app-clerk-dashboard.png" alt="Retail app as Store Clerk with zero chains and one store visible" />
    </td>
  </tr>
</table>

→ [Authorize EF Core queries](https://sqlos.dev/docs/quickstarts/ef-authorization) · [Model your FGA](https://sqlos.dev/docs/guides/model-fga) · [EF query filters](https://sqlos.dev/docs/guides/ef-query-filters)

### 4. Headless auth — bring your own UI

If the hosted pages don't fit your product, keep SqlOS as the protocol engine and draw every screen yourself: `app.Headless("/auth/authorize")` in the one-call setup, or `AuthServer.UseHeadlessAuthPage` on a multi-app host. Your frontend talks to a typed state machine — login, signup, OTP, MFA, consent — while SqlOS still owns OAuth, PKCE, sessions, and tokens. Extra signup fields, A/B tests, and native-feeling popups all become your UI's decisions.

<p align="center">
  <img src="https://sqlos.dev/docs/guides-custom-login-ui.svg" alt="Product-owned login UI connected to the SqlOS headless authentication state machine" width="900" />
</p>

→ [Build your own login and signup UI](https://sqlos.dev/docs/guides/custom-login-ui) · [Headless auth reference](https://sqlos.dev/docs/authserver/headless-auth)

## See it running in 2 minutes

The Todo sample gives you a working login flow and authorized EF Core queries without touching your own code:

```bash
dotnet run --project examples/SqlOS.Todo.AppHost/SqlOS.Todo.AppHost.csproj
```

Then open `http://localhost:5090/`. The Aspire AppHost starts PostgreSQL, the Todo API with SqlOS at `http://localhost:5080`, and a Razor Pages client at `http://localhost:5090`. Set `SqlOS:DatabaseProvider=SqlServer` to start SQL Server instead.

<p align="center">
  <img src="https://sqlos.dev/docs/guides-password-login.png" alt="Hosted AuthPage password step from the SqlOS Todo sample" width="560" />
</p>

[Todo sample walkthrough](https://sqlos.dev/docs/quickstarts/run-todo) · [All documentation](https://sqlos.dev/docs)

## Guides

Everything is documented as a task, not a feature tour. Start with whichever matches your next milestone:

| I want to… | Guide |
| --- | --- |
| Run on SQL Server or PostgreSQL | [Choose a database](https://sqlos.dev/docs/guides/choosing-a-provider) |
| Protect an API with access tokens | [Protect an API](https://sqlos.dev/docs/quickstarts/protect-api) |
| Return only the rows a user may see | [Authorize EF Core queries](https://sqlos.dev/docs/quickstarts/ef-authorization) |
| Add native ASP.NET Core password login | [Password login](https://sqlos.dev/docs/guides/password-login) |
| Add Google/Microsoft/GitHub sign-in | [Social OIDC login](https://sqlos.dev/docs/guides/social-oidc) |
| Sell to enterprises that require SSO | [SAML SSO](https://sqlos.dev/docs/guides/saml-sso) · [SCIM directory sync](https://sqlos.dev/docs/guides/scim-directory-sync) |
| Let other apps sign in with my app's accounts | [Sign in with X](https://sqlos.dev/docs/guides/sign-in-with-x) |
| Build my own login UI | [Custom login UI](https://sqlos.dev/docs/guides/custom-login-ui) |
| Host an MCP server agents can sign in to | [MCP server](https://sqlos.dev/docs/authserver/mcp-server) |
| Authenticate CLIs, background jobs, or MCP clients | [Terminal auth](https://sqlos.dev/docs/guides/terminal-auth) · [Service accounts](https://sqlos.dev/docs/guides/service-account-jobs) · [MCP OAuth](https://sqlos.dev/docs/guides/mcp-oauth) |
| Go to production safely | [Production readiness](https://sqlos.dev/docs/guides/production-readiness) |

More at the [guides index](https://sqlos.dev/docs/guides/index), plus the [SDK reference](https://sqlos.dev/docs/reference/sdk-reference) and [HTTP API reference](https://sqlos.dev/docs/reference/api-reference).

Every administrative capability works through three equivalent control planes — typed code seeds, authenticated admin APIs, and the dashboard — sharing the same validation, tenancy, secret handling, and audit behavior. Configuration can live in source control, in automation, or with operators, without losing anything.

## Contributing

```bash
dotnet build SqlOS.sln
./scripts/unit-tests.sh
./scripts/integration-tests.sh
./scripts/docs-check.sh
```

SqlOS is MIT licensed. Issues and pull requests are welcome.
