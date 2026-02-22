using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sqlzibar.Configuration;
using Sqlzibar.Interfaces;
using Sqlzibar.Models;

namespace Sqlzibar.Services;

public class SqlzibarSeedService
{
    private readonly ISqlzibarDbContext _context;
    private readonly SqlzibarOptions _options;
    private readonly ILogger<SqlzibarSeedService> _logger;

    public SqlzibarSeedService(
        ISqlzibarDbContext context,
        IOptions<SqlzibarOptions> options,
        ILogger<SqlzibarSeedService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedCoreAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Seeding core Sqlzibar data...");

        // Seed principal types
        await SeedIfNotExistsAsync<SqlzibarPrincipalType>("user", new SqlzibarPrincipalType { Id = "user", Name = "User", Description = "A human user" }, cancellationToken);
        await SeedIfNotExistsAsync<SqlzibarPrincipalType>("group", new SqlzibarPrincipalType { Id = "group", Name = "Group", Description = "A user group" }, cancellationToken);
        await SeedIfNotExistsAsync<SqlzibarPrincipalType>("service_account", new SqlzibarPrincipalType { Id = "service_account", Name = "Service Account", Description = "An automated service account" }, cancellationToken);
        await SeedIfNotExistsAsync<SqlzibarPrincipalType>("agent", new SqlzibarPrincipalType { Id = "agent", Name = "Agent", Description = "An automated agent (job, worker, AI)" }, cancellationToken);

        // Seed root resource type
        await SeedIfNotExistsAsync<SqlzibarResourceType>("root", new SqlzibarResourceType { Id = "root", Name = "Root", Description = "The root resource type" }, cancellationToken);

        // Seed root resource
        await SeedIfNotExistsAsync<SqlzibarResource>(_options.RootResourceId, new SqlzibarResource
        {
            Id = _options.RootResourceId,
            Name = _options.RootResourceName,
            ResourceTypeId = "root",
            IsActive = true,
        }, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Core Sqlzibar data seeded.");
    }

    public async Task SeedAuthorizationDataAsync(SqlzibarSeedData data, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Seeding authorization data...");

        if (data.ResourceTypes != null)
        {
            foreach (var rt in data.ResourceTypes)
            {
                await SeedIfNotExistsAsync<SqlzibarResourceType>(rt.Id, rt, cancellationToken);
            }
        }

        if (data.Roles != null)
        {
            foreach (var role in data.Roles)
            {
                await SeedIfNotExistsAsync<SqlzibarRole>(role.Id, role, cancellationToken);
            }
        }

        if (data.Permissions != null)
        {
            foreach (var perm in data.Permissions)
            {
                await SeedIfNotExistsAsync<SqlzibarPermission>(perm.Id, perm, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (data.RolePermissions != null)
        {
            foreach (var (roleKey, permissionKeys) in data.RolePermissions)
            {
                var role = await _context.Set<SqlzibarRole>().FirstOrDefaultAsync(r => r.Key == roleKey, cancellationToken);
                if (role == null)
                {
                    _logger.LogWarning("Role with key {RoleKey} not found for role-permission mapping", roleKey);
                    continue;
                }

                foreach (var permKey in permissionKeys)
                {
                    var perm = await _context.Set<SqlzibarPermission>().FirstOrDefaultAsync(p => p.Key == permKey, cancellationToken);
                    if (perm == null)
                    {
                        _logger.LogWarning("Permission with key {PermKey} not found for role {RoleKey}", permKey, roleKey);
                        continue;
                    }

                    var exists = await _context.Set<SqlzibarRolePermission>()
                        .AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id, cancellationToken);

                    if (!exists)
                    {
                        _context.Set<SqlzibarRolePermission>().Add(new SqlzibarRolePermission
                        {
                            RoleId = role.Id,
                            PermissionId = perm.Id,
                        });
                    }
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("Authorization data seeded.");
    }

    private async Task SeedIfNotExistsAsync<T>(string id, T entity, CancellationToken cancellationToken) where T : class
    {
        var existing = await _context.Set<T>().FindAsync(new object[] { id }, cancellationToken);
        if (existing == null)
        {
            _context.Set<T>().Add(entity);
        }
    }
}

public class SqlzibarSeedData
{
    public List<SqlzibarResourceType>? ResourceTypes { get; set; }
    public List<SqlzibarRole>? Roles { get; set; }
    public List<SqlzibarPermission>? Permissions { get; set; }
    public List<(string RoleKey, string[] PermissionKeys)>? RolePermissions { get; set; }
}
