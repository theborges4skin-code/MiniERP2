using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>FBA 발주지(수취지) 설정(FbaConfig)을 데이터 관리창에서 다룰 수 있게 합니다.</summary>
public class FbaConfigManagedTable : IManagedDataTable
{
    private readonly FbaConfigRepository _repository = new();

    public string DisplayName => "FBA 발주지설정";
    public string[] KeyColumns => ["ConfigKey"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("ConfigKey", typeof(string));
        table.Columns.Add("ReceiverName", typeof(string));
        table.Columns.Add("Phone", typeof(string));
        table.Columns.Add("Phone2", typeof(string));
        table.Columns.Add("Address", typeof(string));
        table.Columns.Add("DeliveryMessage", typeof(string));
        table.Columns.Add("BoxTypeLabel", typeof(string));
        table.Columns.Add("TransferType", typeof(string));
        table.Columns.Add("Etc1", typeof(string));
        table.Columns.Add("OrderNoPrefix", typeof(string));
        table.PrimaryKey = [table.Columns["ConfigKey"]!];

        // 수취지가 1곳 고정(ConfigKey="DEFAULT")이라, 아직 저장된 적이 없어도 편집 가능한 기본
        // 행 1개를 항상 보여준다(FboChannelConfig처럼 다건 등록하는 화면이 아니므로 빈 그리드로
        // 시작하면 첫 사용자가 새 행 추가 버튼부터 찾아야 하는 불편이 있다).
        var configs = _repository.GetAll();
        if (configs.Count == 0)
        {
            configs.Add(new FbaConfigModel());
        }
        foreach (var config in configs)
        {
            table.Rows.Add(config.ConfigKey, config.ReceiverName, config.Phone, config.Phone2, config.Address,
                config.DeliveryMessage, config.BoxTypeLabel, config.TransferType, config.Etc1, config.OrderNoPrefix);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) => Upsert(row);

    public void Update(DataRow row) => Upsert(row);

    public void Delete(DataRow row)
    {
        var configKey = (string)row["ConfigKey", DataRowVersion.Original];
        _repository.Delete(configKey);
    }

    private void Upsert(DataRow row)
    {
        _repository.Upsert(new FbaConfigModel
        {
            ConfigKey = string.IsNullOrEmpty(row["ConfigKey"] as string) ? FbaConfigModel.DefaultConfigKey : (string)row["ConfigKey"],
            ReceiverName = row["ReceiverName"] as string ?? string.Empty,
            Phone = row["Phone"] as string ?? string.Empty,
            Phone2 = row["Phone2"] as string ?? string.Empty,
            Address = row["Address"] as string ?? string.Empty,
            DeliveryMessage = row["DeliveryMessage"] as string ?? string.Empty,
            BoxTypeLabel = string.IsNullOrEmpty(row["BoxTypeLabel"] as string) ? "중" : (string)row["BoxTypeLabel"],
            TransferType = row["TransferType"] as string ?? string.Empty,
            Etc1 = row["Etc1"] as string ?? string.Empty,
            OrderNoPrefix = string.IsNullOrEmpty(row["OrderNoPrefix"] as string) ? "#FBA" : (string)row["OrderNoPrefix"],
        });
    }
}
