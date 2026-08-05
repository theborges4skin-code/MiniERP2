using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>FBA 박스규격 마스터(FbaBoxSpec)를 데이터 관리창에서 다룰 수 있게 합니다.</summary>
public class FbaBoxSpecManagedTable : IManagedDataTable
{
    private readonly FbaBoxSpecRepository _repository = new();

    public string DisplayName => "FBA 박스규격";
    public string[] KeyColumns => ["BoxName"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("BoxName", typeof(string));
        table.Columns.Add("WidthMm", typeof(double));
        table.Columns.Add("DepthMm", typeof(double));
        table.Columns.Add("HeightMm", typeof(double));
        table.Columns.Add("SortOrder", typeof(int));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns["IsActive"]!.DefaultValue = true;
        table.PrimaryKey = [table.Columns["BoxName"]!];

        foreach (var spec in _repository.GetAll())
        {
            table.Rows.Add(spec.BoxName, spec.WidthMm, spec.DepthMm, spec.HeightMm, spec.SortOrder, spec.IsActive);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) => Upsert(row);

    public void Update(DataRow row) => Upsert(row);

    public void Delete(DataRow row)
    {
        var boxName = (string)row["BoxName", DataRowVersion.Original];
        _repository.Delete(boxName);
    }

    private void Upsert(DataRow row)
    {
        _repository.Upsert(new FbaBoxSpec
        {
            BoxName = (string)row["BoxName"],
            WidthMm = row["WidthMm"] is DBNull ? 0 : Convert.ToDouble(row["WidthMm"]),
            DepthMm = row["DepthMm"] is DBNull ? 0 : Convert.ToDouble(row["DepthMm"]),
            HeightMm = row["HeightMm"] is DBNull ? 0 : Convert.ToDouble(row["HeightMm"]),
            SortOrder = row["SortOrder"] is DBNull ? 0 : Convert.ToInt32(row["SortOrder"]),
            IsActive = row["IsActive"] is not DBNull && Convert.ToBoolean(row["IsActive"]),
        });
    }
}
