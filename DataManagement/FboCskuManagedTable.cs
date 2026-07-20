using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>FBO(네이버 풀필먼트) 품목 마스터(FboCskuMaster)를 데이터 관리창에서 다룰 수 있게 합니다.</summary>
public class FboCskuManagedTable : IManagedDataTable
{
    private readonly FboCskuRepository _repository = new();

    public string DisplayName => "FBO 품목마스터";
    public string[] KeyColumns => ["Csku"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("Csku", typeof(string));
        table.Columns.Add("FboItemCode", typeof(string));
        table.Columns.Add("ItemName", typeof(string));
        table.Columns.Add("InvoiceDisplayName", typeof(string));
        table.Columns.Add("QtyPerBox", typeof(int));
        table.Columns.Add("BoxType", typeof(string));
        table.Columns.Add("FreightType", typeof(string));
        table.Columns.Add("IsActive", typeof(bool));
        // FboCskuModel의 기본값(신규 품목은 활성)과 맞춘다 — 이 DefaultValue가 없으면 그리드에서
        // 새 행을 추가할 때 체크박스가 꺼진 채로 시작해, 사용자가 직접 체크하지 않으면 CSKU 검색/
        // 선택창(둘 다 IsActive=true만 보여줌)에 전혀 노출되지 않는 채로 저장되는 문제가 있었다.
        table.Columns["IsActive"]!.DefaultValue = true;
        table.PrimaryKey = [table.Columns["Csku"]!];

        foreach (var csku in _repository.GetAll())
        {
            table.Rows.Add(csku.Csku, csku.FboItemCode, csku.ItemName, csku.InvoiceDisplayName, csku.QtyPerBox, csku.BoxType, csku.FreightType, csku.IsActive);
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
        _repository.Upsert(new FboCskuModel
        {
            Csku = (string)row["Csku"],
            FboItemCode = row["FboItemCode"] as string ?? string.Empty,
            ItemName = row["ItemName"] as string ?? string.Empty,
            InvoiceDisplayName = row["InvoiceDisplayName"] as string,
            QtyPerBox = row["QtyPerBox"] is DBNull ? 0 : Convert.ToInt32(row["QtyPerBox"]),
            BoxType = row["BoxType"] as string ?? "소",
            FreightType = row["FreightType"] as string,
            IsActive = row["IsActive"] is not DBNull && Convert.ToBoolean(row["IsActive"]),
        });
    }
}
