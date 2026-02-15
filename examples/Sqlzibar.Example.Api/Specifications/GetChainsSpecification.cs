using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Specifications;

namespace Sqlzibar.Example.Api.Specifications;

public class GetChainsSpecification : PagedSpecification<Chain>
{
    private readonly string? _search;

    public GetChainsSpecification(int pageSize, string? search = null)
    {
        PageSize = pageSize;
        _search = search;
    }

    public override string? RequiredPermission => RetailPermissionKeys.ChainView;

    public override Expression<Func<Chain, bool>> ToExpression()
    {
        if (string.IsNullOrWhiteSpace(_search))
            return _ => true;

        var search = _search.ToLower();
        return c => c.Name.ToLower().Contains(search) ||
                     (c.Description != null && c.Description.ToLower().Contains(search));
    }

    public override IOrderedQueryable<Chain> ApplySort(IQueryable<Chain> query)
        => query.OrderBy(c => c.Name).ThenBy(c => c.Id);

    public override Expression<Func<Chain, bool>> GetCursorFilter(string cursor)
    {
        var (name, id) = DecodeCursor(cursor);
        return c => c.Name.CompareTo(name) > 0 || (c.Name == name && c.Id.CompareTo(id) > 0);
    }

    public override string BuildCursor(Chain entity)
        => EncodeCursor(entity.Name, entity.Id);

    public override IQueryable<Chain> ConfigureQuery(IQueryable<Chain> query)
        => query.Include(c => c.Locations);
}
