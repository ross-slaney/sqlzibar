using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Example.Api.Specifications;

namespace Sqlzibar.Example.Tests.Specifications;

[TestClass]
public class GetLocationsSpecificationTests
{
    [TestMethod]
    public void ToExpression_WithSearch_FiltersByNameOrStoreNumber()
    {
        var spec = new GetLocationsSpecification(10, "001");
        var expr = spec.ToExpression().Compile();

        var location = new Location { Name = "Store", StoreNumber = "001", ChainId = "c1" };
        expr(location).Should().BeTrue();

        var other = new Location { Name = "Store", StoreNumber = "002", ChainId = "c1" };
        expr(other).Should().BeFalse();
    }

    [TestMethod]
    public void ToExpression_WithChainId_FiltersCorrectly()
    {
        var spec = new GetLocationsSpecification(10, chainId: "chain_1");
        var expr = spec.ToExpression().Compile();

        var match = new Location { Name = "Store", ChainId = "chain_1" };
        expr(match).Should().BeTrue();

        var noMatch = new Location { Name = "Store", ChainId = "chain_2" };
        expr(noMatch).Should().BeFalse();
    }

    [TestMethod]
    public void ToExpression_WithSearchAndChainId_CombinesFilters()
    {
        var spec = new GetLocationsSpecification(10, "main", "chain_1");
        var expr = spec.ToExpression().Compile();

        var match = new Location { Name = "Main Street Store", ChainId = "chain_1" };
        expr(match).Should().BeTrue();

        var wrongChain = new Location { Name = "Main Street Store", ChainId = "chain_2" };
        expr(wrongChain).Should().BeFalse();

        var wrongName = new Location { Name = "Oak Avenue Store", ChainId = "chain_1" };
        expr(wrongName).Should().BeFalse();
    }

    [TestMethod]
    public void RequiredPermission_ReturnsLocationView()
    {
        var spec = new GetLocationsSpecification(10);
        spec.RequiredPermission.Should().Be(RetailPermissionKeys.LocationView);
    }

    [TestMethod]
    public void ApplySort_OrdersByStoreNumberThenId()
    {
        var spec = new GetLocationsSpecification(10);
        var locations = new List<Location>
        {
            new() { Id = "3", StoreNumber = "002", Name = "B", ChainId = "c1" },
            new() { Id = "1", StoreNumber = "001", Name = "A", ChainId = "c1" },
            new() { Id = "2", StoreNumber = "001", Name = "C", ChainId = "c1" }
        };

        var sorted = spec.ApplySort(locations.AsQueryable()).ToList();

        sorted[0].StoreNumber.Should().Be("001");
        sorted[0].Id.Should().Be("1");
        sorted[1].StoreNumber.Should().Be("001");
        sorted[1].Id.Should().Be("2");
        sorted[2].StoreNumber.Should().Be("002");
    }

    [TestMethod]
    public void BuildCursor_And_GetCursorFilter_RoundTrip()
    {
        var spec = new GetLocationsSpecification(10);
        var entity = new Location { Id = "loc_1", StoreNumber = "001", Name = "Store", ChainId = "c1" };

        var cursor = spec.BuildCursor(entity);
        cursor.Should().NotBeNullOrEmpty();

        var filter = spec.GetCursorFilter(cursor).Compile();

        // Items after "001/loc_1" should pass
        filter(new Location { Id = "loc_2", StoreNumber = "002", Name = "S", ChainId = "c1" }).Should().BeTrue();
        // Items before should not pass
        filter(new Location { Id = "loc_0", StoreNumber = "000", Name = "S", ChainId = "c1" }).Should().BeFalse();
    }
}
