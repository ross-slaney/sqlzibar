using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Example.Api.Specifications;

namespace Sqlzibar.Example.Tests.Specifications;

[TestClass]
public class GetChainsSpecificationTests
{
    [TestMethod]
    public void ToExpression_WithSearch_FiltersByNameOrDescription()
    {
        var spec = new GetChainsSpecification(10, "walmart");
        var expr = spec.ToExpression().Compile();

        var chain = new Chain { Name = "Walmart", Description = "Retail" };
        expr(chain).Should().BeTrue();

        var other = new Chain { Name = "Target", Description = "Discount retail" };
        expr(other).Should().BeFalse();
    }

    [TestMethod]
    public void ToExpression_WithSearchMatchingDescription_ReturnsTrue()
    {
        var spec = new GetChainsSpecification(10, "discount");
        var expr = spec.ToExpression().Compile();

        var chain = new Chain { Name = "Target", Description = "Discount retail" };
        expr(chain).Should().BeTrue();
    }

    [TestMethod]
    public void ToExpression_WithoutSearch_ReturnsAll()
    {
        var spec = new GetChainsSpecification(10);
        var expr = spec.ToExpression().Compile();

        var chain = new Chain { Name = "Any Chain" };
        expr(chain).Should().BeTrue();
    }

    [TestMethod]
    public void RequiredPermission_ReturnsChainView()
    {
        var spec = new GetChainsSpecification(10);
        spec.RequiredPermission.Should().Be(RetailPermissionKeys.ChainView);
    }

    [TestMethod]
    public void ApplySort_OrdersByNameThenId()
    {
        var spec = new GetChainsSpecification(10);
        var chains = new List<Chain>
        {
            new() { Id = "2", Name = "Zebra" },
            new() { Id = "1", Name = "Alpha" },
            new() { Id = "3", Name = "Alpha" }
        };

        var sorted = spec.ApplySort(chains.AsQueryable()).ToList();

        sorted[0].Name.Should().Be("Alpha");
        sorted[0].Id.Should().Be("1");
        sorted[1].Name.Should().Be("Alpha");
        sorted[1].Id.Should().Be("3");
        sorted[2].Name.Should().Be("Zebra");
    }

    [TestMethod]
    public void BuildCursor_And_GetCursorFilter_RoundTrip()
    {
        var spec = new GetChainsSpecification(10);
        var entity = new Chain { Id = "chain_1", Name = "Walmart" };

        var cursor = spec.BuildCursor(entity);
        cursor.Should().NotBeNullOrEmpty();

        var filter = spec.GetCursorFilter(cursor).Compile();

        // Items after "Walmart/chain_1" should pass
        filter(new Chain { Id = "chain_2", Name = "Zebra" }).Should().BeTrue();
        // Items before should not pass
        filter(new Chain { Id = "chain_0", Name = "Alpha" }).Should().BeFalse();
        // Same sort key + same id should not pass (cursor is exclusive)
        filter(new Chain { Id = "chain_1", Name = "Walmart" }).Should().BeFalse();
    }

    [TestMethod]
    public void PageSize_SetsCorrectValue()
    {
        var spec = new GetChainsSpecification(25);
        spec.PageSize.Should().Be(25);
    }
}
