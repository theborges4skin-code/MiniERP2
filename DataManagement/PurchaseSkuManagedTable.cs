using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>매입SKU(PurchaseSkuTable)를 데이터 관리창에서 다룰 수 있게 합니다. 매출측 CskuManagedTable과 대칭입니다.</summary>
public class PurchaseSkuManagedTable : IManagedDataTable
{
    private readonly PurchaseSkuRepository _repository = new();

    public string DisplayName => "매입SKU(B2B)";
    public string[] KeyColumns => ["ChannelCode", "Msku"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("ChannelCode", typeof(string));
        table.Columns.Add("Msku", typeof(string));
        table.Columns.Add("PurchasePrice", typeof(decimal));
        table.Columns.Add("Unit", typeof(string));
        table.Columns.Add("Note", typeof(string));
        table.PrimaryKey = [table.Columns["ChannelCode"]!, table.Columns["Msku"]!];

        foreach (var sku in _repository.GetAll())
        {
            table.Rows.Add(sku.ChannelCode, sku.Msku, sku.PurchasePrice, sku.Unit, sku.Note);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) => Upsert(row);

    public void Update(DataRow row) => Upsert(row);

    public void Delete(DataRow row)
    {
        var channelCode = (string)row["ChannelCode", DataRowVersion.Original];
        var msku = (string)row["Msku", DataRowVersion.Original];
        _repository.Delete(channelCode, msku);
    }

    private void Upsert(DataRow row)
    {
        _repository.Upsert(new PurchaseSkuModel
        {
            ChannelCode = (string)row["ChannelCode"],
            Msku = (string)row["Msku"],
            PurchasePrice = row["PurchasePrice"] is DBNull ? 0m : Convert.ToDecimal(row["PurchasePrice"]),
            Unit = row["Unit"] as string is { Length: > 0 } unit ? unit : "kg",
            Note = row["Note"] as string,
        });
    }
}
