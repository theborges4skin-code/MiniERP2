using Microsoft.Data.Sqlite;
using MiniERP2.Config;

namespace MiniERP2.Database;

/// <summary>
/// DB 전체(SQLite 단일 파일)의 스냅샷 백업/복원을 담당합니다. 데이터 관리창에서 변경내역을
/// 저장하기 직전에 항상 자동으로 백업하고(직전 3개까지만 보관), 사용자가 수동으로 "전체 백업"을
/// 누를 수도 있습니다. 백업 단위는 테이블별이 아니라 DB 파일 전체입니다 — SQLite는 한 파일에
/// 모든 테이블이 들어있어 트랜잭션적으로 일관된 시점의 전체 스냅샷이 가장 안전하고 단순합니다.
/// </summary>
public class DbBackupService
{
    private const int MaxBackupsToKeep = 3;

    private string BackupsFolder => Path.Combine(PathProvider.AppDataFolder, "backups");

    /// <summary>
    /// 현재 DB 파일을 백업 폴더에 타임스탬프 이름으로 복사합니다. 보관 개수를 초과하면 가장
    /// 오래된 백업부터 삭제해 항상 최신 <see cref="MaxBackupsToKeep"/>개만 남깁니다.
    /// </summary>
    /// <param name="reason">백업 파일명에 남길 이유 태그(예: "manual", "before_import_save").</param>
    /// <returns>생성된 백업 파일의 전체 경로입니다.</returns>
    public string CreateBackup(string reason)
    {
        Directory.CreateDirectory(BackupsFolder);

        // 진행 중인 쓰기를 모두 비우고 닫아 파일 복사 시점에 일관된 스냅샷이 되도록 한다.
        SqliteConnection.ClearAllPools();

        var fileName = $"ERP_Database_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.sqlite";
        var backupPath = Path.Combine(BackupsFolder, fileName);
        File.Copy(PathProvider.DatabaseFilePath, backupPath, overwrite: true);

        PruneOldBackups();
        return backupPath;
    }

    /// <summary>보관된 백업 목록을 최신순으로 가져옵니다.</summary>
    public List<FileInfo> GetBackups()
    {
        if (!Directory.Exists(BackupsFolder)) return [];

        return new DirectoryInfo(BackupsFolder)
            .GetFiles("ERP_Database_*.sqlite")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
    }

    /// <summary>
    /// 지정된 백업 파일로 현재 DB 파일을 덮어씁니다. 복원 후에는 이미 열려있는 화면들의 메모리
    /// 상태가 옛 DB 기준이므로, 호출 측에서 사용자에게 앱 재시작을 안내해야 합니다.
    /// </summary>
    public void Restore(string backupFilePath)
    {
        SqliteConnection.ClearAllPools();
        File.Copy(backupFilePath, PathProvider.DatabaseFilePath, overwrite: true);
    }

    private void PruneOldBackups()
    {
        var backups = GetBackups();
        foreach (var old in backups.Skip(MaxBackupsToKeep))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
                // 다른 프로세스가 잠깐 잠그고 있어도 다음 백업 시점에 다시 정리되므로 무시한다.
            }
        }
    }
}
