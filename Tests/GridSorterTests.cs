using System.Data;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class GridSorterTests
{
    private class Row
    {
        public string? Name { get; set; }
        public int Qty { get; set; }
    }

    [TestMethod]
    public void TrySort_PlainList_SortsByPropertyAscending()
    {
        var list = new List<Row> { new() { Name = "B", Qty = 2 }, new() { Name = "A", Qty = 1 }, new() { Name = "C", Qty = 3 } };

        var result = GridSorter.TrySort(list, nameof(Row.Name), ascending: true);

        Assert.IsTrue(result);
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, list.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void TrySort_PlainList_SortsByPropertyDescending()
    {
        var list = new List<Row> { new() { Qty = 1 }, new() { Qty = 3 }, new() { Qty = 2 } };

        var result = GridSorter.TrySort(list, nameof(Row.Qty), ascending: false);

        Assert.IsTrue(result);
        CollectionAssert.AreEqual(new[] { 3, 2, 1 }, list.Select(r => r.Qty).ToList());
    }

    [TestMethod]
    public void TrySort_BindingList_SortsInPlace()
    {
        var list = new System.ComponentModel.BindingList<Row> { new() { Qty = 5 }, new() { Qty = 1 } };

        var result = GridSorter.TrySort(list, nameof(Row.Qty), ascending: true);

        Assert.IsTrue(result);
        CollectionAssert.AreEqual(new[] { 1, 5 }, list.Select(r => r.Qty).ToList());
    }

    [TestMethod]
    public void TrySort_DataTable_SetsDefaultViewSort()
    {
        var table = new DataTable();
        table.Columns.Add("Qty", typeof(int));
        table.Rows.Add(2);
        table.Rows.Add(1);

        var result = GridSorter.TrySort(table, "Qty", ascending: true);

        Assert.IsTrue(result);
        Assert.AreEqual("[Qty] ASC", table.DefaultView.Sort);
    }

    [TestMethod]
    public void TrySort_UnknownProperty_ReturnsFalse()
    {
        var list = new List<Row> { new() { Qty = 1 } };

        var result = GridSorter.TrySort(list, "DoesNotExist", ascending: true);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TrySort_NullDataSource_ReturnsFalse()
    {
        Assert.IsFalse(GridSorter.TrySort(null, "Qty", ascending: true));
    }
}
