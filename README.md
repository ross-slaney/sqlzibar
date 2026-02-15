# Sqlzibar

**Hierarchical Role-Based Access Control (RBAC) for .NET + EF Core + SQL Server**

Sqlzibar provides a complete authorization system that plugs into any EF Core application with minimal configuration. It uses a resource hierarchy with SQL Server table-valued functions (TVFs) to deliver fast, composable authorization queries that work directly inside your LINQ-to-SQL pipelines.

📄 **[Paper: SHRBAC](paper/shrbac-compsac-2026.pdf)** — Hierarchical RBAC with SQL Server TVFs.

## Features

- **Hierarchical Resource Model** — Resources form a tree. Grants on parent resources cascade to all descendants.
- **TVF-Based Authorization** — A SQL Server inline TVF (`fn_IsResourceAccessible`) enables single-query authorization via `CROSS APPLY`, eliminating N+1 authorization checks.
- **Minimal Consumer API** — One interface method + one line in `OnModelCreating`. No DbSet properties needed.
- **Group Membership** — Users and service accounts can belong to groups. Group grants apply to all members.
- **Time-Bounded Grants** — Optional `EffectiveFrom` / `EffectiveTo` on grants for temporary access.
- **Configurable Schema** — Custom table names, schema, and root resource.
- **Built-in Dashboard** — Embedded web UI at `/sqlzibar` to browse resources, principals, grants, roles, and permissions.
- **Detailed Access Tracing** — Full diagnostic trace of why access was granted or denied (resource path walk, grants checked, roles evaluated).
- **Specification Executor** — Paginated, authorized queries with cursor pagination out of the box.

## Quick Start

### 1. Install

During development as a project reference:

```xml
<ProjectReference Include="path/to/Sqlzibar/src/Sqlzibar/Sqlzibar.csproj" />
```

### 2. Add to Your DbContext

Implement `ISqlzibarDbContext` on your existing DbContext. You only need **one method** — no DbSet properties required:

```csharp
using Sqlzibar.Interfaces;
using Sqlzibar.Models;
using Sqlzibar.Extensions;

public class AppDbContext : DbContext, ISqlzibarDbContext
{
    // Your own entities — no Sqlzibar DbSets needed
    public DbSet<Project> Projects => Set<Project>();

    // Required: TVF method for composable authorization queries (3 lines)
    public IQueryable<SqlzibarAccessibleResource> IsResourceAccessible(
        string resourceId, string principalIds, string permissionId)
        => FromExpression(() => IsResourceAccessible(resourceId, principalIds, permissionId));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // One line: configures all Sqlzibar tables + registers the TVF
        modelBuilder.ApplySqlzibarModel(GetType());

        // Your own entity configurations...
    }
}
```

Sqlzibar accesses its own entities internally via `Set<T>()` — that's why no DbSet properties are needed on your context.

### 3. Register Services

```csharp
// Program.cs
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));

builder.Services.AddSqlzibar<AppDbContext>(options =>
{
    options.Schema = "dbo";
    options.RootResourceId = "portal_root";
    options.RootResourceName = "Portal Root";
});

var app = builder.Build();

// Initialize TVF + seed core data (principal types, root resource)
await app.UseSqlzibarAsync();

// Optional: mount the dashboard
app.UseSqlzibarDashboard("/sqlzibar");
```

### 4. Seed Your Authorization Data

```csharp
using var scope = app.Services.CreateScope();
var seeder = scope.ServiceProvider.GetRequiredService<SqlzibarSeedService>();

await seeder.SeedAuthorizationDataAsync(new SqlzibarSeedData
{
    ResourceTypes = [
        new() { Id = "agency", Name = "Agency" },
        new() { Id = "project", Name = "Project" },
    ],
    Roles = [
        new() { Id = "role_admin", Key = "Admin", Name = "Administrator" },
        new() { Id = "role_viewer", Key = "Viewer", Name = "Viewer" },
    ],
    Permissions = [
        new() { Id = "perm_view", Key = "PROJECT_VIEW", Name = "View Projects" },
        new() { Id = "perm_edit", Key = "PROJECT_EDIT", Name = "Edit Projects" },
    ],
    RolePermissions = [
        ("Admin", ["PROJECT_VIEW", "PROJECT_EDIT"]),
        ("Viewer", ["PROJECT_VIEW"]),
    ]
});
```

## How It Works

### Resource Hierarchy

Resources form a tree rooted at a configurable root node:

```
portal_root
  ├── agency:acme
  │   ├── project:website
  │   └── project:mobile_app
  └── agency:globex
      └── project:dashboard
```

A grant at `agency:acme` with role `Admin` gives the principal admin access to `project:website` and `project:mobile_app` — the TVF walks up the ancestor chain for any target resource.

### Authorization Model

| Concept        | Description                                                          |
| -------------- | -------------------------------------------------------------------- |
| **Principal**  | A user, group, or service account that can receive grants            |
| **Resource**   | A node in the hierarchy (agency, project, team, etc.)                |
| **Grant**      | Links a principal to a resource with a role, optionally time-bounded |
| **Role**       | A named set of permissions (e.g., Admin, Viewer)                     |
| **Permission** | A specific capability (e.g., `PROJECT_EDIT`)                         |

### TVF: `fn_IsResourceAccessible`

The core of Sqlzibar's performance. This inline TVF runs entirely in SQL Server:

```sql
-- Walks up the resource hierarchy (recursive CTE, max 10 levels)
-- Checks grants for any of the principal's IDs (user + groups)
-- Validates role-permission mappings
-- Respects time-bounded grants (EffectiveFrom/EffectiveTo)
-- Returns a single row if accessible, empty if not
```

EF Core composes this into your LINQ queries via `CROSS APPLY`, meaning authorization filtering happens in the same SQL query as your data fetch — no round trips.

## Authorization Patterns

### Read Authorization (TVF-Based)

For paginated, authorized queries, implement `IHasResourceId` on your entity and use the specification executor:

```csharp
public class Project : IHasResourceId
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string ResourceId { get; set; } = string.Empty;
}

// In your handler:
var spec = new GetProjectsSpecification(pageSize, search) { Cursor = cursor };
var result = await specificationExecutor.ExecuteAsync(
    context.Projects, spec, principalId, MapToDto, ct);
```

Or get a raw filter expression:

```csharp
var filter = await authService.GetAuthorizationFilterAsync<Project>(
    principalId, "PROJECT_VIEW");
var projects = await context.Projects.Where(filter).ToListAsync();
```

### Write Authorization (Hierarchy Walk)

For mutation operations, check access explicitly:

```csharp
var result = await authService.CheckAccessAsync(
    principalId, "PROJECT_EDIT", project.ResourceId);

if (!result.Allowed)
    return Forbid();
```

### Capability Check (Root-Level)

Check if a principal has a permission at the root level (e.g., system admin):

```csharp
bool isAdmin = await authService.HasCapabilityAsync(principalId, "ADMIN_ACCESS");
```

### Access Tracing

Get a detailed diagnostic trace of any access decision:

```csharp
var trace = await authService.TraceResourceAccessAsync(
    principalId, resourceId, "PROJECT_EDIT");

// trace.AccessGranted
// trace.PathNodes — each ancestor with grants found
// trace.GrantsUsed — which grants contributed
// trace.DecisionSummary — human-readable explanation
// trace.DenialReason / trace.Suggestion — for denied access
```

## Principal Management

```csharp
var principalService = services.GetRequiredService<ISqlzibarPrincipalService>();

// Create a principal
var principal = await principalService.CreatePrincipalAsync(
    displayName: "Alice Smith",
    principalTypeId: "user");

// Create a group
var group = await principalService.CreateGroupAsync(
    name: "Engineering Team",
    description: "All engineers");

// Add to group (only users/service accounts — groups cannot contain groups)
await principalService.AddToGroupAsync(principal.Id, group.Id);

// Resolve all IDs for authorization (user + their groups)
List<string> allIds = await principalService.ResolvePrincipalIdsAsync(principal.Id);
// Returns: [principal.Id, group.PrincipalId]
```

> **No nested groups.** Only principals of type `user` or `service_account` can be members of groups. Attempting to add a group to another group throws `InvalidOperationException`.

## Data Access & Integration

Your DbContext has full EF Core access to all Sqlzibar tables. You can query them directly, use `Include` for navigation, and link your own entities to Sqlzibar entities via foreign keys.

### Querying Sqlzibar Entities

```csharp
// Query principals directly
var principal = await _context.Set<SqlzibarPrincipal>()
    .Include(p => p.Grants)
    .FirstOrDefaultAsync(p => p.Id == principalId);

// Include Sqlzibar data when querying your entities
var user = await _context.AuthUsers
    .Include(u => u.Principal)  // Principal is SqlzibarPrincipal
    .FirstOrDefaultAsync(u => u.Email == email);
```

### Creating a User (Principal + Your Entity)

When creating a new user, create the Sqlzibar principal first, then your user entity with the principal reference:

```csharp
// 1. Create the Sqlzibar principal (or use ISqlzibarPrincipalService.CreatePrincipalAsync)
var principal = new SqlzibarPrincipal
{
    Id = $"prin_{Guid.NewGuid():N}",
    PrincipalTypeId = "user",
    DisplayName = $"{firstName} {lastName}",
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};
_context.Set<SqlzibarPrincipal>().Add(principal);

// 2. Create your user entity with FK to the principal
var user = new AuthUser
{
    Id = Guid.NewGuid().ToString(),
    Email = email,
    PrincipalId = principal.Id,
    FirstName = firstName,
    LastName = lastName,
    // ...
};
_context.AuthUsers.Add(user);

await _context.SaveChangesAsync();
```

### Linking Your Entities to Sqlzibar

Your entities can reference Sqlzibar entities via foreign keys. Configure the relationship in `OnModelCreating`:

```csharp
entity.HasOne(e => e.Principal)
    .WithMany()
    .HasForeignKey(e => e.PrincipalId)
    .OnDelete(DeleteBehavior.Restrict);
```

Sqlzibar tables are created by the library (via schema initialization). Once they exist, you use them like any other EF Core entity set.

## Configuration

```csharp
builder.Services.AddSqlzibar<AppDbContext>(options =>
{
    // Database schema (default: "dbo")
    options.Schema = "auth";

    // Root resource configuration
    options.RootResourceId = "my_root";
    options.RootResourceName = "Application Root";

    // Auto-initialization (default: true for both)
    options.InitializeFunctions = true;  // Create/update TVF on startup
    options.SeedCoreData = true;         // Seed principal types + root resource

    // Dashboard path (default: "/sqlzibar")
    options.DashboardPathPrefix = "/admin/auth";

    // Custom table names
    options.TableNames.Resources = "AuthResources";
    options.TableNames.Grants = "AuthGrants";
    options.TableNames.Roles = "AuthRoles";
    options.TableNames.Permissions = "AuthPermissions";
    options.TableNames.RolePermissions = "AuthRolePermissions";
    options.TableNames.Principals = "AuthPrincipals";
    options.TableNames.PrincipalTypes = "AuthPrincipalTypes";
    options.TableNames.UserGroups = "AuthUserGroups";
    options.TableNames.UserGroupMemberships = "AuthUserGroupMemberships";
    options.TableNames.ResourceTypes = "AuthResourceTypes";
    options.TableNames.ServiceAccounts = "AuthServiceAccounts";
});
```

## Dashboard

Sqlzibar includes a built-in web dashboard served as embedded static files. Mount it with:

```csharp
app.UseSqlzibarDashboard("/sqlzibar");
```

**By default the dashboard is only accessible in Development environments.** In production, return 404 unless you configure an authorization callback:

```csharp
app.UseSqlzibarDashboard("/sqlzibar", dashboard =>
{
    dashboard.AuthorizationCallback = async httpContext =>
    {
        // Your custom auth logic here
        return httpContext.User.IsInRole("Admin");
    };
});
```

The dashboard provides:

- **Resources** — Lazy-loading hierarchical tree view with paginated children
- **Principals** — Tabbed by type (users, groups, service accounts), click into a principal to see detail page with info, group memberships, and paginated role grants
- **Grants** — Principal + role + resource with effective dates
- **Roles** — With expandable permission lists (click a role to see its permissions)
- **Permissions** — All permissions with resource type associations
- **Access Tester** — Interactive tool to trace access decisions (see [Demo Scenarios](#access-tester-demo-scenarios))
- **Stats** — Summary counts

All table views support pagination and search.

### Dashboard API Endpoints

| Endpoint                                                            | Description                                                     |
| ------------------------------------------------------------------- | --------------------------------------------------------------- |
| `GET /sqlzibar/api/stats`                                           | Summary counts                                                  |
| `GET /sqlzibar/api/resources/tree?maxDepth=2`                       | Resource tree (breadth-first, configurable depth)               |
| `GET /sqlzibar/api/resources/{id}/children?page=1&pageSize=50`      | Paginated children of a resource                                |
| `GET /sqlzibar/api/principals?type=user&page=1&pageSize=25&search=` | Principals filtered by type, paginated                          |
| `GET /sqlzibar/api/principals/{id}`                                 | Principal detail with group memberships and members             |
| `GET /sqlzibar/api/principals/{id}/grants?page=1&pageSize=25`       | Paginated grants for a principal                                |
| `GET /sqlzibar/api/grants?page=1&pageSize=25&search=`               | All grants with joined names, paginated                         |
| `GET /sqlzibar/api/roles?page=1&pageSize=25&search=`                | Roles with permission counts, paginated                         |
| `GET /sqlzibar/api/roles/{id}/permissions`                          | Permissions for a role                                          |
| `GET /sqlzibar/api/permissions?page=1&pageSize=25&search=`          | All permissions, paginated                                      |
| `POST /sqlzibar/api/trace`                                          | Access trace (body: `{principalId, resourceId, permissionKey}`) |

## Example: Retail API

A complete example application lives in `examples/Sqlzibar.Example.Api/`. It models a retail company with chains, locations, and inventory items.

### Running the Example

Requires a SQL Server instance (e.g., via Docker):

```bash
cd examples/Sqlzibar.Example.Api
dotnet run
```

- Swagger UI: `http://localhost:5000/swagger`
- Dashboard: `http://localhost:5000/sqlzibar/`

> **If you change the seed data**, drop the database first — the seed service skips if data already exists:
>
> ```bash
> sqlcmd -S 127.0.0.1,1433 -U sa -P 'YourPassword' -Q "DROP DATABASE example"
> ```

### Seeded Resource Hierarchy

```
retail_root (CompanyAdmin)
  ├── Walmart (ChainManager Walmart, Walmart Regional Managers group)
  │     ├── Store 001 (StoreManager 001, StoreClerk 001)
  │     │     ├── ProBook Laptop
  │     │     └── SmartPhone X
  │     └── Store 002 (StoreManager 002)
  │           └── TabPro 11
  └── Target (ChainManager Target)
        └── Store 100
              └── BassMax Headphones
```

### Seeded Principals

| Principal                 | Type  | Direct Grant                  | Scope                                 |
| ------------------------- | ----- | ----------------------------- | ------------------------------------- |
| Company Admin             | user  | CompanyAdmin @ retail_root    | Everything                            |
| Walmart Chain Manager     | user  | ChainManager @ Walmart        | Walmart + all descendants             |
| Target Chain Manager      | user  | ChainManager @ Target         | Target + all descendants              |
| Store 001 Manager         | user  | StoreManager @ Store 001      | Store 001 + its inventory             |
| Store 002 Manager         | user  | StoreManager @ Store 002      | Store 002 + its inventory             |
| Store 001 Clerk           | user  | StoreClerk @ Store 001        | Store 001 + its inventory (view only) |
| No Grants User            | user  | _(none)_                      | Nothing                               |
| Walmart Regional Managers | group | ChainManager @ Walmart        | Walmart + all descendants             |
| Alice (Regional)          | user  | _(none — inherits via group)_ | Walmart + all descendants             |
| Bob (Regional)            | user  | _(none — inherits via group)_ | Walmart + all descendants             |

### Access Tester Demo Scenarios

Open the dashboard at `/sqlzibar/` and navigate to **Access Tester**. These scenarios demonstrate different authorization behaviors with the seeded data:

#### 1. Group Inheritance — User inherits access via group membership

| Field      | Value            |
| ---------- | ---------------- |
| Principal  | Alice (Regional) |
| Permission | CHAIN_VIEW       |
| Resource   | Walmart          |

**Expected: ACCESS GRANTED.** Alice has no direct grants. The trace shows principal resolution found her membership in the "Walmart Regional Managers" group, which has a ChainManager grant on Walmart. ChainManager includes CHAIN_VIEW.

#### 2. Hierarchy Cascade — Root grant gives access to everything

| Field      | Value          |
| ---------- | -------------- |
| Principal  | Company Admin  |
| Permission | INVENTORY_VIEW |
| Resource   | Laptop         |

**Expected: ACCESS GRANTED.** Company Admin's grant is at `retail_root`. The trace walks up from Laptop → Store 001 → Walmart → retail_root and finds the CompanyAdmin role grant, which includes INVENTORY_VIEW.

#### 3. Cross-Chain Isolation — Grant on one chain doesn't leak to another

| Field      | Value                 |
| ---------- | --------------------- |
| Principal  | Walmart Chain Manager |
| Permission | CHAIN_VIEW            |
| Resource   | Target                |

**Expected: ACCESS DENIED.** The trace walks up from Target → retail_root, checking for grants from this principal at each level. The ChainManager grant is on Walmart, not on Target or retail_root.

#### 4. Permission Boundary — Having a role doesn't mean having all permissions

| Field      | Value           |
| ---------- | --------------- |
| Principal  | Store 001 Clerk |
| Permission | INVENTORY_EDIT  |
| Resource   | Laptop          |

**Expected: ACCESS DENIED.** The clerk has a StoreClerk grant at Store 001 which covers the Laptop resource, but StoreClerk only includes INVENTORY_VIEW, not INVENTORY_EDIT. The trace shows the grant is found but the permission doesn't match.

#### 5. Store-Level Scoping — Store manager can't see sibling store's data

| Field      | Value             |
| ---------- | ----------------- |
| Principal  | Store 001 Manager |
| Permission | INVENTORY_VIEW    |
| Resource   | Tablet            |

**Expected: ACCESS DENIED.** The Tablet is under Store 002, and Store 001 Manager's grant is at Store 001. Walking up from Tablet → Store 002 → Walmart → retail_root finds no grants for this principal at any ancestor.

#### 6. Group Isolation — Non-member doesn't inherit group access

| Field      | Value          |
| ---------- | -------------- |
| Principal  | No Grants User |
| Permission | CHAIN_VIEW     |
| Resource   | Walmart        |

**Expected: ACCESS DENIED.** This user has no direct grants and no group memberships. The trace shows only one principal was checked (the user themselves), with no grants found at any level.

## Testing

### Unit Tests (InMemory)

For unit tests with EF Core InMemory provider, use the overload **without** `GetType()` to skip TVF registration (TVFs require real SQL Server):

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplySqlzibarModel(); // No GetType() — skips TVF registration
}
```

### Integration Tests (Real SQL Server)

Sqlzibar's integration tests use .NET Aspire to spin up a real SQL Server container. The `TestSqlzibarDbContext` uses the full registration including TVF:

```csharp
public class TestSqlzibarDbContext : DbContext, ISqlzibarDbContext
{
    public IQueryable<SqlzibarAccessibleResource> IsResourceAccessible(
        string resourceId, string principalIds, string permissionId)
        => FromExpression(() => IsResourceAccessible(resourceId, principalIds, permissionId));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplySqlzibarModel(GetType()); // With TVF registration
    }
}
```

### Running Tests

**Docker must be running** for integration tests. Aspire spins up a SQL Server Linux container automatically — no manual setup required.

```bash
# Unit tests (no Docker needed)
dotnet test tests/Sqlzibar.Tests/

# Integration tests (requires Docker)
dotnet test tests/Sqlzibar.IntegrationTests/

# Example project unit tests (no Docker needed)
dotnet test examples/Sqlzibar.Example.Tests/

# Example project integration tests (requires Docker)
dotnet test examples/Sqlzibar.Example.IntegrationTests/
```

## API Reference

### ISqlzibarAuthService

```csharp
// Check if principal has permission on a resource (hierarchy walk)
Task<SqlzibarAccessCheckResult> CheckAccessAsync(
    string principalId, string permissionKey, string resourceId);

// Check if principal has permission at root level
Task<bool> HasCapabilityAsync(string principalId, string permissionKey);

// Get LINQ filter expression for authorized entities
Task<Expression<Func<T, bool>>> GetAuthorizationFilterAsync<T>(
    string principalId, string permissionKey) where T : IHasResourceId;

// Detailed access trace for diagnostics
Task<SqlzibarResourceAccessTrace> TraceResourceAccessAsync(
    string principalId, string resourceId, string permissionKey);
```

### ISqlzibarPrincipalService

```csharp
Task<SqlzibarPrincipal> CreatePrincipalAsync(
    string displayName, string principalTypeId,
    string? organizationId = null, string? externalRef = null,
    CancellationToken cancellationToken = default);

Task<SqlzibarUserGroup> CreateGroupAsync(
    string name, string? description = null, string? groupType = null,
    CancellationToken cancellationToken = default);

Task AddToGroupAsync(string principalId, string userGroupId, CancellationToken ct = default);
Task RemoveFromGroupAsync(string principalId, string userGroupId, CancellationToken ct = default);
Task<List<string>> ResolvePrincipalIdsAsync(string principalId, CancellationToken ct = default);
Task<List<SqlzibarUserGroup>> GetGroupsForPrincipalAsync(string principalId, CancellationToken ct = default);
```

### ISpecificationExecutor

```csharp
// Cursor-paginated, authorized query
Task<PaginatedResult<TDto>> ExecuteAsync<TEntity, TDto>(
    DbSet<TEntity> dbSet,
    PagedSpecification<TEntity> specification,
    string principalId,
    Func<TEntity, TDto> selector,
    CancellationToken cancellationToken = default)
    where TEntity : class, IHasResourceId;

// Total count of matching entities (WARNING: runs COUNT(*) — can be slow at scale)
Task<long> CountAsync<TEntity>(
    DbSet<TEntity> dbSet,
    PagedSpecification<TEntity> specification,
    string principalId,
    CancellationToken cancellationToken = default)
    where TEntity : class, IHasResourceId;
```

## Schema Versioning

Sqlzibar creates and upgrades its own database tables automatically via `UseSqlzibarAsync()`. Consumers do **not** need to write EF migrations for Sqlzibar tables.

On startup, the schema initializer:

1. Checks the `SqlzibarSchema` version table
2. If the database is fresh, runs the initial schema creation script
3. If the version is behind, runs upgrade scripts in order
4. All scripts are idempotent (`IF NOT EXISTS`), so they are safe to run on existing databases

Sqlzibar tables are automatically excluded from EF migrations via `ExcludeFromMigrations()`. This means EF can still query Sqlzibar entities, but `dotnet ef migrations add` will not generate migration code for them. Sqlzibar always manages its own schema via raw SQL.

> **Important:** Because consumer tables often have foreign keys to Sqlzibar tables (e.g., an `Agency` referencing `SqlzibarResources`), `UseSqlzibarAsync()` must be called **before** `Database.Migrate()` in your startup so the referenced tables exist when the migration runs.

```csharp
// 1. Sqlzibar creates its own tables first
await app.UseSqlzibarAsync();

// 2. Then EF migrations create consumer tables (which may FK to Sqlzibar tables)
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
db.Database.Migrate();
```

For library maintainers: see [docs/SCHEMA_CHANGES.md](docs/SCHEMA_CHANGES.md) for how to add new schema versions.

## Architecture

```
src/Sqlzibar/
├── Configuration/       # SqlzibarOptions, SqlzibarModelConfiguration
├── Dashboard/           # Middleware, endpoints, embedded wwwroot/
├── Extensions/          # AddSqlzibar, UseSqlzibarAsync, ApplySqlzibarModel
├── Interfaces/          # ISqlzibarDbContext, ISqlzibarAuthService, etc.
├── Models/              # All entity models (14 files)
├── Schema/              # Versioned SQL scripts (001_Initial.sql, etc.)
├── Services/            # AuthService, PrincipalService, SeedService, SchemaInitializer, etc.
└── Specifications/      # PagedSpecification, PaginatedResult
```

## Performance

Sqlzibar's TVF-based authorization is designed for **predictable, bounded latency** regardless of dataset size. The benchmark suite validates this across 10 test dimensions.

### Key Performance Characteristics

| Property               | Behavior                                            | Evidence                                                         |
| ---------------------- | --------------------------------------------------- | ---------------------------------------------------------------- |
| **Page fetch time**    | O(k) where k = page size, NOT O(N)                  | 3ms at 1K entities, 3ms at 1.2M entities (k=20)                  |
| **Cursor depth**       | Constant — page 500 same speed as page 1            | 3.02ms → 2.74ms from page 1 to page 500 (120K accessible rows)   |
| **Hierarchy depth**    | Negligible impact (CTE bounded at 10 levels)        | 2.00–2.83ms across D=1 to D=5                                    |
| **Access scope**       | Narrower scope = same or faster                     | 3.25ms (full access) vs 1.83ms (single store)                    |
| **Point access check** | ~1ms, independent of total resource count           | 0.98ms at root, 1.69ms at depth=4 (45K resources, 1.2M entities) |
| **Grant set size**     | Negligible impact on point checks                   | 1.07–1.48ms across 1–20 grants                                   |
| **COUNT(\*)**          | O(N) — catastrophic at scale, intentionally avoided | 184ms at 10K → 1,998ms at 100K                                   |

### What Drives Query Time

**Factors that matter:**

- **Page size (k)**: Dominant factor. k=10 → ~2ms, k=20 → ~3ms, k=50 → ~6ms, k=100 → ~12ms
- **Principal set size (M)**: Mild impact at M > 10. STRING_SPLIT + join overhead grows with M
- **Grant density**: More grants on a user = slightly more join work per TVF call
- **Adversarial data layout**: Interleaved unauthorized rows force more scanning (~16ms worst case at 10K)

**Factors that don't matter:**

- **Total entity count (N)**: 1K to 1.2M — no meaningful change in page fetch time
- **Total resource count**: 50 resources vs 45K resources — point check time unchanged
- **Cursor depth**: Page 1 vs page 500 — identical performance (no offset scanning)
- **Hierarchy depth**: D=1 to D=5 — bounded CTE with indexed ParentId lookups

### Benchmark Results (Retail SaaS at 1.2M)

Realistic retail company: 10 chains, 50 regions, 5,000 stores, 40,000 departments, 1,200,000 inventory items. 5-level resource hierarchy.

#### Page Size Scaling (company admin, full access)

| Page Size (k) | Median  | P95     | IQR    |
| ------------- | ------- | ------- | ------ |
| k=10          | 2.03ms  | 2.45ms  | 0.13ms |
| k=20          | 3.22ms  | 5.39ms  | 0.30ms |
| k=50          | 6.00ms  | 7.59ms  | 0.52ms |
| k=100         | 11.09ms | 12.87ms | 0.68ms |

Linear with k. Doubling page size roughly doubles query time.

#### Access Scope (k=20)

| User           | Scope      | Accessible Rows | Median |
| -------------- | ---------- | --------------- | ------ |
| Company Admin  | Everything | 1,200,000       | 3.25ms |
| Chain Manager  | 1 chain    | ~120,000        | 2.76ms |
| Region Manager | 1 region   | ~24,000         | 2.41ms |
| Store Manager  | 1 store    | ~240            | 1.83ms |

Narrower scope is the same speed or faster.

#### TVF vs Materialized Permissions

| Method              | N=10K  | N=50K  | N=100K |
| ------------------- | ------ | ------ | ------ |
| TVF EXISTS (cursor) | 3.43ms | 2.73ms | 2.70ms |
| Materialized JOIN   | 1.30ms | 1.08ms | 0.91ms |

TVF is ~2-3x slower than pre-materialized permissions, but requires zero maintenance on grant changes.

#### Point Access Check (Benchmark 9)

Single-resource TVF call — the SQL equivalent of `CheckAccessAsync()`. Tests on the 1.2M entity / 45K resource tree.

| Dimension         | Config          | Median | P95    |
| ----------------- | --------------- | ------ | ------ |
| Hierarchy depth   | root (0 hops)   | 0.98ms | 1.23ms |
| Hierarchy depth   | chain (1 hop)   | 0.98ms | 1.35ms |
| Hierarchy depth   | region (2 hops) | 1.02ms | 1.51ms |
| Hierarchy depth   | store (3 hops)  | 1.43ms | 2.76ms |
| Hierarchy depth   | dept (4 hops)   | 1.69ms | 3.16ms |
| Grant set size    | 1 grant         | 1.48ms | 3.13ms |
| Grant set size    | 5 grants        | 1.07ms | 1.52ms |
| Grant set size    | 10 grants       | 1.11ms | 2.48ms |
| Grant set size    | 20 grants       | 1.29ms | 1.66ms |
| Principal set (M) | M=1             | 1.11ms | 1.46ms |
| Principal set (M) | M=3             | 1.00ms | 1.47ms |
| Principal set (M) | M=6             | 1.06ms | 1.46ms |
| Principal set (M) | M=11            | 1.44ms | 1.66ms |
| Principal set (M) | M=21            | 1.79ms | 2.92ms |

Point checks are sub-2ms in all realistic scenarios. Grant set size has no meaningful impact (the CTE only walks 5 ancestors, so only grants at those specific nodes are checked). Principal set size causes mild degradation above M=10 from STRING_SPLIT overhead.

#### Dimensional Analysis at 1.2M (Benchmark 10)

| Dimension                  | Config                    | Median | P95    |
| -------------------------- | ------------------------- | ------ | ------ |
| Principal set (list query) | M=1                       | 3.12ms | 4.43ms |
| Principal set (list query) | M=3                       | 3.34ms | 4.50ms |
| Principal set (list query) | M=6                       | 3.15ms | 3.78ms |
| Principal set (list query) | M=11                      | 3.25ms | 3.94ms |
| Grant density              | 1 chain (~120K rows)      | 2.61ms | 3.38ms |
| Grant density              | 3 chains (~360K rows)     | 3.00ms | 3.85ms |
| Grant density              | 5 chains (~600K rows)     | 2.85ms | 4.03ms |
| Grant density              | 10 chains (all 1.2M)      | 3.19ms | 7.73ms |
| Sparse access              | chain grant (inherit all) | 2.75ms | 4.14ms |
| Sparse access              | 8/8 depts (explicit)      | 1.60ms | 2.21ms |
| Sparse access              | 2/8 depts (25%)           | 1.35ms | 2.13ms |
| Sparse access              | 1/8 depts (12.5%)         | 1.53ms | 1.86ms |

Key findings: Principal set size has negligible impact on list queries at million-scale (all ~3ms). Grant density shows mild increase from 1→10 chains but stays under 4ms. Department-level grants are actually faster than chain-level grants because the TVF finds matching grants sooner (closer in the ancestor walk).

### Running Benchmarks

Requires a SQL Server instance (the Aspire integration test container works):

```bash
cd libraries/Sqlzibar/tests/Sqlzibar.Benchmarks
dotnet run

# Or with a custom connection string:
dotnet run -- "Server=localhost,1433;Database=Sqlzibar_Benchmark;User Id=sa;Password=YourPassword;TrustServerCertificate=True;"
```

The full suite takes ~25 minutes (mostly seeding 1.2M entities). Benchmarks 1-7 run on small datasets (seconds). Benchmarks 8-10 share a single 1.2M-entity seed to avoid re-seeding.

### Benchmark Suite Overview

| #   | Benchmark            | Dimension                    | Scale     |
| --- | -------------------- | ---------------------------- | --------- |
| 1   | Entity Count Scaling | Total entities (N)           | 1K–100K   |
| 2   | Hierarchy Depth      | Resource tree depth (D)      | D=1–5     |
| 3   | Principal Set Size   | IDs in authorization (M)     | M=1–21    |
| 4   | Selectivity          | Dense vs sparse access (σ)   | σ=0.1–1.0 |
| 5   | Adversarial Layout   | Worst-case data interleaving | 10K       |
| 6   | TVF vs Materialized  | Authorization strategy       | 10K–100K  |
| 7   | Pagination Strategy  | Cursor vs offset vs COUNT    | 10K–100K  |
| 8   | Retail SaaS          | Realistic million-scale      | 1.2M      |
| 9   | Point Access Check   | Single-resource TVF call     | 1.2M      |
| 10  | Dimensional Analysis | Factor isolation at scale    | 1.2M      |

## Requirements

- .NET 9.0
- SQL Server (for TVF support)
- EF Core 9.0
- Docker (for integration tests via Aspire)

## Helpful Prompt:

```
You are helping me model authorization for an application using Sqlzibar (SHRBAC).

SHRBAC rules (must follow):
- Resources form a rooted TREE (single parent; no DAG).
- Grants: (principal, role, resource_scope, optional time window).
- Roles are flat; roles map to atomic permissions.
- Sharing/collab is done by EXTRA GRANTS, not multiple parents.
- List filtering must be expressible as: WHERE EXISTS(IsResourceAccessible(row.ResourceId, principalIds, perm, now)).

IMPORTANT STYLE REQUIREMENTS (follow these strictly):
1) Keep it simple and readable: aim for ~1 page of output.
2) Use AT MOST 6 resource types total. Prefer fewer.
3) Do NOT introduce “logical leaves” unless absolutely necessary.
   - Default rule: every database row type that needs authorization has a ResourceId.
   - Only map a table to a parent ResourceId if there are millions of rows and per-row ACL is not needed.
4) Use AT MOST 8 permissions total (atomic verbs), and AT MOST 5 roles.
5) Prefer 1–2 canonical sharing patterns (e.g., "joint account is its own subtree" + "share by grant on the resource").
6) Output should be “starter architecture” someone can build immediately.

TASK:
Given the app description below, output a SHRBAC model design.

OUTPUT FORMAT (exact):
1) One-paragraph summary of the authorization approach for this app.
2) Resource Tree (ASCII) with 3–6 levels max.
3) Tables → ResourceId mapping (bullets).
4) Roles and permissions (small table or bullets).
5) Sharing/joint ownership modeling (2–4 bullets).
6) Three example queries:
   - list endpoint (authorized + paged)
   - get one (point check)
   - mutation (create/update with check)
7) 5 quick “design rules” for the dev team (short bullets).

NOW model this app:

[PASTE APP SUMMARY HERE]

```

## Sample Personal Finance App

This section shows how to model a personal finance app (banking, brokerage, joint accounts) with SHRBAC. The key insight: **joint ownership is expressed by grants, not by multiple parents** — keeping the resource tree a true tree.

### 1. Resource Tree

Use a single containment tree rooted at the tenant/org (or "platform") boundary, then hang users and accounts under it.

**Example resource types (RT):**

- `platform_root`
- `tenant` (optional if multi-tenant B2B; if consumer, tenant can be "household" or just platform)
- `customer` (the user's profile/identity "space")
- `portfolio` (brokerage portfolio)
- `account` (savings, checking, brokerage sub-account)
- `subaccount` (optional: cash, margin, positions)
- `instrument` (optional)
- `transaction`
- `statement`
- `beneficiary` / `payee`
- `transfer`

**Example tree:**

```
platform_root
  └── tenant/{tenantId}                       (or omit tenant for pure consumer)
      ├── customer/{aliceCustomerId}
      │    ├── portfolio/{alicePortId}
      │    │    ├── account/{aliceBrokerageAcctId}
      │    │    ├── positions/{...}
      │    │    └── statements/{...}
      │    └── profile/{...}
      ├── customer/{bobCustomerId}
      │    └── ...
      └── joint_account/{jointSavingsId}
           ├── transactions/{...}
           ├── statements/{...}
           └── beneficiaries/{...}
```

**Key point:** The joint account is its own subtree. It is not "under Alice" or "under Bob." That keeps containment a tree and makes joint ownership just… grants.

### 2. Principals and Joint Accounts

**Principals:**

- **User principals:** AliceUserId, BobUserId
- **Group principals** (optional): e.g., `household/{id}`, `tenant_admins/{id}`
- **Agent principals:** robo-advisor, reconciliation worker, statement generator

**Joint ownership pattern**

For joint savings, you do two grants to the joint account node:

- `(AliceUserId, JOINT_OWNER, joint_account/{id}, [tfrom,tto])`
- `(BobUserId, JOINT_OWNER, joint_account/{id}, [tfrom,tto])`

No DAG needed. No multi-parenting.

### 3. Roles and Permissions

Keep roles flat and permissions atomic per resource type.

**Savings / banking permissions**

| Permission              | Description                    |
| ----------------------- | ------------------------------ |
| `ACCOUNT_VIEW`          | View account                   |
| `ACCOUNT_DEPOSIT`       | Deposit funds                  |
| `ACCOUNT_WITHDRAW`      | Withdraw funds                 |
| `ACCOUNT_TRANSFER_OUT`  | Transfer out                   |
| `ACCOUNT_MANAGE_PAYEES` | Manage payees/beneficiaries    |
| `ACCOUNT_CLOSE`         | Close account                  |

**Brokerage permissions**

| Permission           | Description              |
| -------------------- | ------------------------ |
| `PORTFOLIO_VIEW`     | View portfolio           |
| `TRADE_PLACE_ORDER`  | Place orders             |
| `TRADE_CANCEL_ORDER` | Cancel orders            |
| `VIEW_STATEMENTS`    | View statements          |
| `DOWNLOAD_TAX_DOCS`  | Download tax documents   |

**Admin / support permissions** (careful)

| Permission               | Description                                      |
| ------------------------ | ------------------------------------------------ |
| `SUPPORT_READ_ONLY`      | Read-only support access                         |
| `SUPPORT_LIMITED_ACTIONS`| Limited actions (e.g., reset 2FA, reissue statement) |
| `COMPLIANCE_AUDIT`       | Compliance audit access                          |

**Roles (R) map to permission sets:**

| Role               | Permissions                                                       |
| ------------------ | ----------------------------------------------------------------- |
| `SAVINGS_OWNER`    | view + withdraw + transfer + manage payees                        |
| `SAVINGS_VIEWER`   | view only                                                         |
| `JOINT_OWNER`      | same as savings owner, maybe with extra constraints               |
| `BROKERAGE_OWNER`  | view + trade + statements                                        |
| `BROKERAGE_VIEWER` | view + statements                                                |
| `SUPPORT_READONLY` | read-only across tenant/customer scopes (but scoped!)             |
| `AGENT_REBALANCER` | trade permissions, tightly scoped + time-bounded                 |

### 4. Common Access Rules as SHRBAC Grants

**Personal (single-owner) savings account**

Put the account under either the customer subtree (simple) or a separate "accounts" subtree under tenant. Either way, grant the owner on the account node:

```
(AliceUserId, SAVINGS_OWNER, account/{aliceSavingsId}, ⊥, ⊥)
```

**Joint savings account**

```
(AliceUserId, JOINT_OWNER, joint_account/{id}, ⊥, ⊥)
(BobUserId, JOINT_OWNER, joint_account/{id}, ⊥, ⊥)
```

If you want "view-only joint holder":

```
(BobUserId, SAVINGS_VIEWER, joint_account/{id}, ⊥, ⊥)
```

**Household-level shared view** (optional)

If you want "household members can view all household accounts" without per-account grants:

1. Create a group principal `household/{hid}`
2. Add users to that group
3. Grant the group at the household root scope (could be tenant child):

```
(household/{hid}, HOUSEHOLD_VIEWER, household_scope/{hid}, ⊥, ⊥)
```

You may not even need household groups if joint is the only sharing mechanism.

### 5. List Filtering Examples

**"List accounts Alice can view"**

Query the Accounts table and filter with the TVF on `Account.ResourceId` + `ACCOUNT_VIEW`. Because joint account is its own subtree and Alice has a grant there, it shows up naturally.

**"List transactions in a joint account"**

Transactions live under the joint account subtree, so the `JOINT_OWNER` grant cascades.

### 6. Edge Cases (Finance-Specific, SHRBAC-Friendly Patterns)

**A) "Authorized users can invite another joint holder"**

That's just a permission like `ACCOUNT_MANAGE_OWNERS`. Grant it only to `JOINT_OWNER` at the joint_account node.

**B) "Power of attorney / advisor access"**

Model advisor as a user or `service_account` principal. Grant them `ADVISOR_VIEWER` on a specific customer subtree or account subtree. Time-bound it.

**C) Break-glass support access**

Create support principals/groups and scope them to tenant root (B2B) or to a "support case scope" node created per case, then grant support there with time bounds.

**D) Compliance/audit**

Auditors should get read-only role, scoped to tenant root or customer root, time-bounded.

### 7. Why This Avoids the "DAG" Trap

You keep containment as the system-of-record tree: **a resource has one parent.**

Sharing is expressed by additional grants. Joint ownership is not "two parents." It's "two principals granted on one resource."

That's exactly what SHRBAC wants.

## License

MIT
