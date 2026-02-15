using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Specifications;

namespace Sqlzibar.Example.Api.Specifications;

public class GetLocationsSpecification : PagedSpecification<Location>
{
    private readonly string? _search;
    private readonly string? _chainId;

    public GetLocationsSpecification(int pageSize, string? search = null, string? chainId = null)
    {
        PageSize = pageSize;
        _search = search;
        _chainId = chainId;
    }

    public override string? RequiredPermission => RetailPermissionKeys.LocationView;

    public override Expression<Func<Location, bool>> ToExpression()
    {
        return l =>
            (_chainId == null || l.ChainId == _chainId) &&
            (_search == null ||
             l.Name.ToLower().Contains(_search.ToLower()) ||
             (l.StoreNumber != null && l.StoreNumber.ToLower().Contains(_search.ToLower())));
    }

    public override IOrderedQueryable<Location> ApplySort(IQueryable<Location> query)
        => query.OrderBy(l => l.StoreNumber).ThenBy(l => l.Id);

    public override Expression<Func<Location, bool>> GetCursorFilter(string cursor)
    {
        var (storeNumber, id) = DecodeCursor(cursor);
        return l => string.Compare(l.StoreNumber, storeNumber) > 0
            || (l.StoreNumber == storeNumber && l.Id.CompareTo(id) > 0);
    }

    public override string BuildCursor(Location entity)
        => EncodeCursor(entity.StoreNumber ?? "", entity.Id);

    public override IQueryable<Location> ConfigureQuery(IQueryable<Location> query)
        => query.Include(l => l.Chain);
}
