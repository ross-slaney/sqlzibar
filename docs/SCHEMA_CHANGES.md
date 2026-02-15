# Sqlzibar Schema Changes Guide

This document is for **library maintainers** who need to add, modify, or upgrade the Sqlzibar database schema.

## How Schema Versioning Works

Sqlzibar uses a Hangfire-style raw SQL schema versioning system:

1. A `SqlzibarSchema` table tracks the current schema version (single `Version` int column)
2. Embedded SQL scripts in `src/Sqlzibar/Schema/` define each version
3. On startup, `SqlzibarSchemaInitializer.EnsureSchemaAsync()` runs automatically (via `UseSqlzibarAsync()`)
4. The initializer checks the current version and runs any pending upgrade scripts in order

### Startup Flow

```
UseSqlzibarAsync()
  1. Schema Init  → Check version table → Run pending scripts
  2. Function Init → Create/update fn_IsResourceAccessible TVF
  3. Seed Core     → Seed principal types + root resource
```

### Script Conventions

- Scripts are embedded resources at `src/Sqlzibar/Schema/*.sql`
- Named `NNN_DescriptiveName.sql` (e.g., `001_Initial.sql`, `002_AddEffectiveFromIndex.sql`)
- Use `{Schema}`, `{Principals}`, `{Resources}`, etc. as placeholders (replaced at runtime from `SqlzibarOptions`)
- Use `GO` to separate batches (the initializer splits on `GO` lines)
- All DDL should be idempotent where possible (`IF NOT EXISTS` for new objects, `IF COL_NAME(...)` checks for columns)

## Adding a New Schema Version

1. **Create the script** in `src/Sqlzibar/Schema/`:

   ```
   002_AddGrantIndex.sql
   ```

   Example content:
   ```sql
   IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_{Grants}_PrincipalId_ResourceId')
   BEGIN
       CREATE INDEX [IX_{Grants}_PrincipalId_ResourceId]
           ON [{Schema}].[{Grants}] ([PrincipalId], [ResourceId]);
   END
   GO

   UPDATE [{Schema}].[SqlzibarSchema] SET [Version] = 2;
   ```

2. **Bump `CurrentSchemaVersion`** in `SqlzibarSchemaInitializer.cs`:

   ```csharp
   private const int CurrentSchemaVersion = 2; // was 1
   ```

3. **Add the upgrade path** in `EnsureSchemaAsync()` (in the `currentVersion < CurrentSchemaVersion` branch):

   ```csharp
   if (currentVersion < 2)
       await RunScriptAsync("Sqlzibar.Schema.002_AddGrantIndex.sql", cancellationToken);
   ```

4. **Verify the `.csproj`** includes the script. The glob `<EmbeddedResource Include="Schema\*.sql" />` should pick it up automatically.

5. **Update the model** if needed. If the script adds a column, update the corresponding model class and `SqlzibarModelConfiguration`.

## Local Development Workflow

1. **Fresh database**: Run the app against an empty database. The initializer creates all tables from scratch.

2. **Existing database (previous version)**: Run the app against a database with the previous version. The initializer detects the version gap and runs upgrade scripts.

3. **Testing both paths**: Always test new scripts against:
   - A fresh database (no tables at all)
   - A database at the previous version (upgrade path)

4. **Docker SQL Server** (for integration tests):
   ```bash
   # Tests use Aspire to spin up SQL Server automatically
   dotnet test tests/Sqlzibar.IntegrationTests/
   ```

## Publishing Workflow

1. Bump the NuGet package version
2. Add release notes describing the schema changes
3. Consumers upgrading to the new package version get schema updates automatically on their next app startup — no `dotnet ef migrations add` needed
4. The upgrade is transparent: `UseSqlzibarAsync()` detects the version gap and applies pending scripts

## Script Placeholder Reference

| Placeholder              | Source                             |
|--------------------------|------------------------------------|
| `{Schema}`               | `SqlzibarOptions.Schema`           |
| `{PrincipalTypes}`       | `SqlzibarOptions.TableNames.PrincipalTypes` |
| `{Principals}`           | `SqlzibarOptions.TableNames.Principals` |
| `{UserGroups}`           | `SqlzibarOptions.TableNames.UserGroups` |
| `{UserGroupMemberships}` | `SqlzibarOptions.TableNames.UserGroupMemberships` |
| `{ResourceTypes}`        | `SqlzibarOptions.TableNames.ResourceTypes` |
| `{Resources}`            | `SqlzibarOptions.TableNames.Resources` |
| `{Grants}`               | `SqlzibarOptions.TableNames.Grants` |
| `{Roles}`                | `SqlzibarOptions.TableNames.Roles` |
| `{Permissions}`          | `SqlzibarOptions.TableNames.Permissions` |
| `{RolePermissions}`      | `SqlzibarOptions.TableNames.RolePermissions` |
| `{ServiceAccounts}`      | `SqlzibarOptions.TableNames.ServiceAccounts` |
