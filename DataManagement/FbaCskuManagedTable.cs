using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>FBA(아마존 풀필먼트) 품목 마스터(FbaCskuMaster)를 데이터 관리창에서 다룰 수 있게 합니다.</summary>
public class FbaCskuManagedTable : IManagedDataTable
{
    private readonly FbaCskuRepository _repository = new();

    public string DisplayName => "FBA 품목마스터";
    public string[] KeyColumns => ["Csku"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("Csku", typeof(string));
        table.Columns.Add("ItemName", typeof(string));
        table.Columns.Add("InvoiceDisplayName", typeof(string));
        table.Columns.Add("Asin", typeof(string));
        table.Columns.Add("CommodityDescription", typeof(string));
        table.Columns.Add("HsCode", typeof(string));
        table.Columns.Add("ItemPrice", typeof(decimal));
        table.Columns.Add("Volume", typeof(string));
        table.Columns.Add("UnitWeightG", typeof(double));
        table.Columns.Add("QtyPerLayer", typeof(int));
        table.Columns.Add("MocraListingNo", typeof(string));
        table.Columns.Add("IsActive", typeof(bool));
        // FboCskuManagedTable과 동일한 이유 — 없으면 신규 행이 비활성으로 저장돼 CSKU 검색/선택창에
        // 노출되지 않는 문제가 재발한다.
        table.Columns["IsActive"]!.DefaultValue = true;
        table.PrimaryKey = [table.Columns["Csku"]!];

        foreach (var csku in _repository.GetAll())
        {
            table.Rows.Add(csku.Csku, csku.ItemName, csku.InvoiceDisplayName, csku.Asin, csku.CommodityDescription,
                csku.HsCode, csku.ItemPrice, csku.Volume, csku.UnitWeightG, csku.QtyPerLayer, csku.MocraListingNo, csku.IsActive);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) => Upsert(row);

    public void Update(DataRow row) => Upsert(row);

    public void Delete(DataRow row)
    {
        var csku = (string)row["Csku", DataRowVersion.Original];
        _repository.Delete(csku);
    }

    private void Upsert(DataRow row)
    {
        _repository.Upsert(new FbaCskuModel
        {
            Csku = (string)row["Csku"],
            ItemName = row["ItemName"] as string ?? string.Empty,
            InvoiceDisplayName = row["InvoiceDisplayName"] as string,
            Asin = row["Asin"] as string ?? string.Empty,
            CommodityDescription = row["CommodityDescription"] as string ?? string.Empty,
            HsCode = row["HsCode"] as string ?? string.Empty,
            ItemPrice = row["ItemPrice"] is DBNull ? 0m : Convert.ToDecimal(row["ItemPrice"]),
            Volume = row["Volume"] as string ?? string.Empty,
            UnitWeightG = row["UnitWeightG"] is DBNull ? 0 : Convert.ToDouble(row["UnitWeightG"]),
            QtyPerLayer = row["QtyPerLayer"] is DBNull ? 0 : Convert.ToInt32(row["QtyPerLayer"]),
            MocraListingNo = row["MocraListingNo"] as string ?? string.Empty,
            IsActive = row["IsActive"] is not DBNull && Convert.ToBoolean(row["IsActive"]),
        });
    }
}
