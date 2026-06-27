using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>마스터SKU(ItemTable)를 데이터 관리창에서 다룰 수 있게 합니다.</summary>
public class MasterSkuManagedTable : IManagedDataTable
{
    private readonly ItemRepository _repository = new();

    public string DisplayName => "마스터SKU";
    public string[] KeyColumns => ["Sku"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("Sku", typeof(string));
        table.Columns.Add("ItemName", typeof(string));
        table.Columns.Add("CostPrice", typeof(decimal));
        table.Columns.Add("ProductGroup", typeof(string));
        table.Columns.Add("Reserve1", typeof(string));
        table.Columns.Add("Reserve2", typeof(string));
        table.Columns.Add("Reserve3", typeof(string));
        table.PrimaryKey = [table.Columns["Sku"]!];

        foreach (var item in _repository.GetAll())
        {
            table.Rows.Add(item.Sku, item.ItemName, item.CostPrice, item.ProductGroup, item.Reserve1, item.Reserve2, item.Reserve3);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) => Upsert(row);

    public void Update(DataRow row) => Upsert(row);

    public void Delete(DataRow row)
    {
        var sku = (string)row["Sku", DataRowVersion.Original];
        _repository.Delete(sku);
    }

    private void Upsert(DataRow row)
    {
        _repository.Upsert(new ItemModel
        {
            Sku = (string)row["Sku"],
            ItemName = row["ItemName"] as string ?? string.Empty,
            CostPrice = ToDecimal(row["CostPrice"]),
            ProductGroup = row["ProductGroup"] as string,
            Reserve1 = row["Reserve1"] as string,
            Reserve2 = row["Reserve2"] as string,
            Reserve3 = row["Reserve3"] as string,
        });
    }

    private static decimal ToDecimal(object value) => value is DBNull or null ? 0m : Convert.ToDecimal(value);
}
