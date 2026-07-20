using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>CSKU(ChannelSkuTable)를 데이터 관리창에서 다룰 수 있게 합니다.</summary>
public class CskuManagedTable : IManagedDataTable
{
    private readonly ChannelSkuRepository _repository = new();

    public string DisplayName => "CSKU(채널별 SKU)";
    public string[] KeyColumns => ["ChannelCode", "CskuCode"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("ChannelCode", typeof(string));
        table.Columns.Add("CskuCode", typeof(string));
        table.Columns.Add("Msku", typeof(string));
        table.Columns.Add("SupplyPrice", typeof(decimal));
        table.Columns.Add("InvoiceDisplayName", typeof(string));
        table.Columns.Add("Unit", typeof(string));
        table.Columns.Add("Packing", typeof(string));
        table.Columns.Add("Note", typeof(string));
        table.PrimaryKey = [table.Columns["ChannelCode"]!, table.Columns["CskuCode"]!];

        foreach (var csku in _repository.GetAll())
        {
            table.Rows.Add(csku.ChannelCode, csku.CskuCode, csku.Msku, csku.SupplyPrice, csku.InvoiceDisplayName, csku.Unit, csku.Packing, csku.Note);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) => Upsert(row);

    public void Update(DataRow row) => Upsert(row);

    public void Delete(DataRow row)
    {
        var channelCode = (string)row["ChannelCode", DataRowVersion.Original];
        var cskuCode = (string)row["CskuCode", DataRowVersion.Original];
        _repository.Delete(channelCode, cskuCode);
    }

    private void Upsert(DataRow row)
    {
        _repository.Upsert(new ChannelSkuModel
        {
            ChannelCode = (string)row["ChannelCode"],
            CskuCode = (string)row["CskuCode"],
            Msku = row["Msku"] as string ?? string.Empty,
            SupplyPrice = row["SupplyPrice"] is DBNull ? 0m : Convert.ToDecimal(row["SupplyPrice"]),
            InvoiceDisplayName = row["InvoiceDisplayName"] as string,
            Unit = row["Unit"] as string is { Length: > 0 } unit ? unit : "kg",
            Packing = row["Packing"] as string,
            Note = row["Note"] as string,
        });
    }
}
