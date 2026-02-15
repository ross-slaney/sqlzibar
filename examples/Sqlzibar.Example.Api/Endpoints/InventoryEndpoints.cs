using Microsoft.EntityFrameworkCore;
using Sqlzibar.Example.Api.Data;
using Sqlzibar.Example.Api.Dtos;
using Sqlzibar.Example.Api.Middleware;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Example.Api.Specifications;
using Sqlzibar.Interfaces;
using Sqlzibar.Models;

namespace Sqlzibar.Example.Api.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Inventory");

        group.MapGet("/locations/{locationId}/inventory", async (
            string locationId,
            RetailDbContext context,
            ISpecificationExecutor executor,
            HttpContext http,
            int pageSize = 10,
            string? search = null,
            string? cursor = null,
            string? sortBy = null,
            string? sortDir = null) =>
        {
            var principalId = http.GetPrincipalId();
            var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
            var spec = new GetInventoryItemsSpecification(pageSize, search, locationId, sortBy, descending) { Cursor = cursor };
            var result = await executor.ExecuteAsync(
                context.InventoryItems, spec, principalId,
                i => new InventoryItemDto
                {
                    Id = i.Id,
                    ResourceId = i.ResourceId,
                    LocationId = i.LocationId,
                    LocationName = i.Location?.Name,
                    Sku = i.Sku,
                    Name = i.Name,
                    Price = i.Price,
                    QuantityOnHand = i.QuantityOnHand,
                    CreatedAt = i.CreatedAt
                });
            return Results.Ok(result);
        }).WithName("GetInventoryItems");

        group.MapGet("/inventory/{id}", async (
            string id,
            RetailDbContext context,
            ISqlzibarAuthService authService,
            HttpContext http) =>
        {
            var item = await context.InventoryItems
                .Include(i => i.Location)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (item is null) return Results.NotFound();

            var principalId = http.GetPrincipalId();
            var access = await authService.CheckAccessAsync(principalId, RetailPermissionKeys.InventoryView, item.ResourceId);
            if (!access.Allowed) return Results.Json(new { error = "Permission denied" }, statusCode: 403);

            return Results.Ok(new InventoryItemDetailDto
            {
                Id = item.Id,
                ResourceId = item.ResourceId,
                LocationId = item.LocationId,
                LocationName = item.Location?.Name,
                Sku = item.Sku,
                Name = item.Name,
                Description = item.Description,
                Price = item.Price,
                QuantityOnHand = item.QuantityOnHand,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            });
        }).WithName("GetInventoryItem");

        group.MapPost("/locations/{locationId}/inventory", async (
            string locationId,
            CreateInventoryItemRequest request,
            RetailDbContext context,
            ISqlzibarAuthService authService,
            HttpContext http) =>
        {
            var principalId = http.GetPrincipalId();

            var location = await context.Locations.FirstOrDefaultAsync(l => l.Id == locationId);
            if (location is null) return Results.NotFound();

            var access = await authService.CheckAccessAsync(principalId, RetailPermissionKeys.InventoryEdit, location.ResourceId);
            if (!access.Allowed) return Results.Json(new { error = "Permission denied" }, statusCode: 403);

            var resourceId = $"res_inv_{Guid.NewGuid():N}"[..30];
            var resource = new SqlzibarResource
            {
                Id = resourceId,
                ParentId = location.ResourceId,
                Name = request.Name,
                ResourceTypeId = RetailResourceTypeIds.InventoryItem
            };
            context.Set<SqlzibarResource>().Add(resource);

            var item = new InventoryItem
            {
                ResourceId = resourceId,
                LocationId = locationId,
                Sku = request.Sku,
                Name = request.Name,
                Description = request.Description,
                Price = request.Price,
                QuantityOnHand = request.QuantityOnHand
            };
            context.InventoryItems.Add(item);

            await context.SaveChangesAsync();

            return Results.Created($"/api/inventory/{item.Id}", new InventoryItemDetailDto
            {
                Id = item.Id,
                ResourceId = item.ResourceId,
                LocationId = item.LocationId,
                LocationName = location.Name,
                Sku = item.Sku,
                Name = item.Name,
                Description = item.Description,
                Price = item.Price,
                QuantityOnHand = item.QuantityOnHand,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            });
        }).WithName("CreateInventoryItem");
    }
}
