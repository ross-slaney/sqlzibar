using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Specifications;

namespace Sqlzibar.Example.Api.Specifications;

public class GetLocationsSpecification : SortablePagedSpecification<Location>
{
    public GetLocationsSpecification(int pageSize, string? search = null, string? chainId = null)
    {
        PageSize = pageSize;
        RegisterStringSort("storeNumber", l => l.StoreNumber ?? "", isDefault: true);

        if (chainId != null)
            AddFilter(l => l.ChainId == chainId);

        Search(search, l => l.Name, l => l.StoreNumber);
    }

    public override string? RequiredPermission => RetailPermissionKeys.LocationView;
    protected override Expression<Func<Location, string>> IdSelector => l => l.Id;

    public override IQueryable<Location> ConfigureQuery(IQueryable<Location> query)
        => query.Include(l => l.Chain);
}
