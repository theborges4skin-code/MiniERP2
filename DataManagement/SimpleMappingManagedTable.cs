using System.Data;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.DataManagement;

/// <summary>
/// 단순 매핑 규칙(1:1/임시/예외 — Key/TargetSku만 갖는 유형)을 데이터 관리창에서 다룰 수 있게
/// 합니다. 조건부 매핑(다중 상세조건)은 <see cref="ConditionalMappingManagedTable"/>을 쓰세요.
/// 자연키는 DB의 실제 기본키인 Id입니다 — RuleExact/RuleTemp/RuleException 어디에도 (ChannelCode,
/// Key) 유니크 제약이 없어 같은 채널에 같은 키가 여러 건 존재할 수 있다. 예전엔 (ChannelCode, Key)를
/// DataTable의 PrimaryKey로 잡아서, 끝 공백이나 대소문자만 다른 키가 있으면(DataTable의 문자열
/// 비교는 그 둘을 같은 값으로 본다) 데이터 관리창을 여는 순간 ConstraintException으로 창 전체가
/// 죽었다 — <see cref="ConditionalMappingManagedTable"/>에서 먼저 겪은 것과 같은 문제.
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
    public string[] KeyColumns => ["Id"];

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        var idColumn = table.Columns.Add("Id", typeof(long));
        idColumn.ReadOnly = true; // 사용자가 직접 편집하면 안 되는 내부 식별자 — 그리드가 자동으로 읽기전용 처리한다.
        table.Columns.Add("ChannelCode", typeof(string));
        table.Columns.Add("Key", typeof(string));
        table.Columns.Add("TargetSku", typeof(string));
        table.PrimaryKey = [idColumn];

        foreach (var rule in _repository.GetAllRules(_ruleType))
        {
            table.Rows.Add(rule.Id, rule.ChannelCode, rule.Key, rule.TargetSku);
        }
        table.AcceptChanges();
        return table;
    }

    public void Insert(DataRow row) =>
        _repository.UpsertRule(_ruleType, (string)row["ChannelCode"], (string)row["Key"], row["TargetSku"] as string ?? string.Empty);

    public void Update(DataRow row)
    {
        var id = Convert.ToInt64(row["Id", DataRowVersion.Original]);
        _repository.UpdateRule(_ruleType, id, (string)row["ChannelCode"], (string)row["Key"], row["TargetSku"] as string ?? string.Empty);
    }

    public void Delete(DataRow row)
    {
        var id = Convert.ToInt64(row["Id", DataRowVersion.Original]);
        _repository.DeleteRule(_ruleType, id);
    }
}
