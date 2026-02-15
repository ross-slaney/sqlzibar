using Microsoft.EntityFrameworkCore;
using Sqlzibar.Example.Api.Data;
using Sqlzibar.Example.Api.Dtos;
using Sqlzibar.Example.Api.Middleware;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Interfaces;
using Sqlzibar.Models;
using Sqlzibar.Specifications;

namespace Sqlzibar.Example.Api.Endpoints;

public static class ChainEndpoints
{
    public static void MapChainEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chains").WithTags("Chains");

        group.MapGet("/", async (
            RetailDbContext context,
            ISpecificationExecutor executor,
            HttpContext http,
            int pageSize = 10,
            string? search = null,
            string? cursor = null) =>
        {
            var principalId = http.GetPrincipalId();

            var builder = PagedSpec.For<Chain>(c => c.Id)
                .RequirePermission(RetailPermissionKeys.ChainView)
                .SortByString("name", c => c.Name, isDefault: true)
                .Configure(q => q.Include(c => c.Locations));

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLower();
                builder = builder.Where(c => c.Name.ToLower().Contains(s) ||
                                             (c.Description != null && c.Description.ToLower().Contains(s)));
            }

            var spec = builder.Build(pageSize, cursor);
            var result = await executor.ExecuteAsync(
                context.Chains, spec, principalId,
                c => new ChainDto
                {
                    Id = c.Id,
                    ResourceId = c.ResourceId,
                    Name = c.Name,
                    Description = c.Description,
                    LocationCount = c.Locations.Count,
                    CreatedAt = c.CreatedAt
                });
            return Results.Ok(result);
        }).WithName("GetChains");

        group.MapGet("/{id}", async (
            string id,
            RetailDbContext context,
            ISqlzibarAuthService authService,
            HttpContext http) =>
        {
            var chain = await context.Chains
                .Include(c => c.Locations)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (chain is null) return Results.NotFound();

            var principalId = http.GetPrincipalId();
            var access = await authService.CheckAccessAsync(principalId, RetailPermissionKeys.ChainView, chain.ResourceId);
            if (!access.Allowed) return Results.Json(new { error = "Permission denied" }, statusCode: 403);

            return Results.Ok(new ChainDetailDto
            {
                Id = chain.Id,
                ResourceId = chain.ResourceId,
                Name = chain.Name,
                Description = chain.Description,
                HeadquartersAddress = chain.HeadquartersAddress,
                LocationCount = chain.Locations.Count,
                CreatedAt = chain.CreatedAt,
                UpdatedAt = chain.UpdatedAt
            });
        }).WithName("GetChain");

        group.MapPost("/", async (
            CreateChainRequest request,
            RetailDbContext context,
            ISqlzibarAuthService authService,
            HttpContext http) =>
        {
            var principalId = http.GetPrincipalId();

            // Creating a chain requires CHAIN_EDIT at the root
            var access = await authService.CheckAccessAsync(principalId, RetailPermissionKeys.ChainEdit, "retail_root");
            if (!access.Allowed) return Results.Json(new { error = "Permission denied" }, statusCode: 403);

            var resourceId = $"res_chain_{Guid.NewGuid():N}"[..30];
            var resource = new SqlzibarResource
            {
                Id = resourceId,
                ParentId = "retail_root",
                Name = request.Name,
                ResourceTypeId = RetailResourceTypeIds.Chain
            };
            context.Set<SqlzibarResource>().Add(resource);

            var chain = new Chain
            {
                ResourceId = resourceId,
                Name = request.Name,
                Description = request.Description,
                HeadquartersAddress = request.HeadquartersAddress
            };
            context.Chains.Add(chain);

            await context.SaveChangesAsync();

            return Results.Created($"/api/chains/{chain.Id}", new ChainDetailDto
            {
                Id = chain.Id,
                ResourceId = chain.ResourceId,
                Name = chain.Name,
                Description = chain.Description,
                HeadquartersAddress = chain.HeadquartersAddress,
                LocationCount = 0,
                CreatedAt = chain.CreatedAt,
                UpdatedAt = chain.UpdatedAt
            });
        }).WithName("CreateChain");
    }
}
