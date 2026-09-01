using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 광고비 채널 분리(AdChannelSplit_Spec.md §4) 규칙에 대한 데이터베이스 작업을 처리합니다.
/// AdMappingRepository의 조건부 매핑과 같은 구조지만 대상이 상품그룹이 아니라 하위채널입니다.
/// </summary>
public class AdChannelSplitRepository
{
    // ===================== 채널 분리 사용 여부 설정 =====================
    // ChannelConfig(channels_config.json)에 두면 "채널 설정" 창이 같은 파일을 통째로 로드해 두고
    // 있다가 무관한 항목을 저장할 때 이 값을 되돌려버리는 문제가 있어(여러 창이 같은 JSON을
    // "전체 로드 → 수정 → 전체 저장"하는 구조라 서로 덮어씀), DB에 직접 저장해 그 경쟁을 피한다.

    public (bool Enabled, List<string> CampaignSourceHeaders) GetSettings(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Enabled, CampaignSourceHeaders FROM AdChannelSplitSettings WHERE ChannelCode = $channelCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);

        using var reader = command.ExecuteReader();
        if (!reader.Read()) return (false, []);

        var enabled = reader.GetInt64(0) != 0;
        var headers = reader.GetString(1)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        return (enabled, headers);
    }

    public void SaveSettings(string channelCode, bool enabled, List<string> campaignSourceHeaders)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AdChannelSplitSettings (ChannelCode, Enabled, CampaignSourceHeaders)
            VALUES ($channelCode, $enabled, $headers)
            ON CONFLICT(ChannelCode) DO UPDATE SET Enabled = $enabled, CampaignSourceHeaders = $headers
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$headers", string.Join(",", campaignSourceHeaders));
        command.ExecuteNonQuery();
    }

    // ===================== 캠페인 인벤토리(완전일치) =====================

    public List<AdChannelSplitInventoryEntry> GetInventory(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, HeaderName, Value, TargetChannel, ConfirmedAt, LastSeenYymm, LastCost
            FROM AdChannelSplitInventory
            WHERE ChannelCode = $channelCode
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var list = new List<AdChannelSplitInventoryEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AdChannelSplitInventoryEntry
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                HeaderName = reader.GetString(2),
                Value = reader.GetString(3),
                TargetChannel = reader.GetString(4),
                ConfirmedAt = reader.IsDBNull(5) ? null : reader.GetString(5),
                LastSeenYymm = reader.IsDBNull(6) ? null : reader.GetString(6),
                LastCost = (decimal)reader.GetDouble(7),
            });
        }
        return list;
    }

    /// <summary>같은 채널+(헤더,값)이 있으면 갱신하고, 없으면 새로 추가합니다.</summary>
    public void UpsertInventoryEntry(string channelCode, string headerName, string value, string targetChannel, string lastSeenYymm, decimal lastCost)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var findCommand = connection.CreateCommand();
        findCommand.Transaction = transaction;
        findCommand.CommandText = "SELECT Id FROM AdChannelSplitInventory WHERE ChannelCode = $channelCode AND HeaderName = $headerName AND Value = $value";
        findCommand.Parameters.AddWithValue("$channelCode", channelCode);
        findCommand.Parameters.AddWithValue("$headerName", headerName);
        findCommand.Parameters.AddWithValue("$value", value);
        var existingId = findCommand.ExecuteScalar();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (existingId != null)
        {
            command.CommandText = """
                UPDATE AdChannelSplitInventory
                SET TargetChannel = $targetChannel, ConfirmedAt = $confirmedAt, LastSeenYymm = $lastSeenYymm, LastCost = $lastCost
                WHERE Id = $id
                """;
            command.Parameters.AddWithValue("$id", existingId);
        }
        else
        {
            command.CommandText = """
                INSERT INTO AdChannelSplitInventory (ChannelCode, HeaderName, Value, TargetChannel, ConfirmedAt, LastSeenYymm, LastCost)
                VALUES ($channelCode, $headerName, $value, $targetChannel, $confirmedAt, $lastSeenYymm, $lastCost)
                """;
            command.Parameters.AddWithValue("$channelCode", channelCode);
            command.Parameters.AddWithValue("$headerName", headerName);
            command.Parameters.AddWithValue("$value", value);
        }
        command.Parameters.AddWithValue("$targetChannel", targetChannel);
        command.Parameters.AddWithValue("$confirmedAt", now);
        command.Parameters.AddWithValue("$lastSeenYymm", lastSeenYymm);
        command.Parameters.AddWithValue("$lastCost", (double)lastCost);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void DeleteInventoryEntry(long id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM AdChannelSplitInventory WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    // ===================== 선판정 규칙(prerules) =====================

    public List<AdChannelSplitPrerule> GetPrerules(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, Priority, TargetChannel, Note, Enabled
            FROM AdChannelSplitPrerule
            WHERE ChannelCode = $channelCode
            ORDER BY Priority
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);

        var list = new List<AdChannelSplitPrerule>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AdChannelSplitPrerule
            {
                Id = reader.GetInt64(0),
                ChannelCode = reader.GetString(1),
                Priority = reader.GetInt32(2),
                TargetChannel = reader.GetString(3),
                Note = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Enabled = reader.GetInt64(5) != 0,
            });
        }
        return list;
    }

    public List<AdChannelSplitPreruleDetail> GetPreruleDetails(long ruleId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RuleId, HeaderName, Operator, TargetValue, Logic
            FROM AdChannelSplitPreruleDetail
            WHERE RuleId = $ruleId
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$ruleId", ruleId);

        var list = new List<AdChannelSplitPreruleDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(ReadDetail(reader));
        return list;
    }

    public long AddPreruleWithDetails(string channelCode, int priority, string targetChannel, string note, bool enabled, List<AdChannelSplitPreruleDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO AdChannelSplitPrerule (ChannelCode, Priority, TargetChannel, Note, Enabled)
            VALUES ($channelCode, $priority, $targetChannel, $note, $enabled)
            """;
        insertCommand.Parameters.AddWithValue("$channelCode", channelCode);
        insertCommand.Parameters.AddWithValue("$priority", priority);
        insertCommand.Parameters.AddWithValue("$targetChannel", targetChannel);
        insertCommand.Parameters.AddWithValue("$note", note);
        insertCommand.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        insertCommand.ExecuteNonQuery();

        using var lastIdCommand = connection.CreateCommand();
        lastIdCommand.Transaction = transaction;
        lastIdCommand.CommandText = "SELECT last_insert_rowid()";
        var ruleId = (long)lastIdCommand.ExecuteScalar()!;

        InsertDetails(connection, transaction, ruleId, details);

        transaction.Commit();
        return ruleId;
    }

    public void UpdatePreruleSummary(long ruleId, int priority, string targetChannel, string note, bool enabled)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE AdChannelSplitPrerule
            SET Priority = $priority, TargetChannel = $targetChannel, Note = $note, Enabled = $enabled
            WHERE Id = $ruleId
            """;
        command.Parameters.AddWithValue("$priority", priority);
        command.Parameters.AddWithValue("$targetChannel", targetChannel);
        command.Parameters.AddWithValue("$note", note);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$ruleId", ruleId);
        command.ExecuteNonQuery();
    }

    public void ReplacePreruleDetails(long ruleId, List<AdChannelSplitPreruleDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = "DELETE FROM AdChannelSplitPreruleDetail WHERE RuleId = $ruleId";
        deleteCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteCommand.ExecuteNonQuery();

        InsertDetails(connection, transaction, ruleId, details);

        transaction.Commit();
    }

    public void DeletePrerule(long ruleId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var deleteDetailsCommand = connection.CreateCommand();
        deleteDetailsCommand.Transaction = transaction;
        deleteDetailsCommand.CommandText = "DELETE FROM AdChannelSplitPreruleDetail WHERE RuleId = $ruleId";
        deleteDetailsCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteDetailsCommand.ExecuteNonQuery();

        using var deleteRuleCommand = connection.CreateCommand();
        deleteRuleCommand.Transaction = transaction;
        deleteRuleCommand.CommandText = "DELETE FROM AdChannelSplitPrerule WHERE Id = $ruleId";
        deleteRuleCommand.Parameters.AddWithValue("$ruleId", ruleId);
        deleteRuleCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private static void InsertDetails(SqliteConnection connection, SqliteTransaction transaction, long ruleId, List<AdChannelSplitPreruleDetail> details)
    {
        using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO AdChannelSplitPreruleDetail (RuleId, HeaderName, Operator, TargetValue, Logic)
            VALUES ($ruleId, $headerName, $operator, $targetValue, $logic)
            """;
        foreach (var detail in details)
        {
            if (string.IsNullOrWhiteSpace(detail.HeaderName)) continue;
            if (string.IsNullOrWhiteSpace(detail.TargetValue) && detail.Operator != AdConditionOperator.IsZero) continue;

            insertCommand.Parameters.Clear();
            insertCommand.Parameters.AddWithValue("$ruleId", ruleId);
            insertCommand.Parameters.AddWithValue("$headerName", detail.HeaderName);
            insertCommand.Parameters.AddWithValue("$operator", detail.Operator.ToString());
            insertCommand.Parameters.AddWithValue("$targetValue", detail.TargetValue);
            insertCommand.Parameters.AddWithValue("$logic", detail.Logic.ToString());
            insertCommand.ExecuteNonQuery();
        }
    }

    private static AdChannelSplitPreruleDetail ReadDetail(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        RuleId = reader.GetInt64(1),
        HeaderName = reader.GetString(2),
        Operator = Enum.Parse<AdConditionOperator>(reader.GetString(3)),
        TargetValue = reader.GetString(4),
        Logic = Enum.Parse<ConditionLogic>(reader.GetString(5)),
    };
}
