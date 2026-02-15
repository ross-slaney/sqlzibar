using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Sqlzibar.Configuration;
using Sqlzibar.Interfaces;
using Sqlzibar.Models;

namespace Sqlzibar.Dashboard;

public class SqlzibarDashboardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _pathPrefix;
    private readonly bool _isDevelopment;
    private readonly SqlzibarDashboardOptions _dashboardOptions;
    private readonly ManifestEmbeddedFileProvider _fileProvider;
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SqlzibarDashboardMiddleware(
        RequestDelegate next,
        string pathPrefix,
        IHostEnvironment environment,
        SqlzibarDashboardOptions dashboardOptions)
    {
        _next = next;
        _pathPrefix = pathPrefix.TrimEnd('/');
        _isDevelopment = environment.IsDevelopment();
        _dashboardOptions = dashboardOptions;
        _fileProvider = new ManifestEmbeddedFileProvider(
            typeof(SqlzibarDashboardMiddleware).Assembly,
            "Dashboard/wwwroot");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (!path.StartsWith(_pathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Authorization: use custom callback if provided, otherwise only allow in Development
        if (_dashboardOptions.AuthorizationCallback != null)
        {
            if (!await _dashboardOptions.AuthorizationCallback(context))
            {
                context.Response.StatusCode = 404;
                return;
            }
        }
        else if (!_isDevelopment)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var relativePath = path[_pathPrefix.Length..].TrimStart('/');

        // Redirect /sqlzibar to /sqlzibar/ so relative paths (style.css, app.js) resolve correctly
        if (string.IsNullOrEmpty(relativePath) && !path.EndsWith('/'))
        {
            context.Response.Redirect($"{_pathPrefix}/", permanent: false);
            return;
        }

        // API endpoints
        if (relativePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleApiRequest(context, relativePath[4..]);
            return;
        }

        // Serve static files
        await ServeStaticFile(context, relativePath);
    }

    private async Task HandleApiRequest(HttpContext context, string endpoint)
    {
        using var scope = context.RequestServices.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ISqlzibarDbContext>();

        context.Response.ContentType = "application/json";

        // Handle POST trace endpoint
        if (endpoint.Equals("trace", StringComparison.OrdinalIgnoreCase) && context.Request.Method == "POST")
        {
            var body = await JsonSerializer.DeserializeAsync<TraceRequest>(context.Request.Body, JsonOptions);
            if (body == null || string.IsNullOrEmpty(body.PrincipalId) || string.IsNullOrEmpty(body.ResourceId) || string.IsNullOrEmpty(body.PermissionKey))
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("{\"error\":\"principalId, resourceId, and permissionKey are required\"}");
                return;
            }
            var authService = scope.ServiceProvider.GetRequiredService<ISqlzibarAuthService>();
            var trace = await authService.TraceResourceAccessAsync(body.PrincipalId, body.ResourceId, body.PermissionKey);
            await context.Response.WriteAsync(JsonSerializer.Serialize(trace, JsonOptions));
            return;
        }

        // Handle roles/{id}/permissions before the main switch
        if (endpoint.StartsWith("roles/", StringComparison.OrdinalIgnoreCase) && endpoint.EndsWith("/permissions"))
        {
            var roleId = endpoint[6..^12]; // extract id between "roles/" and "/permissions"
            var perms = await dbContext.Set<SqlzibarRolePermission>()
                .Include(rp => rp.Permission)
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => new
                {
                    rp.Permission!.Id,
                    rp.Permission.Key,
                    rp.Permission.Name,
                    rp.Permission.Description
                })
                .ToListAsync();
            await context.Response.WriteAsync(JsonSerializer.Serialize(perms, JsonOptions));
            return;
        }

        // Handle resources/{parentId}/children
        if (endpoint.StartsWith("resources/", StringComparison.OrdinalIgnoreCase) && endpoint.EndsWith("/children"))
        {
            var parentId = endpoint[10..^9]; // extract id between "resources/" and "/children"
            await HandleResourceChildren(context, dbContext, parentId);
            return;
        }

        // Handle principals/{id}/grants
        if (endpoint.StartsWith("principals/", StringComparison.OrdinalIgnoreCase) && endpoint.EndsWith("/grants"))
        {
            var principalId = endpoint[11..^7]; // extract id between "principals/" and "/grants"
            await HandlePrincipalGrants(context, dbContext, principalId);
            return;
        }

        // Handle principals/{id} (single principal detail) — must be after /grants check
        if (endpoint.StartsWith("principals/", StringComparison.OrdinalIgnoreCase) && !endpoint[11..].Contains('/'))
        {
            var principalId = endpoint[11..];
            await HandlePrincipalDetail(context, dbContext, principalId);
            return;
        }

        object? result = endpoint.ToLowerInvariant() switch
        {
            "resources/tree" => await GetResourceTreeAsync(dbContext, context),
            "principals" => await GetPrincipalsAsync(dbContext, context),
            "grants" => await GetGrantsAsync(dbContext, context),
            "roles" => await GetRolesAsync(dbContext, context),
            "permissions" => await GetPermissionsAsync(dbContext, context),
            "stats" => new
            {
                Resources = await dbContext.Set<SqlzibarResource>().CountAsync(),
                Principals = await dbContext.Set<SqlzibarPrincipal>().CountAsync(),
                Grants = await dbContext.Set<SqlzibarGrant>().CountAsync(),
                Roles = await dbContext.Set<SqlzibarRole>().CountAsync(),
                Permissions = await dbContext.Set<SqlzibarPermission>().CountAsync(),
            },
            _ => null
        };

        if (result == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("{\"error\":\"Not found\"}");
            return;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
    }

    // --- Resource Tree (breadth-first initial load) ---

    private static async Task<object> GetResourceTreeAsync(ISqlzibarDbContext dbContext, HttpContext context)
    {
        var maxDepth = GetIntParam(context, "maxDepth", 2);
        maxDepth = Math.Clamp(maxDepth, 1, 5);

        var allResources = dbContext.Set<SqlzibarResource>()
            .Include(r => r.ResourceType)
            .Where(r => r.IsActive);

        // Find root nodes (no parent)
        var rootIds = await allResources
            .Where(r => r.ParentId == null)
            .OrderBy(r => r.Name)
            .Select(r => r.Id)
            .ToListAsync();

        var nodes = new List<object>();
        var currentLevelIds = rootIds;

        for (int depth = 0; depth <= maxDepth && currentLevelIds.Count > 0; depth++)
        {
            var levelNodes = await allResources
                .Where(r => currentLevelIds.Contains(r.Id))
                .OrderBy(r => r.Name)
                .Select(r => new
                {
                    r.Id,
                    r.ParentId,
                    r.Name,
                    ResourceType = r.ResourceType != null ? r.ResourceType.Name : r.ResourceTypeId,
                    ChildCount = dbContext.Set<SqlzibarResource>().Count(c => c.ParentId == r.Id && c.IsActive),
                    GrantsCount = dbContext.Set<SqlzibarGrant>().Count(g => g.ResourceId == r.Id)
                })
                .ToListAsync();

            nodes.AddRange(levelNodes.Cast<object>());

            // Get next level IDs
            if (depth < maxDepth)
            {
                currentLevelIds = await allResources
                    .Where(r => currentLevelIds.Contains(r.ParentId!))
                    .Select(r => r.Id)
                    .ToListAsync();
            }
            else
            {
                currentLevelIds = [];
            }
        }

        return new { Nodes = nodes, RootIds = rootIds, LoadedDepth = maxDepth };
    }

    private static async Task HandleResourceChildren(
        HttpContext context, ISqlzibarDbContext dbContext, string parentId)
    {
        var (page, pageSize) = GetPaginationParams(context);
        var search = context.Request.Query["search"].FirstOrDefault();

        var query = dbContext.Set<SqlzibarResource>()
            .Include(r => r.ResourceType)
            .Where(r => r.ParentId == parentId && r.IsActive);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => r.Name.Contains(search));

        var totalCount = await query.CountAsync();
        var data = await query
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.ParentId,
                r.Name,
                ResourceType = r.ResourceType != null ? r.ResourceType.Name : r.ResourceTypeId,
                ChildCount = dbContext.Set<SqlzibarResource>().Count(c => c.ParentId == r.Id && c.IsActive),
                GrantsCount = dbContext.Set<SqlzibarGrant>().Count(g => g.ResourceId == r.Id)
            })
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var result = new
        {
            Data = data,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            ParentId = parentId
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
    }

    // --- Principal detail ---

    private static async Task HandlePrincipalDetail(HttpContext context, ISqlzibarDbContext dbContext, string principalId)
    {
        var principal = await dbContext.Set<SqlzibarPrincipal>()
            .Include(p => p.PrincipalType)
            .Where(p => p.Id == principalId)
            .Select(p => new
            {
                p.Id, p.DisplayName, p.PrincipalTypeId,
                PrincipalType = p.PrincipalType != null ? p.PrincipalType.Name : p.PrincipalTypeId,
                p.OrganizationId, p.ExternalRef, p.CreatedAt, p.UpdatedAt
            })
            .FirstOrDefaultAsync();

        if (principal == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("{\"error\":\"Principal not found\"}");
            return;
        }

        // Get group memberships (groups this principal belongs to)
        var groups = await dbContext.Set<SqlzibarUserGroupMembership>()
            .Include(m => m.UserGroup)
            .Where(m => m.PrincipalId == principalId)
            .Select(m => new
            {
                m.UserGroup!.Id,
                m.UserGroup.Name,
                m.UserGroup.GroupType,
                m.UserGroup.PrincipalId,
                m.CreatedAt
            })
            .ToListAsync();

        // If this principal IS a group, get its members
        var members = await dbContext.Set<SqlzibarUserGroupMembership>()
            .Include(m => m.Principal)
            .Where(m => m.UserGroup != null && m.UserGroup.PrincipalId == principalId)
            .Select(m => new
            {
                m.Principal!.Id,
                m.Principal.DisplayName,
                m.Principal.PrincipalTypeId,
                m.CreatedAt
            })
            .ToListAsync();

        var result = new { Principal = principal, Groups = groups, Members = members };
        await context.Response.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
    }

    private static async Task HandlePrincipalGrants(
        HttpContext context, ISqlzibarDbContext dbContext, string principalId)
    {
        var (page, pageSize) = GetPaginationParams(context);

        var query = dbContext.Set<SqlzibarGrant>()
            .Include(g => g.Resource)
            .Include(g => g.Role)
            .Where(g => g.PrincipalId == principalId);

        var totalCount = await query.CountAsync();
        var data = await query
            .OrderByDescending(g => g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new
            {
                g.Id,
                ResourceName = g.Resource != null ? g.Resource.Name : g.ResourceId,
                g.ResourceId,
                RoleName = g.Role != null ? g.Role.Name : g.RoleId,
                g.RoleId,
                g.EffectiveFrom, g.EffectiveTo, g.CreatedAt
            })
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var result = new
        {
            Data = data,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(result, JsonOptions));
    }

    // --- Paginated table endpoints ---

    private static async Task<object> GetPrincipalsAsync(ISqlzibarDbContext dbContext, HttpContext context)
    {
        var (page, pageSize) = GetPaginationParams(context);
        var type = context.Request.Query["type"].FirstOrDefault();
        var search = context.Request.Query["search"].FirstOrDefault();

        var query = dbContext.Set<SqlzibarPrincipal>()
            .Include(p => p.PrincipalType)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type))
            query = query.Where(p => p.PrincipalTypeId == type);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(p => p.DisplayName.Contains(search) || p.Id.Contains(search));

        var totalCount = await query.CountAsync();
        var data = await query
            .OrderBy(p => p.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id, p.DisplayName, p.PrincipalTypeId,
                PrincipalType = p.PrincipalType != null ? p.PrincipalType.Name : p.PrincipalTypeId,
                p.OrganizationId, p.ExternalRef, p.CreatedAt
            })
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new { Data = data, Page = page, PageSize = pageSize, TotalCount = totalCount, TotalPages = totalPages };
    }

    private static async Task<object> GetGrantsAsync(ISqlzibarDbContext dbContext, HttpContext context)
    {
        var (page, pageSize) = GetPaginationParams(context);
        var search = context.Request.Query["search"].FirstOrDefault();

        var query = dbContext.Set<SqlzibarGrant>()
            .Include(g => g.Principal)
            .Include(g => g.Resource)
            .Include(g => g.Role)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(g =>
                (g.Principal != null && g.Principal.DisplayName.Contains(search)) ||
                (g.Resource != null && g.Resource.Name.Contains(search)) ||
                (g.Role != null && g.Role.Name.Contains(search)));

        var totalCount = await query.CountAsync();
        var data = await query
            .OrderByDescending(g => g.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new
            {
                g.Id,
                PrincipalName = g.Principal != null ? g.Principal.DisplayName : g.PrincipalId,
                g.PrincipalId,
                ResourceName = g.Resource != null ? g.Resource.Name : g.ResourceId,
                g.ResourceId,
                RoleName = g.Role != null ? g.Role.Name : g.RoleId,
                g.RoleId,
                g.EffectiveFrom, g.EffectiveTo, g.CreatedAt
            })
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new { Data = data, Page = page, PageSize = pageSize, TotalCount = totalCount, TotalPages = totalPages };
    }

    private static async Task<object> GetRolesAsync(ISqlzibarDbContext dbContext, HttpContext context)
    {
        var (page, pageSize) = GetPaginationParams(context);
        var search = context.Request.Query["search"].FirstOrDefault();

        var query = dbContext.Set<SqlzibarRole>()
            .Include(r => r.RolePermissions)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(r => r.Name.Contains(search) || r.Key.Contains(search));

        var totalCount = await query.CountAsync();
        var data = await query
            .OrderBy(r => r.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id, r.Key, r.Name, r.Description, r.IsVirtual,
                PermissionCount = r.RolePermissions.Count
            })
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new { Data = data, Page = page, PageSize = pageSize, TotalCount = totalCount, TotalPages = totalPages };
    }

    private static async Task<object> GetPermissionsAsync(ISqlzibarDbContext dbContext, HttpContext context)
    {
        var (page, pageSize) = GetPaginationParams(context);
        var search = context.Request.Query["search"].FirstOrDefault();

        var query = dbContext.Set<SqlzibarPermission>()
            .Include(p => p.ResourceType)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
            query = query.Where(p => p.Key.Contains(search) || p.Name.Contains(search));

        var totalCount = await query.CountAsync();
        var data = await query
            .OrderBy(p => p.Key)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id, p.Key, p.Name, p.Description,
                ResourceType = p.ResourceType != null ? p.ResourceType.Name : null
            })
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        return new { Data = data, Page = page, PageSize = pageSize, TotalCount = totalCount, TotalPages = totalPages };
    }

    // --- Helpers ---

    private static (int Page, int PageSize) GetPaginationParams(HttpContext context)
    {
        var page = GetIntParam(context, "page", 1);
        var pageSize = GetIntParam(context, "pageSize", DefaultPageSize);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        return (page, pageSize);
    }

    private static int GetIntParam(HttpContext context, string name, int defaultValue)
    {
        var value = context.Request.Query[name].FirstOrDefault();
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private async Task ServeStaticFile(HttpContext context, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || relativePath == "/")
        {
            relativePath = "index.html";
        }

        var fileInfo = _fileProvider.GetFileInfo(relativePath);

        if (!fileInfo.Exists)
        {
            // SPA fallback
            fileInfo = _fileProvider.GetFileInfo("index.html");
        }

        if (!fileInfo.Exists)
        {
            context.Response.StatusCode = 404;
            return;
        }

        var contentType = GetContentType(relativePath);
        context.Response.ContentType = contentType;

        await using var stream = fileInfo.CreateReadStream();
        await stream.CopyToAsync(context.Response.Body);
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html",
        ".css" => "text/css",
        ".js" => "application/javascript",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };

    private record TraceRequest(string PrincipalId, string ResourceId, string PermissionKey);
}
