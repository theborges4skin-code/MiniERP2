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

    private static string GetTableName(MappingRuleType ruleType) => ruleType switch
    {
        MappingRuleType.Exception => "RuleException",
        MappingRuleType.Exact => "RuleExact",
        MappingRuleType.Temp => "RuleTemp",
        MappingRuleType.Condition => "RuleCondition",
        _ => throw new ArgumentOutOfRangeException(nameof(ruleType), $"Unsupported rule type: {ruleType}")
    };
}