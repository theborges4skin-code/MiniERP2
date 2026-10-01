using System.Text.Json;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>온라인 매출 종합보고서의 월별 수동 입력값(OnlineReportMonthTable, Period = "YYYY-MM").</summary>
public class OnlineReportMonthRepository
{
    public OnlineReportMonthInput? Get(string period)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Json FROM OnlineReportMonthTable WHERE Period = @p";
        cmd.Parameters.AddWithValue("@p", period);
        var json = cmd.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OnlineReportMonthInput>(json);
    }

    public void Save(string period, OnlineReportMonthInput input)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO OnlineReportMonthTable (Period, Json, SavedAt) VALUES (@p, @j, @s)
            ON CONFLICT(Period) DO UPDATE SET Json = excluded.Json, SavedAt = excluded.SavedAt
            """;
        cmd.Parameters.AddWithValue("@p", period);
        cmd.Parameters.AddWithValue("@j", JsonSerializer.Serialize(input));
        cmd.Parameters.AddWithValue("@s", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>입력값이 아직 없는 달에 "전월 값 가져오기"(포천 고정비 등)를 할 때 쓸 직전 저장 월.</summary>
    public string? GetLatestPeriodBefore(string period)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Period FROM OnlineReportMonthTable WHERE Period < @p ORDER BY Period DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@p", period);
        return cmd.ExecuteScalar() as string;
    }
}
