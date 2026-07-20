using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>FBO(네이버 풀필먼트) 센터별 발주지 설정(FboChannelConfig)을 데이터 관리창에서 다룰 수 있게 합니다.</summary>
public class FboChannelConfigManagedTable : IManagedDataTable
{
    private readonly FboChannelConfigRepository _repository = new();

    public string DisplayName => "FBO 채널설정";
    public string[] KeyColumns => ["ChannelId"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("ChannelId", typeof(string));
        table.Columns.Add("ChannelName", typeof(string));
        table.Columns.Add("ReceiverName", typeof(string));
        table.Columns.Add("Phone", typeof(string));
        table.Columns.Add("Address", typeof(string));
        table.Columns.Add("ReceiverSeqFormat", typeof(string));
        table.Columns.Add("ChannelLabel", typeof(string));
        table.Columns.Add("OrderNoPrefix", typeof(string));
        table.Columns.Add("InboundType", typeof(string));
        table.Columns.Add("IsDefault", typeof(bool));
        table.PrimaryKey = [table.Columns["ChannelId"]!];

        foreach (var config in _repository.GetAll())
        {
            table.Rows.Add(config.ChannelId, config.ChannelName, config.ReceiverName, config.Phone,
                config.Address, config.ReceiverSeqFormat, config.ChannelLabel, config.OrderNoPrefix,
                config.InboundType, config.IsDefault);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) => Upsert(row);

    public void Update(DataRow row) => Upsert(row);

    public void Delete(DataRow row)
    {
        var channelId = (string)row["ChannelId", DataRowVersion.Original];
        _repository.Delete(channelId);
    }

    private void Upsert(DataRow row)
    {
        _repository.Upsert(new FboChannelConfigModel
        {
            ChannelId = (string)row["ChannelId"],
            ChannelName = row["ChannelName"] as string ?? string.Empty,
            ReceiverName = row["ReceiverName"] as string ?? string.Empty,
            Phone = row["Phone"] as string ?? string.Empty,
            Address = row["Address"] as string ?? string.Empty,
            ReceiverSeqFormat = string.IsNullOrEmpty(row["ReceiverSeqFormat"] as string) ? "{name}{seq:00}" : (string)row["ReceiverSeqFormat"],
            ChannelLabel = row["ChannelLabel"] as string ?? string.Empty,
            OrderNoPrefix = string.IsNullOrEmpty(row["OrderNoPrefix"] as string) ? "#FBO" : (string)row["OrderNoPrefix"],
            InboundType = string.IsNullOrEmpty(row["InboundType"] as string) ? "31" : (string)row["InboundType"],
            IsDefault = row["IsDefault"] is not DBNull && Convert.ToBoolean(row["IsDefault"]),
        });
    }
}
