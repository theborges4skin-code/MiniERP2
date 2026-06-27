using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 광고매핑(임시/조건부/예외) 규칙에 대한 데이터베이스 작업을 처리합니다. 일반 매핑
/// (MappingRepository)과 같은 구조지만 대상이 마스터SKU가 아니라 상품그룹입니다.
/// </summary>
public class AdMappingRepository
{
    // ===================== 임시 매핑 =====================

    public List<AdMappingRule> GetTempRules(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ChannelCode, Key, TargetGroup FROM AdRuleTemp WHERE ChannelCode = $channelCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);
        return ReadRules(command);
    }

    public List<AdMappingRule> GetAllTempRules()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ChannelCode, Key, TargetGroup FROM AdRuleTemp";
        return ReadRules(command);
    }

    /// <summary>같은 채널+키가 있으면 대상그룹을 갱신하고, 없으면 새로 추가합니다.</summary>
    public void UpsertTempRule(string channelCode, string key, string targetGroup)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var findCommand = connection.CreateCommand();
        findCommand.Transaction = transaction;
        findCommand.CommandText = "SELECT Id FROM AdRuleTemp WHERE ChannelCode = $channelCode AND Key = $key";
        findCommand.Parameters.AddWithValue("$channelCode", channelCode);
        findCommand.Parameters.AddWithValue("$key", key);
        var existingId = findCommand.ExecuteScalar();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (existingId != null)
        {
            command.CommandText = "UPDATE AdRuleTemp SET TargetGroup = $targetGroup WHERE Id = $id";
            command.Parameters.AddWithValue("$targetGroup", targetGroup);
            command.Parameters.AddWithValue("$id", existingId);
        }
        else
        {
            command.CommandText = "INSERT INTO AdRuleTemp (ChannelCode, Key, TargetGroup) VALUES ($channelCode, $key, $targetGroup)";
            command.Parameters.AddWithValue("$channelCode", channelCode);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$targetGroup", targetGroup);
        }
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void DeleteTempRule(long id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AdRuleTemp WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    // ===================== 조건부 매핑 =====================

    public List<AdMappingRule> GetConditionRules(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ChannelCode, Key, TargetGroup FROM AdRuleCondition WHERE ChannelCode = $channelCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);
        return ReadRules(command);
    }

    public List<(AdMappingRule Rule, List<AdConditionDetail> Details)> GetAllConditionRulesWithDetails()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ChannelCode, Key, TargetGroup FROM AdRuleCondition";
        var rules = ReadRules(command);
        return rules.Select(r => (r, GetConditionDetails(r.Id))).ToList();
    }

    public List<AdConditionDetail> GetConditionDetails(long ruleId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RuleId, HeaderField, Operator, TargetValue, Logic
            FROM AdRuleConditionDetail
            WHERE RuleId = $ruleId
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$ruleId", ruleId);

        var details = new List<AdConditionDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            details.Add(ReadConditionDetail(reader));
        }
        return details;
    }

    public long AddConditionRuleWithDetails(string channelCode, string key, string targetGroup, List<AdConditionDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var insertRuleCommand = connection.CreateCommand();
        insertRuleCommand.Transaction = transaction;
        insertRuleCommand.CommandText = "INSERT INTO AdRuleCondition (ChannelCode, Key, TargetGroup) VALUES ($channelCode, $key, $targetGroup)";
        insertRuleCommand.Parameters.AddWithValue("$channelCode", channelCode);
        insertRuleCommand.Parameters.AddWithValue("$key", key);
        insertRuleCommand.Parameters.AddWithValue("$targetGroup", targetGroup);
        insertRuleCommand.ExecuteNonQuery();

        using var lastIdCommand = connection.CreateCommand();
        lastIdCommand.Transaction = transaction;
        lastIdCommand.CommandText = "SELECT last_insert_rowid()";
        var ruleId = (long)lastIdCommand.ExecuteScalar()!;

        InsertConditionDetails(connection, transaction, ruleId, details);

        transaction.Commit();
        return ruleId;
    }

    public void UpdateConditionRuleSummary(long ruleId, string key, string targetGroup)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AdRuleCondition SET Key = $key, TargetGroup = $targetGroup WHERE Id = $ruleId";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$targetGroup", targetGroup);
        command.Parameters.AddWithValue("$ruleId", ruleId);
        command.ExecuteNonQuery();
    }

    public void ReplaceConditionDetails(long ruleId, List<AdConditionDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM AdRuleConditionDetail WHERE RuleId = $ruleId";
        deleteCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteCommand.ExecuteNonQuery();

        InsertConditionDetails(connection, transaction, ruleId, details);

        transaction.Commit();
    }

    public void DeleteConditionRule(long ruleId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var deleteDetailsCommand = connection.CreateCommand();
        deleteDetailsCommand.Transaction = transaction;
        deleteDetailsCommand.CommandText = "DELETE FROM AdRuleConditionDetail WHERE RuleId = $ruleId";
        deleteDetailsCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteDetailsCommand.ExecuteNonQuery();

        using var deleteRuleCommand = connection.CreateCommand();
        deleteRuleCommand.Transaction = transaction;
        deleteRuleCommand.CommandText = "DELETE FROM AdRuleCondition WHERE Id = $ruleId";
        deleteRuleCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteRuleCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private static void InsertConditionDetails(SqliteConnection connection, SqliteTransaction transaction, long ruleId, List<AdConditionDetail> details)
    {
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO AdRuleConditionDetail (RuleId, HeaderField, Operator, TargetValue, Logic)
            VALUES ($ruleId, $headerField, $operator, $targetValue, $logic)
            """;
        foreach (var detail in details)
        {
            if (string.IsNullOrWhiteSpace(detail.TargetValue) && detail.Operator != AdConditionOperator.IsZero) continue;

            insertCommand.Parameters.Clear();
            insertCommand.Parameters.AddWithValue("$ruleId", ruleId);
            insertCommand.Parameters.AddWithValue("$headerField", detail.HeaderField.ToString());
            insertCommand.Parameters.AddWithValue("$operator", detail.Operator.ToString());
            insertCommand.Parameters.AddWithValue("$targetValue", detail.TargetValue);
            insertCommand.Parameters.AddWithValue("$logic", detail.Logic.ToString());
            insertCommand.ExecuteNonQuery();
        }
    }

    // ===================== 예외 처리(행 필터) =====================

    public List<AdExceptionRule> GetExceptionRules(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, HeaderField, Operator, TargetValue
            FROM AdRuleException
            WHERE ChannelCode = $channelCode
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        return ReadExceptionRules(command);
    }

    public List<AdExceptionRule> GetAllExceptionRules()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ChannelCode, HeaderField, Operator, TargetValue FROM AdRuleException";
        return ReadExceptionRules(command);
    }

    public void AddExceptionRule(AdExceptionRule rule)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AdRuleException (ChannelCode, HeaderField, Operator, TargetValue)
            VALUES ($channelCode, $headerField, $operator, $targetValue)
            """;
        command.Parameters.AddWithValue("$channelCode", rule.ChannelCode);
        command.Parameters.AddWithValue("$headerField", rule.HeaderField.ToString());
        command.Parameters.AddWithValue("$operator", rule.Operator.ToString());
        command.Parameters.AddWithValue("$targetValue", rule.TargetValue);
        command.ExecuteNonQuery();
    }

    public void DeleteExceptionRule(long id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AdRuleException WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static List<AdMappingRule> ReadRules(SqliteCommand command)
    {
        var rules = new List<AdMappingRule>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rules.Add(new AdMappingRule
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                Key = reader.GetString(2),
                TargetGroup = reader.GetString(3),
            });
        }
        return rules;
    }

    private static List<AdExceptionRule> ReadExceptionRules(SqliteCommand command)
    {
        var rules = new List<AdExceptionRule>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rules.Add(new AdExceptionRule
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                HeaderField = Enum.Parse<AdStdField>(reader.GetString(2)),
                Operator = Enum.Parse<AdConditionOperator>(reader.GetString(3)),
                TargetValue = reader.GetString(4),
            });
        }
        return rules;
    }

    private static AdConditionDetail ReadConditionDetail(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        RuleId = reader.GetInt64(1),
        HeaderField = Enum.Parse<AdStdField>(reader.GetString(2)),
        Operator = Enum.Parse<AdConditionOperator>(reader.GetString(3)),
        TargetValue = reader.GetString(4),
        Logic = Enum.Parse<ConditionLogic>(reader.GetString(5)),
    };
}
