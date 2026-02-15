using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sqlzibar.Configuration;
using Sqlzibar.Interfaces;
using Sqlzibar.Models;

namespace Sqlzibar.Services;

public class SqlzibarAuthService : ISqlzibarAuthService
{
    private readonly ISqlzibarDbContext _context;
    private readonly SqlzibarOptions _options;
    private readonly ILogger<SqlzibarAuthService> _logger;

    public SqlzibarAuthService(
        ISqlzibarDbContext context,
        IOptions<SqlzibarOptions> options,
        ILogger<SqlzibarAuthService> logger)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> HasCapabilityAsync(string principalId, string permissionKey)
    {
        var result = await CheckAccessAsync(principalId, permissionKey, _options.RootResourceId);
        return result.Allowed;
    }

    public async Task<SqlzibarAccessCheckResult> CheckAccessAsync(
        string principalId,
        string permissionKey,
        string resourceId)
    {
        var trace = new List<SqlzibarAccessTrace>();

        var resource = await _context.Set<SqlzibarResource>()
            .Include(r => r.ResourceType)
            .FirstOrDefaultAsync(r => r.Id == resourceId);

        if (resource == null)
        {
            return new SqlzibarAccessCheckResult { Allowed = false, Trace = trace, Error = $"Resource {resourceId} not found" };
        }

        trace.Add(new SqlzibarAccessTrace
        {
            Step = "Target Resource",
            Detail = $"Checking access to {resource.Name} ({resource.ResourceTypeId})",
            ResourceId = resource.Id,
            ResourceName = resource.Name,
        });

        var permission = await _context.Set<SqlzibarPermission>().FirstOrDefaultAsync(p => p.Key == permissionKey);
        if (permission == null)
        {
            return new SqlzibarAccessCheckResult { Allowed = false, Trace = trace, Error = $"Permission {permissionKey} not found" };
        }

        trace.Add(new SqlzibarAccessTrace
        {
            Step = "Permission",
            Detail = $"Looking for permission: {permission.Name}",
        });

        var allPrincipals = await ResolvePrincipalsAsync(principalId, trace);
        if (allPrincipals.Count == 0)
        {
            return new SqlzibarAccessCheckResult { Allowed = false, Trace = trace, Error = "No principals found" };
        }

        var ancestorResources = await GetAncestorResourcesAsync(resourceId);
        trace.Add(new SqlzibarAccessTrace
        {
            Step = "Resource Path",
            Detail = $"Checking {ancestorResources.Count} resource(s) in hierarchy",
        });

        foreach (var ancestorResource in ancestorResources)
        {
            var resourceType = await _context.Set<SqlzibarResourceType>().FirstOrDefaultAsync(rt => rt.Id == ancestorResource.ResourceTypeId);
            trace.Add(new SqlzibarAccessTrace
            {
                Step = "Checking Resource",
                Detail = $"[{resourceType?.Name ?? "Unknown"}] {ancestorResource.Name}",
                ResourceId = ancestorResource.Id,
                ResourceName = ancestorResource.Name,
            });

            foreach (var pid in allPrincipals)
            {
                var principalData = await _context.Set<SqlzibarPrincipal>().FirstOrDefaultAsync(p => p.Id == pid);
                var grants = await GetActiveGrantsAsync(pid, ancestorResource.Id);

                foreach (var grant in grants)
                {
                    var role = await _context.Set<SqlzibarRole>().FirstOrDefaultAsync(r => r.Id == grant.RoleId);
                    if (role == null) continue;

                    var roleHasPermission = await _context.Set<SqlzibarRolePermission>()
                        .AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id);

                    if (roleHasPermission)
                    {
                        trace.Add(new SqlzibarAccessTrace
                        {
                            Step = "Access Granted",
                            Detail = $"Role \"{role.Name}\" provides permission \"{permission.Name}\" via grant at {ancestorResource.Name}",
                            ResourceId = ancestorResource.Id,
                            ResourceName = ancestorResource.Name,
                            GrantId = grant.Id,
                            RoleName = role.Name,
                            PrincipalName = principalData?.DisplayName,
                        });
                        return new SqlzibarAccessCheckResult { Allowed = true, Trace = trace };
                    }
                    else
                    {
                        trace.Add(new SqlzibarAccessTrace
                        {
                            Step = "Role Checked",
                            Detail = $"Role \"{role.Name}\" does not provide permission \"{permission.Name}\"",
                            RoleName = role.Name,
                        });
                    }
                }
            }
        }

        trace.Add(new SqlzibarAccessTrace
        {
            Step = "Access Denied",
            Detail = $"No matching grants found that provide permission \"{permission.Name}\"",
        });

        return new SqlzibarAccessCheckResult { Allowed = false, Trace = trace };
    }

    public async Task<SqlzibarResourceAccessTrace> TraceResourceAccessAsync(
        string principalId,
        string resourceId,
        string permissionKey)
    {
        var trace = new SqlzibarResourceAccessTrace
        {
            PrincipalId = principalId,
            PermissionKey = permissionKey
        };

        var resource = await _context.Set<SqlzibarResource>()
            .Include(r => r.ResourceType)
            .FirstOrDefaultAsync(r => r.Id == resourceId);

        if (resource == null)
        {
            trace.AccessGranted = false;
            trace.DenialReason = $"Resource '{resourceId}' not found";
            trace.DecisionSummary = $"Access denied because the resource '{resourceId}' does not exist.";
            return trace;
        }

        trace.TargetResourceId = resource.Id;
        trace.TargetResourceName = resource.Name;
        trace.TargetResourceType = resource.ResourceType?.Name ?? resource.ResourceTypeId;

        var principal = await _context.Set<SqlzibarPrincipal>().FirstOrDefaultAsync(p => p.Id == principalId);
        if (principal == null)
        {
            trace.AccessGranted = false;
            trace.DenialReason = $"Principal '{principalId}' not found";
            trace.DecisionSummary = $"Access denied because the principal '{principalId}' does not exist.";
            return trace;
        }

        trace.PrincipalDisplayName = principal.DisplayName;

        var permission = await _context.Set<SqlzibarPermission>().FirstOrDefaultAsync(p => p.Key == permissionKey);
        if (permission == null)
        {
            trace.AccessGranted = false;
            trace.DenialReason = $"Permission '{permissionKey}' not found";
            trace.DecisionSummary = $"Access denied because the permission '{permissionKey}' does not exist.";
            return trace;
        }

        trace.PermissionName = permission.Name;

        var allPrincipals = await ResolvePrincipalsWithInfoAsync(principalId);
        trace.PrincipalsChecked = allPrincipals;

        var principalIds = allPrincipals.Select(p => p.PrincipalId).ToList();
        var ancestorResources = await GetAncestorResourcesAsync(resourceId);
        var pathNodes = new List<SqlzibarResourcePathNodeTrace>();
        var allGrantsUsed = new List<SqlzibarGrantTrace>();
        var allRolesUsed = new Dictionary<string, SqlzibarRoleTrace>();
        var now = DateTime.UtcNow;

        bool accessGranted = false;
        string? grantingNodeName = null;
        string? grantingRoleName = null;
        string? grantingPrincipalName = null;
        bool grantedViaGroup = false;
        string? grantingGroupName = null;

        int depth = 0;
        foreach (var ancestorResource in ancestorResources)
        {
            var resourceType = await _context.Set<SqlzibarResourceType>().FirstOrDefaultAsync(rt => rt.Id == ancestorResource.ResourceTypeId);

            var pathNode = new SqlzibarResourcePathNodeTrace
            {
                ResourceId = ancestorResource.Id,
                Name = ancestorResource.Name,
                ResourceType = resourceType?.Name ?? ancestorResource.ResourceTypeId,
                Depth = depth,
                IsTarget = ancestorResource.Id == resourceId
            };

            var grantsOnNode = await _context.Set<SqlzibarGrant>()
                .Include(g => g.Role)
                .Include(g => g.Principal)
                .Where(g => g.ResourceId == ancestorResource.Id &&
                           principalIds.Contains(g.PrincipalId) &&
                           (g.EffectiveFrom == null || g.EffectiveFrom <= now) &&
                           (g.EffectiveTo == null || g.EffectiveTo >= now))
                .ToListAsync();

            foreach (var grant in grantsOnNode)
            {
                var role = grant.Role;
                if (role == null) continue;

                var rolePermissions = await _context.Set<SqlzibarRolePermission>()
                    .Include(rp => rp.Permission)
                    .Where(rp => rp.RoleId == role.Id)
                    .ToListAsync();

                var hasRequestedPermission = rolePermissions.Any(rp => rp.PermissionId == permission.Id);

                var principalInfo = allPrincipals.FirstOrDefault(p => p.PrincipalId == grant.PrincipalId);
                var isDirect = principalInfo?.IsDirect ?? false;
                var viaGroupName = !isDirect ? principalInfo?.DisplayName : null;

                var grantTrace = new SqlzibarGrantTrace
                {
                    GrantId = grant.Id,
                    ResourceId = ancestorResource.Id,
                    ResourceName = ancestorResource.Name,
                    ResourceType = pathNode.ResourceType,
                    RoleKey = role.Key,
                    RoleName = role.Name,
                    PrincipalId = grant.PrincipalId,
                    PrincipalDisplayName = grant.Principal?.DisplayName ?? grant.PrincipalId,
                    AppliesToPrincipal = true,
                    IsDirectGrant = isDirect,
                    ViaGroupName = viaGroupName,
                    ContributedToDecision = hasRequestedPermission && !accessGranted
                };

                pathNode.GrantsOnThisNode.Add(grantTrace);
                allGrantsUsed.Add(grantTrace);

                if (!allRolesUsed.ContainsKey(role.Id))
                {
                    var roleTrace = new SqlzibarRoleTrace
                    {
                        RoleKey = role.Key,
                        RoleName = role.Name,
                        IsFromGrant = true,
                        IsVirtualRole = role.IsVirtual,
                        SourceResourceId = ancestorResource.Id,
                        SourceResourceName = ancestorResource.Name,
                        SourceResourceType = pathNode.ResourceType,
                        ContributedToDecision = hasRequestedPermission && !accessGranted,
                        Permissions = rolePermissions.Select(rp => new SqlzibarPermissionAssignmentTrace
                        {
                            PermissionKey = rp.Permission?.Key ?? "",
                            PermissionName = rp.Permission?.Name ?? "",
                            UsedForDecision = rp.PermissionId == permission.Id
                        }).ToList()
                    };
                    allRolesUsed[role.Id] = roleTrace;
                }

                foreach (var rp in rolePermissions)
                {
                    if (rp.Permission != null && !pathNode.EffectivePermissions.Contains(rp.Permission.Key))
                    {
                        pathNode.EffectivePermissions.Add(rp.Permission.Key);
                    }
                }

                if (hasRequestedPermission && !accessGranted)
                {
                    accessGranted = true;
                    pathNode.PermissionFoundHere = true;
                    grantingNodeName = ancestorResource.Name;
                    grantingRoleName = role.Name;
                    grantingPrincipalName = grant.Principal?.DisplayName ?? grant.PrincipalId;
                    grantedViaGroup = !isDirect;
                    grantingGroupName = viaGroupName;
                }
            }

            pathNodes.Add(pathNode);
            depth++;
        }

        pathNodes.Reverse();
        for (int i = 0; i < pathNodes.Count; i++)
        {
            pathNodes[i].Depth = i;
        }

        trace.PathNodes = pathNodes;
        trace.AllRolesUsed = allRolesUsed.Values.ToList();
        trace.GrantsUsed = allGrantsUsed;
        trace.AccessGranted = accessGranted;

        if (accessGranted)
        {
            if (grantedViaGroup && grantingGroupName != null)
            {
                trace.DecisionSummary = $"Access granted via group '{grantingGroupName}' which has role '{grantingRoleName}' on '{grantingNodeName}'. " +
                                       $"The role '{grantingRoleName}' includes permission '{permissionKey}' which is inherited by child resources.";
            }
            else
            {
                var targetNode = pathNodes.FirstOrDefault(n => n.IsTarget);
                if (targetNode != null && targetNode.PermissionFoundHere)
                {
                    trace.DecisionSummary = $"Access granted because {trace.PrincipalDisplayName} has role '{grantingRoleName}' " +
                                           $"directly on this resource, and '{grantingRoleName}' includes permission '{permissionKey}'.";
                }
                else
                {
                    trace.DecisionSummary = $"Access granted because {trace.PrincipalDisplayName} has role '{grantingRoleName}' " +
                                           $"on parent resource '{grantingNodeName}', and '{grantingRoleName}' includes permission '{permissionKey}' " +
                                           $"which is inherited by child resources.";
                }
            }
        }
        else
        {
            if (allGrantsUsed.Count == 0)
            {
                trace.DenialReason = $"No grants found for {trace.PrincipalDisplayName} (or their groups) on this resource or any ancestor resources.";
                trace.DecisionSummary = $"Access denied. No roles are assigned to {trace.PrincipalDisplayName} on '{trace.TargetResourceName}' " +
                                       $"or any of its parent resources.";
                trace.Suggestion = $"To grant access, assign a role that includes '{permissionKey}' on this resource or on a parent.";
            }
            else
            {
                var roleNames = allRolesUsed.Values.Select(r => r.RoleName).Distinct().ToList();
                trace.DenialReason = $"Grants were found, but none of the roles ({string.Join(", ", roleNames)}) include permission '{permissionKey}'.";
                trace.DecisionSummary = $"Access denied. {trace.PrincipalDisplayName} has grants on ancestor resources, " +
                                       $"but none of the assigned roles include permission '{permissionKey}'.";
                trace.Suggestion = $"Either assign a different role that includes '{permissionKey}', or add '{permissionKey}' " +
                                  $"to one of the existing roles ({string.Join(", ", roleNames)}).";
            }
        }

        return trace;
    }

    public async Task<Expression<Func<T, bool>>> GetAuthorizationFilterAsync<T>(
        string principalId,
        string permissionKey) where T : IHasResourceId
    {
        var principalIds = await ResolvePrincipalIdsAsync(principalId);
        if (principalIds.Count == 0)
        {
            _logger.LogWarning("No principals found for {PrincipalId}", principalId);
            return entity => false;
        }

        var permission = await _context.Set<SqlzibarPermission>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == permissionKey);

        if (permission == null)
        {
            _logger.LogWarning("Permission {PermissionKey} not found", permissionKey);
            return entity => false;
        }

        var principalIdsStr = string.Join(",", principalIds);
        var permissionId = permission.Id;

        // Build the expression using the concrete DbContext type's method
        // so EF Core can match it to the registered DbFunction (TVF).
        // Using the interface method directly would fail because EF Core only
        // registers DbFunctions on DbContext subclasses, not interfaces.
        var contextType = _context.GetType();
        var tvfMethod = contextType.GetMethod(
            nameof(ISqlzibarDbContext.IsResourceAccessible),
            new[] { typeof(string), typeof(string), typeof(string) });

        if (tvfMethod == null)
        {
            _logger.LogWarning("IsResourceAccessible method not found on {ContextType}", contextType.Name);
            return entity => false;
        }

        var entityParam = Expression.Parameter(typeof(T), "entity");
        var resourceIdProp = Expression.Property(entityParam, nameof(IHasResourceId.ResourceId));
        var contextExpr = Expression.Constant(_context, contextType);
        var tvfCall = Expression.Call(contextExpr, tvfMethod,
            resourceIdProp,
            Expression.Constant(principalIdsStr),
            Expression.Constant(permissionId));

        var anyMethod = typeof(Queryable).GetMethods()
            .First(m => m.Name == "Any" && m.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(SqlzibarAccessibleResource));
        var anyCall = Expression.Call(anyMethod, tvfCall);

        return Expression.Lambda<Func<T, bool>>(anyCall, entityParam);
    }

    private async Task<List<string>> ResolvePrincipalsAsync(string principalId, List<SqlzibarAccessTrace> trace)
    {
        var principals = await ResolvePrincipalIdsAsync(principalId);

        var principalData = await _context.Set<SqlzibarPrincipal>().FirstOrDefaultAsync(p => p.Id == principalId);
        var groupCount = principals.Count - 1;
        trace.Add(new SqlzibarAccessTrace
        {
            Step = "Principal Resolution",
            Detail = $"Principal \"{principalData?.DisplayName}\" + {groupCount} group membership(s)",
            PrincipalName = principalData?.DisplayName,
        });

        return principals;
    }

    private async Task<List<string>> ResolvePrincipalIdsAsync(string principalId)
    {
        var principals = new List<string> { principalId };

        var groupPrincipalIds = await _context.Set<SqlzibarUserGroupMembership>()
            .Where(m => m.PrincipalId == principalId)
            .Join(_context.Set<SqlzibarUserGroup>(),
                m => m.UserGroupId,
                g => g.Id,
                (m, g) => g.PrincipalId)
            .ToListAsync();

        principals.AddRange(groupPrincipalIds);
        return principals;
    }

    private async Task<List<SqlzibarPrincipalInfo>> ResolvePrincipalsWithInfoAsync(string principalId)
    {
        var result = new List<SqlzibarPrincipalInfo>();

        var principal = await _context.Set<SqlzibarPrincipal>().FirstOrDefaultAsync(p => p.Id == principalId);
        result.Add(new SqlzibarPrincipalInfo
        {
            PrincipalId = principalId,
            DisplayName = principal?.DisplayName ?? principalId,
            Type = "user",
            IsDirect = true
        });

        var memberships = await _context.Set<SqlzibarUserGroupMembership>()
            .Where(m => m.PrincipalId == principalId)
            .ToListAsync();

        foreach (var membership in memberships)
        {
            var group = await _context.Set<SqlzibarUserGroup>().FirstOrDefaultAsync(g => g.Id == membership.UserGroupId);
            if (group != null)
            {
                result.Add(new SqlzibarPrincipalInfo
                {
                    PrincipalId = group.PrincipalId,
                    DisplayName = group.Name,
                    Type = "usergroup",
                    IsDirect = false
                });
            }
        }

        return result;
    }

    private async Task<List<SqlzibarResource>> GetAncestorResourcesAsync(string resourceId)
    {
        var ancestors = new List<SqlzibarResource>();
        string? currentId = resourceId;

        while (currentId != null)
        {
            var resource = await _context.Set<SqlzibarResource>().FirstOrDefaultAsync(r => r.Id == currentId);
            if (resource == null) break;
            ancestors.Add(resource);
            currentId = resource.ParentId;
        }

        return ancestors;
    }

    private async Task<List<SqlzibarGrant>> GetActiveGrantsAsync(string principalId, string resourceId)
    {
        var now = DateTime.UtcNow;

        return await _context.Set<SqlzibarGrant>()
            .Where(g => g.PrincipalId == principalId &&
                       g.ResourceId == resourceId &&
                       (g.EffectiveFrom == null || g.EffectiveFrom <= now) &&
                       (g.EffectiveTo == null || g.EffectiveTo >= now))
            .ToListAsync();
    }
}
