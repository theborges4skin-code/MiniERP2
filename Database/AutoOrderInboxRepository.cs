using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 자동발주처리(Gmail 자동화) 로컬 tracking 테이블(AutoOrderInboxTable) 저장소.
/// 운영상태(중복 알림 방지 + 처리이력)만 다루며 업무·개인정보는 저장하지 않는다
/// (02_자동발주처리_MiniERP2연동_설계.md §3).
/// </summary>
public class AutoOrderInboxRepository
{
    /// <summary>manifest item.id가 이미 로컬에 있는지 확인한다(폴링 시 신규 판정 키).</summary>
    public bool Exists(string id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM AutoOrderInboxTable WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return command.ExecuteScalar() != null;
    }

    /// <summary>새로 감지된 manifest 항목을 Status=new로 등록한다. 이미 있으면 아무 것도 하지 않는다(멱등).</summary>
    public void InsertIfNew(AutoOrderInboxItem item)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AutoOrderInboxTable (Id, SubjectSnip, ReceivedAt, XlsxPath, Sha256, RowCount, ParseStatus, Status, SeenAt)
            VALUES ($id, $subjectSnip, $receivedAt, $xlsxPath, $sha256, $rowCount, $parseStatus, 'new', $seenAt)
            ON CONFLICT(Id) DO NOTHING
            """;
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$subjectSnip", item.SubjectSnip);
        command.Parameters.AddWithValue("$receivedAt", item.ReceivedAt);
        command.Parameters.AddWithValue("$xlsxPath", item.XlsxPath);
        command.Parameters.AddWithValue("$sha256", item.Sha256);
        command.Parameters.AddWithValue("$rowCount", item.RowCount);
        command.Parameters.AddWithValue("$parseStatus", item.ParseStatus);
        command.Parameters.AddWithValue("$seenAt", item.SeenAt);
        command.ExecuteNonQuery();
    }

    /// <summary>알림 목록용 전체 조회(최신 수신순). imported/dismissed는 호출 측에서 기본 숨김 처리한다.</summary>
    public List<AutoOrderInboxItem> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SubjectSnip, ReceivedAt, XlsxPath, Sha256, RowCount, ParseStatus, Status, LocalFilePath, SeenAt, ImportedAt
            FROM AutoOrderInboxTable
            ORDER BY ReceivedAt DESC
            """;

        var results = new List<AutoOrderInboxItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Read(reader));
        }
        return results;
    }

    public AutoOrderInboxItem? GetById(string id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SubjectSnip, ReceivedAt, XlsxPath, Sha256, RowCount, ParseStatus, Status, LocalFilePath, SeenAt, ImportedAt
            FROM AutoOrderInboxTable
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    /// <summary>배지 표시용 신규 건수(Status=new).</summary>
    public int CountNew()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AutoOrderInboxTable WHERE Status = 'new'";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>[다운로드&저장] — 로컬 경로를 기록하고 downloaded로 전이한다.</summary>
    public void MarkDownloaded(string id, string localFilePath)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AutoOrderInboxTable SET Status = 'downloaded', LocalFilePath = $localFilePath WHERE Id = $id";
        command.Parameters.AddWithValue("$localFilePath", localFilePath);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>[발주 파일 로드로 열기] — imported로 전이하고 시각을 기록한다.</summary>
    public void MarkImported(string id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AutoOrderInboxTable SET Status = 'imported', ImportedAt = $importedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$importedAt", DateTime.Now);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>[무시] — 목록에서 기본적으로 숨겨지는 dismissed로 전이한다.</summary>
    public void MarkDismissed(string id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AutoOrderInboxTable SET Status = 'dismissed' WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static AutoOrderInboxItem Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        SubjectSnip = reader.GetString(1),
        ReceivedAt = reader.GetDateTime(2),
        XlsxPath = reader.GetString(3),
        Sha256 = reader.GetString(4),
        RowCount = reader.GetInt32(5),
        ParseStatus = reader.GetString(6),
        Status = reader.GetString(7),
        LocalFilePath = reader.IsDBNull(8) ? null : reader.GetString(8),
        SeenAt = reader.GetDateTime(9),
        ImportedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
    };
}
