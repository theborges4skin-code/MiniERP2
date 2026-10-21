using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Config;

namespace MiniERP2.MFG.Data;

/// <summary>
/// mfgdb 백업/복원. MiniERP2 DbBackupService와 같은 규칙(docs/MFG_PLAN.md §1):
/// 수동·작업 전 백업 최신 3개 / 종료 시 일일 백업 2개월 보관 / 월간 백업(정리 대상 아님).
/// 파일명 프리픽스를 종류별로 달리해 서로의 보관 개수 정리에 영향을 주지 않는다.
/// </summary>
public class MfgBackupService
{
    private const int MaxBackupsToKeep = 3;
    private const int DailyBackupRetentionMonths = 2;

    private static string Folder => MfgPaths.BackupsFolder;

    /// <param name="reason">파일명에 남길 이유 태그(예: "manual", "before_import").</param>
    public string CreateBackup(string reason)
    {
        var path = CopyTo($"mfgdb_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.sqlite");
        Prune(GetBackups().Skip(MaxBackupsToKeep));
        return path;
    }

    public List<FileInfo> GetBackups() => List("mfgdb_2*.sqlite");

    public string CreateOrUpdateDailyBackup()
    {
        var path = CopyTo($"mfgdb_DailyBackup_{DateTime.Now:yyyyMMdd}.sqlite");
        var cutoff = DateTime.UtcNow.AddMonths(-DailyBackupRetentionMonths);
        Prune(GetDailyBackups().Where(f => f.LastWriteTimeUtc < cutoff));
        return path;
    }

    public List<FileInfo> GetDailyBackups() => List("mfgdb_DailyBackup_*.sqlite");

    public bool NeedsMonthlyBackup() =>
        !Directory.Exists(Folder) || !new DirectoryInfo(Folder).GetFiles($"mfgdb_MonthlyBackup_{DateTime.Now:yyyyMM}*.sqlite").Any();

    public string CreateMonthlyBackup() => CopyTo($"mfgdb_MonthlyBackup_{DateTime.Now:yyyyMM}.sqlite");

    public List<FileInfo> GetMonthlyBackups() => List("mfgdb_MonthlyBackup_*.sqlite");

    /// <summary>모든 종류의 백업을 최신순으로(복원 목록용).</summary>
    public List<FileInfo> GetAllBackups() => List("mfgdb_*.sqlite");

    /// <summary>백업으로 현재 mfgdb를 덮어쓴다. 복원 직전 상태도 "before_restore"로 남긴다.</summary>
    public void Restore(string backupFilePath)
    {
        if (File.Exists(MfgPaths.DatabaseFilePath)) CreateBackup("before_restore");
        SqliteConnection.ClearAllPools();
        File.Copy(backupFilePath, MfgPaths.DatabaseFilePath, overwrite: true);
    }

    private static string CopyTo(string fileName)
    {
        Directory.CreateDirectory(Folder);
        // 열린 연결의 쓰기를 비워 일관된 시점의 파일을 복사한다.
        SqliteConnection.ClearAllPools();
        var path = Path.Combine(Folder, fileName);
        File.Copy(MfgPaths.DatabaseFilePath, path, overwrite: true);
        return path;
    }

    private static List<FileInfo> List(string pattern)
    {
        if (!Directory.Exists(Folder)) return [];
        return new DirectoryInfo(Folder).GetFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
    }

    private static void Prune(IEnumerable<FileInfo> files)
    {
        foreach (var f in files.ToList())
        {
            try { f.Delete(); }
            catch (IOException) { /* 잠겨 있으면 다음 백업 때 다시 정리된다 */ }
        }
    }
}
