using Microsoft.Data.Sqlite;

namespace MiniERP2.Database;

/// <summary>
/// 운송장 라벨 수동 정정분(TrackingLabelOverrideTable)을 다룬다. 자동 분류기가 틀린 건을 사용자가
/// 고치면 운송장번호 기준으로 남겨두고, 같은 파일을 다시 읽을 때 자동 판정 위에 덮어씌운다
/// (TrackingBackfillCheckFlow). "자동 판정으로 되돌리기"는 행 삭제로 처리한다 — 되돌린 뒤에는
/// 분류기 결과를 그대로 따라가야 하므로 빈 라벨을 저장해두면 안 된다.
/// </summary>
public class TrackingLabelOverrideRepository
{
    /// <summary>주어진 운송장번호들에 대한 정정 라벨만 조회한다(운송장번호 → 라벨).</summary>
    public Dictionary<string, string> GetForTrackingNos(IEnumerable<string> trackingNos)
    {
        var keys = trackingNos
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0) return result;

        using var connection = SqliteConnectionFactory.OpenConnection();

        // 운송장 파일 한 건이 수천 행이라 IN 절 파라미터가 SQLite 상한(999)을 넘길 수 있다.
        // 정정분 자체는 많아야 수백 건 수준이므로 전부 읽어와 메모리에서 걸러내는 편이 안전하다.
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TrackingNo, Label FROM TrackingLabelOverrideTable";
        using var reader = command.ExecuteReader();

        var wanted = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            var trackingNo = reader.GetString(0);
            if (wanted.Contains(trackingNo)) result[trackingNo] = reader.GetString(1);
        }
        return result;
    }

    /// <summary>운송장번호 여러 건의 라벨을 한 트랜잭션으로 정정 저장한다(이미 있으면 덮어쓴다).</summary>
    public void SaveMany(IEnumerable<string> trackingNos, string label)
    {
        var keys = trackingNos
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0) return;

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var updatedAt = DateTime.Now.ToString("O");
        foreach (var trackingNo in keys)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO TrackingLabelOverrideTable (TrackingNo, Label, UpdatedAt)
                VALUES ($trackingNo, $label, $updatedAt)
                ON CONFLICT(TrackingNo) DO UPDATE SET Label = $label, UpdatedAt = $updatedAt
                """;
            command.Parameters.AddWithValue("$trackingNo", trackingNo);
            command.Parameters.AddWithValue("$label", label);
            command.Parameters.AddWithValue("$updatedAt", updatedAt);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>정정분을 지워 자동 판정으로 되돌린다.</summary>
    public void DeleteMany(IEnumerable<string> trackingNos)
    {
        var keys = trackingNos
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (keys.Count == 0) return;

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var trackingNo in keys)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM TrackingLabelOverrideTable WHERE TrackingNo = $trackingNo";
            command.Parameters.AddWithValue("$trackingNo", trackingNo);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
