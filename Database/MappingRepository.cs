using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// SKU 매핑 규칙에 대한 데이터베이스 작업을 처리합니다.
/// </summary>
public class MappingRepository
{
    public List<MappingRule> GetRules(MappingRuleType ruleType, string channelCode)
    {
        var tableName = GetTableName(ruleType);
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, ChannelCode, Key, TargetSku
            FROM {tableName}
            WHERE ChannelCode = $channelCode
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var rules = new List<MappingRule>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rules.Add(new MappingRule
            {
                Id = reader.GetInt64(0),
                RuleType = ruleType,
                ChannelCode = reader.GetString(1),
                Key = reader.GetString(2),
                TargetSku = reader.GetString(3),
            });
        }
        return rules;
    }

    public void SaveRules(MappingRuleType ruleType, string channelCode, IEnumerable<MappingRule> rules)
    {
        var tableName = GetTableName(ruleType);
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        // 조건부 매핑은 다중 상세조건(RuleConditionDetail)을 가질 수 있다. 부모 규칙을
        // 삭제하기 전에, 매핑관리창의 단순 그리드 저장으로 고아가 될 상세조건도 함께 정리한다.
        if (ruleType == MappingRuleType.Condition)
        {
            using var deleteDetailsCommand = connection.CreateCommand();
            deleteDetailsCommand.Transaction = transaction;
            deleteDetailsCommand.CommandText = $"""
                DELETE FROM RuleConditionDetail
                WHERE RuleId IN (SELECT Id FROM {tableName} WHERE ChannelCode = $channelCode)
                """;
            deleteDetailsCommand.Parameters.AddWithValue("$channelCode", channelCode);
            deleteDetailsCommand.ExecuteNonQuery();
        }

        // 1. 기존 채널의 모든 규칙 삭제
        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = $"DELETE FROM {tableName} WHERE ChannelCode = $channelCode";
        deleteCommand.Parameters.AddWithValue("$channelCode", channelCode);
        deleteCommand.ExecuteNonQuery();

        // 2. 새 규칙 삽입
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = $"INSERT INTO {tableName} (ChannelCode, Key, TargetSku) VALUES ($channelCode, $key, $targetSku)";
        
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Key)) continue;
            
            insertCommand.Parameters.Clear();
            insertCommand.Parameters.AddWithValue("$channelCode", channelCode);
            insertCommand.Parameters.AddWithValue("$key", rule.Key);
            insertCommand.Parameters.AddWithValue("$targetSku", rule.TargetSku);
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 지정된 조건부 매핑 규칙(RuleId)에 속한 상세 조건 목록을 가져옵니다.
    /// </summary>
    public List<MappingConditionDetail> GetConditionDetails(long ruleId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RuleId, HeaderField, Operator, TargetValue, Logic
            FROM RuleConditionDetail
            WHERE RuleId = $ruleId
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$ruleId", ruleId);

        var details = new List<MappingConditionDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            details.Add(new MappingConditionDetail
            {
                Id = reader.GetInt64(0),
                RuleId = reader.GetInt64(1),
                HeaderField = Enum.Parse<StdField>(reader.GetString(2)),
                Operator = Enum.Parse<ConditionOperator>(reader.GetString(3)),
                TargetValue = reader.GetString(4),
                Logic = Enum.Parse<ConditionLogic>(reader.GetString(5)),
            });
        }
        return details;
    }

    /// <summary>
    /// 지정된 채널의 조건부 매핑 규칙에 속한 모든 상세조건을 한 번에 가져와 RuleId별로 묶어 반환합니다.
    /// SkuMapper가 규칙마다 따로 조회하지 않도록 묶어서 제공합니다.
    /// </summary>
    public Dictionary<long, List<MappingConditionDetail>> GetConditionDetailsByChannel(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.Id, d.RuleId, d.HeaderField, d.Operator, d.TargetValue, d.Logic
            FROM RuleConditionDetail d
            JOIN RuleCondition r ON r.Id = d.RuleId
            WHERE r.ChannelCode = $channelCode
            ORDER BY d.RuleId, d.Id
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var result = new Dictionary<long, List<MappingConditionDetail>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var detail = new MappingConditionDetail
            {
                Id = reader.GetInt64(0),
                RuleId = reader.GetInt64(1),
                HeaderField = Enum.Parse<StdField>(reader.GetString(2)),
                Operator = Enum.Parse<ConditionOperator>(reader.GetString(3)),
                TargetValue = reader.GetString(4),
                Logic = Enum.Parse<ConditionLogic>(reader.GetString(5)),
            };

            if (!result.TryGetValue(detail.RuleId, out var list))
            {
                list = new List<MappingConditionDetail>();
                result[detail.RuleId] = list;
            }
            list.Add(detail);
        }
        return result;
    }

    /// <summary>
    /// 다중 상세조건을 가진 조건부 매핑 규칙 1건을 새로 추가합니다(레거시 마이그레이션 등에서 사용).
    /// 기존 단순 조건부 규칙(매핑관리창의 단일 Key)과 공존하며, SaveRules(Condition)을 호출하면
    /// 채널 전체의 조건부 규칙(이 규칙 포함)이 함께 삭제될 수 있으니 주의해야 한다.
    /// </summary>
    public long AddConditionRuleWithDetails(string channelCode, string key, string targetSku, List<MappingConditionDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var insertRuleCommand = connection.CreateCommand();
        insertRuleCommand.Transaction = transaction;
        insertRuleCommand.CommandText = "INSERT INTO RuleCondition (ChannelCode, Key, TargetSku) VALUES ($channelCode, $key, $targetSku)";
        insertRuleCommand.Parameters.AddWithValue("$channelCode", channelCode);
        insertRuleCommand.Parameters.AddWithValue("$key", key);
        insertRuleCommand.Parameters.AddWithValue("$targetSku", targetSku);
        insertRuleCommand.ExecuteNonQuery();

        using var lastIdCommand = connection.CreateCommand();
        lastIdCommand.Transaction = transaction;
        lastIdCommand.CommandText = "SELECT last_insert_rowid()";
        var ruleId = (long)lastIdCommand.ExecuteScalar()!;

        using var insertDetailCommand = connection.CreateCommand();
        insertDetailCommand.Transaction = transaction;
        insertDetailCommand.CommandText = """
            INSERT INTO RuleConditionDetail (RuleId, HeaderField, Operator, TargetValue, Logic)
            VALUES ($ruleId, $headerField, $operator, $targetValue, $logic)
            """;
        foreach (var detail in details)
        {
            insertDetailCommand.Parameters.Clear();
            insertDetailCommand.Parameters.AddWithValue("$ruleId", ruleId);
            insertDetailCommand.Parameters.AddWithValue("$headerField", detail.HeaderField.ToString());
            insertDetailCommand.Parameters.AddWithValue("$operator", detail.Operator.ToString());
            insertDetailCommand.Parameters.AddWithValue("$targetValue", detail.TargetValue);
            insertDetailCommand.Parameters.AddWithValue("$logic", detail.Logic.ToString());
            insertDetailCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return ruleId;
    }

    /// <summary>
    /// 1:1 매핑 규칙을 추가하거나(같은 채널+키가 없으면) 기존 규칙의 TargetSku를 갱신합니다.
    /// SKU 매핑 도우미에서 사용자가 한 번 매핑을 선택하면 같은 상품명/옵션명 조합이 다음
    /// 발주서에서도 자동으로 매핑되도록, 매번 다시 골라야 하는 수고를 없애기 위함입니다.
    /// </summary>
    public void UpsertExactRule(string channelCode, string key, string targetSku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var findCommand = connection.CreateCommand();
        findCommand.Transaction = transaction;
        findCommand.CommandText = "SELECT Id FROM RuleExact WHERE ChannelCode = $channelCode AND Key = $key";
        findCommand.Parameters.AddWithValue("$channelCode", channelCode);
        findCommand.Parameters.AddWithValue("$key", key);
        var existingId = findCommand.ExecuteScalar();

        if (existingId != null)
        {
            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = "UPDATE RuleExact SET TargetSku = $targetSku WHERE Id = $id";
            updateCommand.Parameters.AddWithValue("$targetSku", targetSku);
            updateCommand.Parameters.AddWithValue("$id", existingId);
            updateCommand.ExecuteNonQuery();
        }
        else
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = "INSERT INTO RuleExact (ChannelCode, Key, TargetSku) VALUES ($channelCode, $key, $targetSku)";
            insertCommand.Parameters.AddWithValue("$channelCode", channelCode);
            insertCommand.Parameters.AddWithValue("$key", key);
            insertCommand.Parameters.AddWithValue("$targetSku", targetSku);
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 조건부 매핑 규칙의 요약 정보(Key/TargetSku)만 갱신합니다. 상세조건은 건드리지 않습니다.
    /// </summary>
    public void UpdateConditionRuleSummary(long ruleId, string key, string targetSku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE RuleCondition SET Key = $key, TargetSku = $targetSku WHERE Id = $ruleId";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$targetSku", targetSku);
        command.Parameters.AddWithValue("$ruleId", ruleId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 조건부 매핑 규칙 1건과 그에 속한 모든 상세조건을 삭제합니다.
    /// </summary>
    public void DeleteConditionRule(long ruleId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var deleteDetailsCommand = connection.CreateCommand();
        deleteDetailsCommand.Transaction = transaction;
        deleteDetailsCommand.CommandText = "DELETE FROM RuleConditionDetail WHERE RuleId = $ruleId";
        deleteDetailsCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteDetailsCommand.ExecuteNonQuery();

        using var deleteRuleCommand = connection.CreateCommand();
        deleteRuleCommand.Transaction = transaction;
        deleteRuleCommand.CommandText = "DELETE FROM RuleCondition WHERE Id = $ruleId";
        deleteRuleCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteRuleCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    /// <summary>
    /// 지정된 규칙(RuleId)의 상세조건을 모두 지우고 새 목록으로 교체합니다.
    /// </summary>
    public void ReplaceConditionDetails(long ruleId, List<MappingConditionDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM RuleConditionDetail WHERE RuleId = $ruleId";
        deleteCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteCommand.ExecuteNonQuery();

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO RuleConditionDetail (RuleId, HeaderField, Operator, TargetValue, Logic)
            VALUES ($ruleId, $headerField, $operator, $targetValue, $logic)
            """;
        foreach (var detail in details)
        {
            if (string.IsNullOrWhiteSpace(detail.TargetValue)) continue;

            insertCommand.Parameters.Clear();
            insertCommand.Parameters.AddWithValue("$ruleId", ruleId);
            insertCommand.Parameters.AddWithValue("$headerField", detail.HeaderField.ToString());
            insertCommand.Parameters.AddWithValue("$operator", detail.Operator.ToString());
            insertCommand.Parameters.AddWithValue("$targetValue", detail.TargetValue);
            insertCommand.Parameters.AddWithValue("$logic", detail.Logic.ToString());
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static string GetTableName(MappingRuleType ruleType) => ruleType switch
    {
        MappingRuleType.Exception => "RuleException",
        MappingRuleType.Exact => "RuleExact",
        MappingRuleType.Temp => "RuleTemp",
        MappingRuleType.Condition => "RuleCondition",
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), $"Unsupported rule type: {ruleType}")
    };
}