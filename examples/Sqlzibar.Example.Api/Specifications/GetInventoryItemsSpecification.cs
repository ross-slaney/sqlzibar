using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Specifications;

namespace Sqlzibar.Example.Api.Specifications;

public class GetInventoryItemsSpecification : PagedSpecification<InventoryItem>
{
    private readonly string? _search;
    private readonly string? _locationId;
    private readonly string _sortBy;
    private readonly bool _descending;

    public GetInventoryItemsSpecification(
        int pageSize, string? search = null, string? locationId = null,
        string? sortBy = null, bool descending = false)
    {
        PageSize = pageSize;
        _search = search;
        _locationId = locationId;
        _sortBy = (sortBy ?? "name").ToLowerInvariant();
        _descending = descending;
    }

    public override string? RequiredPermission => RetailPermissionKeys.InventoryView;

    public override Expression<Func<InventoryItem, bool>> ToExpression()
    {
        return i =>
            (_locationId == null || i.LocationId == _locationId) &&
            (_search == null ||
             i.Name.ToLower().Contains(_search.ToLower()) ||
             i.Sku.ToLower().Contains(_search.ToLower()));
    }

    public override IOrderedQueryable<InventoryItem> ApplySort(IQueryable<InventoryItem> query)
    {
        return (_sortBy, _descending) switch
        {
            ("price", false) => query.OrderBy(i => i.Price).ThenBy(i => i.Id),
            ("price", true) => query.OrderByDescending(i => i.Price).ThenByDescending(i => i.Id),
            ("sku", false) => query.OrderBy(i => i.Sku).ThenBy(i => i.Id),
            ("sku", true) => query.OrderByDescending(i => i.Sku).ThenByDescending(i => i.Id),
            (_, true) => query.OrderByDescending(i => i.Name).ThenByDescending(i => i.Id),
            _ => query.OrderBy(i => i.Name).ThenBy(i => i.Id),
        };
    }

    public override Expression<Func<InventoryItem, bool>> GetCursorFilter(string cursor)
    {
        var (sortVal, id) = DecodeCursor(cursor);

        if (_sortBy == "price")
        {
            var price = decimal.Parse(sortVal, CultureInfo.InvariantCulture);
            if (_descending)
                return i => i.Price < price || (i.Price == price && i.Id.CompareTo(id) < 0);
            return i => i.Price > price || (i.Price == price && i.Id.CompareTo(id) > 0);
        }

        if (_descending)
        {
            return _sortBy switch
            {
                "sku" => i => i.Sku.CompareTo(sortVal) < 0 || (i.Sku == sortVal && i.Id.CompareTo(id) < 0),
                _ => i => i.Name.CompareTo(sortVal) < 0 || (i.Name == sortVal && i.Id.CompareTo(id) < 0),
            };
        }

        return _sortBy switch
        {
            "sku" => i => i.Sku.CompareTo(sortVal) > 0 || (i.Sku == sortVal && i.Id.CompareTo(id) > 0),
            _ => i => i.Name.CompareTo(sortVal) > 0 || (i.Name == sortVal && i.Id.CompareTo(id) > 0),
        };
    }

    public override string BuildCursor(InventoryItem entity)
    {
        var sortValue = _sortBy switch
        {
            "price" => entity.Price.ToString(CultureInfo.InvariantCulture),
            "sku" => entity.Sku,
            _ => entity.Name,
        };
        return EncodeCursor(sortValue, entity.Id);
    }

    public override IQueryable<InventoryItem> ConfigureQuery(IQueryable<InventoryItem> query)
        => query.Include(i => i.Location);
}
