using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>
/// 단순 매핑 규칙(1:1/임시/예외 — Key/TargetSku만 갖는 유형)을 데이터 관리창에서 다룰 수 있게
/// 합니다. 조건부 매핑(다중 상세조건)은 <see cref="ConditionalMappingManagedTable"/>을 쓰세요.
/// </summary>
public class SimpleMappingManagedTable : IManagedDataTable
{
    private readonly MappingRepository _repository = new();
    private readonly MappingRuleType _ruleType;

    public SimpleMappingManagedTable(MappingRuleType ruleType, string displayName)
    {
        _ruleType = ruleType;
        DisplayName = displayName;
    }

    public string DisplayName { get; }
    public string[] KeyColumns => ["ChannelCode", "Key"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("ChannelCode", typeof(string));
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add("TargetSku", typeof(string));
        table.PrimaryKey = [table.Columns["ChannelCode"]!, table.Columns["Key"]!];

        foreach (var rule in _repository.GetAllRules(_ruleType))
        {
            table.Rows.Add(rule.ChannelCode, rule.Key, rule.TargetSku);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) => Upsert(row);

    public void Update(DataRow row) => Upsert(row);

    public void Delete(DataRow row)
    {
        var channelCode = (string)row["ChannelCode", DataRowVersion.Original];
        var key = (string)row["Key", DataRowVersion.Original];
        var existing = _repository.GetAllRules(_ruleType).FirstOrDefault(r => r.ChannelCode == channelCode && r.Key == key);
        if (existing != null) _repository.DeleteRule(_ruleType, existing.Id);
    }

    private void Upsert(DataRow row)
    {
        _repository.UpsertRule(_ruleType, (string)row["ChannelCode"], (string)row["Key"], row["TargetSku"] as string ?? string.Empty);
    }
}
