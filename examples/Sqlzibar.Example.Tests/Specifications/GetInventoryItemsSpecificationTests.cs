using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sqlzibar.Example.Api.Models;
using Sqlzibar.Example.Api.Seeding;
using Sqlzibar.Example.Api.Specifications;

namespace Sqlzibar.Example.Tests.Specifications;

[TestClass]
public class GetInventoryItemsSpecificationTests
{
    [TestMethod]
    public void ToExpression_WithSearch_FiltersByNameOrSku()
    {
        var spec = new GetInventoryItemsSpecification(10, "laptop");
        var expr = spec.ToExpression().Compile();

        var match = new InventoryItem { Name = "Laptop Pro", Sku = "ELEC-001", LocationId = "loc1" };
        expr(match).Should().BeTrue();

        var noMatch = new InventoryItem { Name = "Tablet", Sku = "ELEC-002", LocationId = "loc1" };
        expr(noMatch).Should().BeFalse();
    }

    [TestMethod]
    public void ToExpression_WithSkuSearch_Matches()
    {
        var spec = new GetInventoryItemsSpecification(10, "ELEC-001");
        var expr = spec.ToExpression().Compile();

        var match = new InventoryItem { Name = "Something", Sku = "ELEC-001", LocationId = "loc1" };
        expr(match).Should().BeTrue();
    }

    [TestMethod]
    public void ToExpression_WithLocationId_FiltersCorrectly()
    {
        var spec = new GetInventoryItemsSpecification(10, locationId: "loc_1");
        var expr = spec.ToExpression().Compile();

        var match = new InventoryItem { Name = "Item", Sku = "S1", LocationId = "loc_1" };
        expr(match).Should().BeTrue();

        var noMatch = new InventoryItem { Name = "Item", Sku = "S2", LocationId = "loc_2" };
        expr(noMatch).Should().BeFalse();
    }

    [TestMethod]
    public void RequiredPermission_ReturnsInventoryView()
    {
        var spec = new GetInventoryItemsSpecification(10);
        spec.RequiredPermission.Should().Be(RetailPermissionKeys.InventoryView);
    }

    [TestMethod]
    public void ApplySort_OrdersByNameThenId()
    {
        var spec = new GetInventoryItemsSpecification(10);
        var items = new List<InventoryItem>
        {
            new() { Id = "3", Name = "Zebra", Sku = "Z1", LocationId = "l1" },
            new() { Id = "1", Name = "Alpha", Sku = "A1", LocationId = "l1" },
            new() { Id = "2", Name = "Alpha", Sku = "A2", LocationId = "l1" }
        };

        var sorted = spec.ApplySort(items.AsQueryable()).ToList();

        sorted[0].Name.Should().Be("Alpha");
        sorted[0].Id.Should().Be("1");
        sorted[1].Name.Should().Be("Alpha");
        sorted[1].Id.Should().Be("2");
        sorted[2].Name.Should().Be("Zebra");
    }

    [TestMethod]
    public void BuildCursor_And_GetCursorFilter_RoundTrip()
    {
        var spec = new GetInventoryItemsSpecification(10);
        var entity = new InventoryItem { Id = "inv_1", Name = "Laptop", Sku = "S1", LocationId = "l1" };

        var cursor = spec.BuildCursor(entity);
        cursor.Should().NotBeNullOrEmpty();

        var filter = spec.GetCursorFilter(cursor).Compile();

        // Items after "Laptop/inv_1" should pass
        filter(new InventoryItem { Id = "inv_2", Name = "Phone", Sku = "S2", LocationId = "l1" }).Should().BeTrue();
        // Items before should not pass
        filter(new InventoryItem { Id = "inv_0", Name = "Alpha", Sku = "S0", LocationId = "l1" }).Should().BeFalse();
    }

    [TestMethod]
    public void ApplySort_NameDesc_OrdersByNameDescThenIdDesc()
    {
        var spec = new GetInventoryItemsSpecification(10, sortBy: "name", descending: true);
        var items = new List<InventoryItem>
        {
            new() { Id = "1", Name = "Alpha", Sku = "A1", LocationId = "l1" },
            new() { Id = "3", Name = "Zebra", Sku = "Z1", LocationId = "l1" },
            new() { Id = "2", Name = "Alpha", Sku = "A2", LocationId = "l1" }
        };

        var sorted = spec.ApplySort(items.AsQueryable()).ToList();

        sorted[0].Name.Should().Be("Zebra");
        sorted[1].Name.Should().Be("Alpha");
        sorted[1].Id.Should().Be("2");
        sorted[2].Name.Should().Be("Alpha");
        sorted[2].Id.Should().Be("1");
    }

    [TestMethod]
    public void ApplySort_PriceAsc_OrdersByPriceThenId()
    {
        var spec = new GetInventoryItemsSpecification(10, sortBy: "price");
        var items = new List<InventoryItem>
        {
            new() { Id = "1", Name = "Expensive", Sku = "E1", LocationId = "l1", Price = 999.99m },
            new() { Id = "2", Name = "Cheap", Sku = "C1", LocationId = "l1", Price = 9.99m },
            new() { Id = "3", Name = "Mid", Sku = "M1", LocationId = "l1", Price = 99.99m }
        };

        var sorted = spec.ApplySort(items.AsQueryable()).ToList();

        sorted[0].Price.Should().Be(9.99m);
        sorted[1].Price.Should().Be(99.99m);
        sorted[2].Price.Should().Be(999.99m);
    }

    [TestMethod]
    public void ApplySort_PriceDesc_OrdersByPriceDescThenIdDesc()
    {
        var spec = new GetInventoryItemsSpecification(10, sortBy: "price", descending: true);
        var items = new List<InventoryItem>
        {
            new() { Id = "1", Name = "Cheap", Sku = "C1", LocationId = "l1", Price = 9.99m },
            new() { Id = "2", Name = "Expensive", Sku = "E1", LocationId = "l1", Price = 999.99m },
            new() { Id = "3", Name = "Mid", Sku = "M1", LocationId = "l1", Price = 99.99m }
        };

        var sorted = spec.ApplySort(items.AsQueryable()).ToList();

        sorted[0].Price.Should().Be(999.99m);
        sorted[1].Price.Should().Be(99.99m);
        sorted[2].Price.Should().Be(9.99m);
    }

    [TestMethod]
    public void BuildCursor_PriceSort_And_GetCursorFilter_RoundTrip()
    {
        var spec = new GetInventoryItemsSpecification(10, sortBy: "price");
        var entity = new InventoryItem { Id = "inv_1", Name = "Mid", Sku = "M1", LocationId = "l1", Price = 99.99m };

        var cursor = spec.BuildCursor(entity);
        var filter = spec.GetCursorFilter(cursor).Compile();

        // More expensive item should pass (price asc)
        filter(new InventoryItem { Id = "inv_2", Name = "Expensive", Sku = "E1", LocationId = "l1", Price = 999.99m }).Should().BeTrue();
        // Cheaper item should not pass
        filter(new InventoryItem { Id = "inv_0", Name = "Cheap", Sku = "C1", LocationId = "l1", Price = 9.99m }).Should().BeFalse();
    }

    [TestMethod]
    public void BuildCursor_PriceDescSort_And_GetCursorFilter_RoundTrip()
    {
        var spec = new GetInventoryItemsSpecification(10, sortBy: "price", descending: true);
        var entity = new InventoryItem { Id = "inv_1", Name = "Mid", Sku = "M1", LocationId = "l1", Price = 99.99m };

        var cursor = spec.BuildCursor(entity);
        var filter = spec.GetCursorFilter(cursor).Compile();

        // Cheaper item should pass (price desc — next page goes lower)
        filter(new InventoryItem { Id = "inv_0", Name = "Cheap", Sku = "C1", LocationId = "l1", Price = 9.99m }).Should().BeTrue();
        // More expensive item should not pass
        filter(new InventoryItem { Id = "inv_2", Name = "Expensive", Sku = "E1", LocationId = "l1", Price = 999.99m }).Should().BeFalse();
    }

    [TestMethod]
    public void BuildCursor_NameDescSort_And_GetCursorFilter_RoundTrip()
    {
        var spec = new GetInventoryItemsSpecification(10, sortBy: "name", descending: true);
        var entity = new InventoryItem { Id = "inv_1", Name = "Laptop", Sku = "L1", LocationId = "l1" };

        var cursor = spec.BuildCursor(entity);
        var filter = spec.GetCursorFilter(cursor).Compile();

        // "Alpha" comes after "Laptop" in desc order (lower value = later page)
        filter(new InventoryItem { Id = "inv_0", Name = "Alpha", Sku = "A1", LocationId = "l1" }).Should().BeTrue();
        // "Zebra" should not pass (higher than Laptop in desc = already seen)
        filter(new InventoryItem { Id = "inv_2", Name = "Zebra", Sku = "Z1", LocationId = "l1" }).Should().BeFalse();
    }
}
