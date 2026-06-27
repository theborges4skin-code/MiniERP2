using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>
/// 조건부 매핑 규칙(RuleCondition + 다중 상세조건 RuleConditionDetail)을 데이터 관리창에서 다룰 수
/// 있게 합니다. 한 규칙이 여러 상세조건을 가질 수 있어, 엑셀 한 행 = 한 규칙으로 맞추기 위해
/// 상세조건 목록을 "Condition" 한 열에 직렬화한 문자열로 표현합니다
/// (형식: <c>AND ProductName Contains "셔츠" ; OR OptionName Contains "블루"</c>).
/// 자연키는 (ChannelCode, Key)입니다 — DB에 유니크 제약은 없지만, 이 관리창에서 일괄편집할 때는
/// 채널 안에서 Key를 규칙을 구분하는 고유 설명으로 쓴다고 가정합니다.
/// </summary>
public class ConditionalMappingManagedTable : IManagedDataTable
{
    private readonly MappingRepository _repository = new();

    public string DisplayName => "조건부 매핑";
    public string[] KeyColumns => ["ChannelCode", "Key"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        table.Columns.Add("ChannelCode", typeof(string));
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add("TargetSku", typeof(string));
        table.Columns.Add("Condition", typeof(string));
        table.PrimaryKey = [table.Columns["ChannelCode"]!, table.Columns["Key"]!];

        foreach (var (rule, details) in _repository.GetAllConditionRulesWithDetails())
        {
            table.Rows.Add(rule.ChannelCode, rule.Key, rule.TargetSku, SerializeConditions(details));
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row)
    {
        var channelCode = (string)row["ChannelCode"];
        var key = row["Key"] as string ?? string.Empty;
        var targetSku = row["TargetSku"] as string ?? string.Empty;
        var details = ParseConditions(row["Condition"] as string ?? string.Empty);
        _repository.AddConditionRuleWithDetails(channelCode, key, targetSku, details);
    }

    public void Update(DataRow row)
    {
        var channelCode = (string)row["ChannelCode"];
        var key = row["Key"] as string ?? string.Empty;
        var existing = FindExisting(channelCode, key);
        if (existing is null)
        {
            // 매칭되는 기존 규칙을 못 찾으면(동시 편집 등으로 이미 삭제된 경우) 새로 만든다.
            Insert(row);
            return;
        }

        var targetSku = row["TargetSku"] as string ?? string.Empty;
        var details = ParseConditions(row["Condition"] as string ?? string.Empty);
        _repository.UpdateConditionRuleSummary(existing.Value.Rule.Id, key, targetSku);
        _repository.ReplaceConditionDetails(existing.Value.Rule.Id, details);
    }

    public void Delete(DataRow row)
    {
        var channelCode = (string)row["ChannelCode", DataRowVersion.Original];
        var key = row["Key", DataRowVersion.Original] as string ?? string.Empty;
        var existing = FindExisting(channelCode, key);
        if (existing != null) _repository.DeleteConditionRule(existing.Value.Rule.Id);
    }

    private (MappingRule Rule, List<MappingConditionDetail> Details)? FindExisting(string channelCode, string key)
    {
        var all = _repository.GetAllConditionRulesWithDetails();
        foreach (var entry in all)
        {
            if (entry.Rule.ChannelCode == channelCode && entry.Rule.Key == key) return entry;
        }
        return null;
    }

    private static string SerializeConditions(List<MappingConditionDetail> details) =>
        string.Join(" ; ", details.Select(d => $"{d.Logic} {d.HeaderField} {d.Operator} \"{d.TargetValue}\""));

    private static List<MappingConditionDetail> ParseConditions(string text)
    {
        var details = new List<MappingConditionDetail>();
        if (string.IsNullOrWhiteSpace(text)) return details;

        foreach (var segment in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split(' ', 4, StringSplitOptions.TrimEntries);
            if (parts.Length < 4) continue;
            if (!Enum.TryParse<ConditionLogic>(parts[0], true, out var logic)) continue;
            if (!Enum.TryParse<StdField>(parts[1], true, out var field)) continue;
            if (!Enum.TryParse<ConditionOperator>(parts[2], true, out var op)) continue;

            var value = parts[3].Trim().Trim('"');
            details.Add(new MappingConditionDetail { HeaderField = field, Operator = op, TargetValue = value, Logic = logic });
        }
        return details;
    }
}
