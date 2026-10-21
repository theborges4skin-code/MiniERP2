using System.Globalization;
using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Config;

namespace MiniERP2.MFG.Data;

/// <summary>mfgdb 연결 진입점. 경로별로 스키마 보장을 한 번만 수행한다(MiniERP2 SqliteConnectionFactory와 같은 방식).</summary>
public static class MfgDb
{
    private static readonly HashSet<string> EnsuredPaths = new();
    private static readonly Lock EnsuredLock = new();

    public static SqliteConnection Open()
    {
        Directory.CreateDirectory(MfgPaths.AppDataFolder);
        var path = MfgPaths.DatabaseFilePath;
        var connection = new SqliteConnection($"Data Source={path};Foreign Keys=True");
        connection.Open();

        lock (EnsuredLock)
        {
            if (EnsuredPaths.Add(path))
            {
                MfgSchema.EnsureCreated(connection);
            }
        }
        return connection;
    }

    public static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Today() => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---- 짧은 헬퍼(리포지토리 공용) ----

    internal static SqliteCommand Cmd(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    internal static long Insert(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(c, sql + "; SELECT last_insert_rowid();", tx, args);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    internal static int Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(c, sql, tx, args);
        return cmd.ExecuteNonQuery();
    }

    internal static string Str(this SqliteDataReader r, string col) => r[col] is DBNull ? string.Empty : Convert.ToString(r[col], CultureInfo.InvariantCulture) ?? string.Empty;

    internal static string? NStr(this SqliteDataReader r, string col) => r[col] is DBNull ? null : Convert.ToString(r[col], CultureInfo.InvariantCulture);

    internal static double Dbl(this SqliteDataReader r, string col) => r[col] is DBNull ? 0 : Convert.ToDouble(r[col], CultureInfo.InvariantCulture);

    internal static double? NDbl(this SqliteDataReader r, string col) => r[col] is DBNull ? null : Convert.ToDouble(r[col], CultureInfo.InvariantCulture);

    internal static long Lng(this SqliteDataReader r, string col) => Convert.ToInt64(r[col], CultureInfo.InvariantCulture);

    internal static long? NLng(this SqliteDataReader r, string col) => r[col] is DBNull ? null : Convert.ToInt64(r[col], CultureInfo.InvariantCulture);

    internal static bool Bool(this SqliteDataReader r, string col) => r[col] is not DBNull && Convert.ToInt64(r[col], CultureInfo.InvariantCulture) != 0;
}
