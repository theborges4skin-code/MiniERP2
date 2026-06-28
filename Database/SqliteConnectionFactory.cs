using Microsoft.Data.Sqlite;
using MiniERP2.Config;

namespace MiniERP2.Database;

public static class SqliteConnectionFactory
{
    /// <summary>
    /// DbSchema.EnsureCreated를 이미 실행한 DB 파일 경로 집합. 마감/이익분석에서 정산파일을 읽을
    /// 때처럼 행마다 여러 Repository 메서드(각각 OpenConnection 호출)가 반복 실행되는 경우,
    /// 매번 17개 CREATE TABLE + 14개 PRAGMA table_info 체크를 다시 도는 게 가장 큰 병목이었다
    /// (적은 데이터의 정산파일도 1분 이상 걸리던 원인). 같은 DB 파일에 대해서는 프로세스 생애주기
    /// 동안 한 번만 보장하면 충분하므로 경로별로 캐시한다(테스트는 매번 다른 임시 폴더를 쓰므로
    /// 영향 없음).
    /// </summary>
    private static readonly HashSet<string> EnsuredDatabasePaths = new();
    private static readonly Lock EnsuredPathsLock = new();

    public static SqliteConnection OpenConnection()
    {
        var path = PathProvider.DatabaseFilePath;
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        lock (EnsuredPathsLock)
        {
            if (EnsuredDatabasePaths.Add(path))
            {
                DbSchema.EnsureCreated(connection);
            }
        }

        return connection;
    }
}
