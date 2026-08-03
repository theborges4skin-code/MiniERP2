using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>
/// 조건부 매핑 규칙(RuleCondition + 다중 상세조건 RuleConditionDetail)을 데이터 관리창에서 다룰 수
/// 있게 합니다. 한 규칙이 여러 상세조건을 가질 수 있어, 엑셀 한 행 = 한 규칙으로 맞추기 위해
/// 상세조건 목록을 "Condition" 한 열에 직렬화한 문자열로 표현합니다
/// (형식: <c>AND ProductName Contains "셔츠" ; OR OptionName Contains "블루"</c>).
/// 자연키는 DB의 실제 기본키인 Id입니다 — (ChannelCode, Key)는 유니크 제약이 없고 Key는 사람이
/// 적는 설명 문구일 뿐이라 실제로 같은 채널에 같은 Key를 가진 규칙이 여러 개 있을 수 있다(조건부
/// 매핑 "중복 규칙 병합" 기능이 바로 이 상황을 다룬다). 예전엔 (ChannelCode, Key)를 DataTable의
/// PrimaryKey로 잘못 가정해서, 그런 중복이 있는 채널에서 데이터관리창을 열면
/// System.Data.ConstraintException으로 프로그램 전체가 죽었다.
/// </summary>
public class ConditionalMappingManagedTable : IManagedDataTable
{
    private readonly MappingRepository _repository = new();

    public string DisplayName => "조건부 매핑";
    public string[] KeyColumns => ["Id"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        var idColumn = table.Columns.Add("Id", typeof(long));
        idColumn.ReadOnly = true; // 사용자가 직접 편집하면 안 되는 내부 식별자 — 그리드가 자동으로 읽기전용 처리한다.
        table.Columns.Add("ChannelCode", typeof(string));
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add("TargetSku", typeof(string));
        table.Columns.Add("Condition", typeof(string));
        table.PrimaryKey = [idColumn];

        foreach (var (rule, details) in _repository.GetAllConditionRulesWithDetails())
        {
            table.Rows.Add(rule.Id, rule.ChannelCode, rule.Key, rule.TargetSku, SerializeConditions(details));
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
        var id = Convert.ToInt64(row["Id", DataRowVersion.Original]);
        var key = row["Key"] as string ?? string.Empty;
        var targetSku = row["TargetSku"] as string ?? string.Empty;
        var details = ParseConditions(row["Condition"] as string ?? string.Empty);
        _repository.UpdateConditionRuleSummary(id, key, targetSku);
        _repository.ReplaceConditionDetails(id, details);
    }

    public void Delete(DataRow row)
    {
        var id = Convert.ToInt64(row["Id", DataRowVersion.Original]);
        _repository.DeleteConditionRule(id);
    }

    /// <summary>
    /// HeaderField=Raw(정산파일 원본 열 조건)는 "Raw:실제열이름" 형태로 직렬화한다
    /// (예: <c>AND Raw:sku Contains "ABC123"</c>). 그래야 데이터관리창에서 내보내기/가져오기를
    /// 거쳐도 어떤 원본 열을 가리키는지 유실되지 않는다.
    /// </summary>
    private static string SerializeConditions(List<MappingConditionDetail> details) =>
        string.Join(" ; ", details.Select(d =>
        {
            var fieldToken = d.HeaderField == StdField.Raw ? $"Raw:{d.RawFieldName}" : d.HeaderField.ToString();
            return $"{d.Logic} {fieldToken} {d.Operator} \"{d.TargetValue}\"";
        }));

    private static List<MappingConditionDetail> ParseConditions(string text)
    {
        var details = new List<MappingConditionDetail>();
        if (string.IsNullOrWhiteSpace(text)) return details;

        foreach (var segment in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split(' ', 4, StringSplitOptions.TrimEntries);
            if (parts.Length < 4) continue;
            if (!Enum.TryParse<ConditionLogic>(parts[0], true, out var logic)) continue;
            if (!Enum.TryParse<ConditionOperator>(parts[2], true, out var op)) continue;

            var value = parts[3].Trim().Trim('"');

            string? rawFieldName = null;
            StdField field;
            if (parts[1].StartsWith("Raw:", StringComparison.OrdinalIgnoreCase))
            {
                field = StdField.Raw;
                rawFieldName = parts[1]["Raw:".Length..];
                if (string.IsNullOrWhiteSpace(rawFieldName)) continue;
            }
            else if (!Enum.TryParse(parts[1], true, out field))
            {
                continue;
            }

            details.Add(new MappingConditionDetail { HeaderField = field, RawFieldName = rawFieldName, Operator = op, TargetValue = value, Logic = logic });
        }
        return details;
    }
}
