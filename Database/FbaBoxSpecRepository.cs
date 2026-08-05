using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>아마존 FBA 박스규격 마스터(FbaBoxSpec)에 대한 데이터베이스 작업을 처리한다.</summary>
public class FbaBoxSpecRepository
{
    public void Upsert(FbaBoxSpec model)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FbaBoxSpec (BoxName, WidthMm, DepthMm, HeightMm, SortOrder, IsActive, UpdatedAt)
            VALUES ($boxName, $widthMm, $depthMm, $heightMm, $sortOrder, $isActive, $updatedAt)
            ON CONFLICT(BoxName) DO UPDATE SET
                WidthMm = excluded.WidthMm,
                DepthMm = excluded.DepthMm,
                HeightMm = excluded.HeightMm,
                SortOrder = excluded.SortOrder,
                IsActive = excluded.IsActive,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$boxName", model.BoxName);
        command.Parameters.AddWithValue("$widthMm", model.WidthMm);
        command.Parameters.AddWithValue("$depthMm", model.DepthMm);
        command.Parameters.AddWithValue("$heightMm", model.HeightMm);
        command.Parameters.AddWithValue("$sortOrder", model.SortOrder);
        command.Parameters.AddWithValue("$isActive", model.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.ExecuteNonQuery();
    }

    public List<FbaBoxSpec> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BoxName, WidthMm, DepthMm, HeightMm, SortOrder, IsActive, UpdatedAt
            FROM FbaBoxSpec
            ORDER BY SortOrder, BoxName
            """;

        var result = new List<FbaBoxSpec>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadModel(reader));
        }
        return result;
    }

    public FbaBoxSpec? GetByName(string boxName)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT BoxName, WidthMm, DepthMm, HeightMm, SortOrder, IsActive, UpdatedAt
            FROM FbaBoxSpec
            WHERE BoxName = $boxName
            """;
        command.Parameters.AddWithValue("$boxName", boxName);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadModel(reader) : null;
    }

    public void Delete(string boxName)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FbaBoxSpec WHERE BoxName = $boxName";
        command.Parameters.AddWithValue("$boxName", boxName);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 실측 초기 4건을 시드한다. 이미 있으면 손대지 않는다(INSERT OR IGNORE) — DbSchema.EnsureCreated에
    /// 두지 않고 MainHub 시작 시 1회만 호출하는 이유는 SalesChannelRepository.EnsureSampleChannel()과
    /// 동일: 스키마 초기화에 두면 테스트가 매번 만드는 임시 DB에도 매번 시드되어 행 개수를 세는 테스트가
    /// 깨질 수 있다.
    /// </summary>
    public void EnsureDefaultBoxSpecs()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO FbaBoxSpec (BoxName, WidthMm, DepthMm, HeightMm, SortOrder, IsActive, UpdatedAt) VALUES
                ('4Lx4개', 500, 280, 300, 1, 1, ''),
                ('vol182개', 500, 280, 230, 2, 1, ''),
                ('10및50', 500, 280, 280, 3, 1, ''),
                ('500x20개', 460, 300, 180, 4, 1, '')
            """;
        command.ExecuteNonQuery();
    }

    private static FbaBoxSpec ReadModel(SqliteDataReader reader) => new()
    {
        BoxName = reader.GetString(0),
        WidthMm = reader.GetDouble(1),
        DepthMm = reader.GetDouble(2),
        HeightMm = reader.GetDouble(3),
        SortOrder = reader.GetInt32(4),
        IsActive = reader.GetInt32(5) != 0,
        UpdatedAt = reader.IsDBNull(6) || string.IsNullOrEmpty(reader.GetString(6)) ? null : DateTime.Parse(reader.GetString(6)),
    };
}
