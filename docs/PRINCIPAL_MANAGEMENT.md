# Principal Management & Data Access

## Principals

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
