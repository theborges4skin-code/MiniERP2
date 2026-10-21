using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MiniERP2.MFG.Integration;

/// <summary>
/// MiniERP2 DB와의 연동(docs/MFG_PLAN.md §2). MFG는 MiniERP2 코드를 참조하지 않고 DB 파일에 직접 접근한다.
/// <list type="bullet">
/// <item>읽기: 매입처 채널(SalesChannelTable.IsPurchase=1), 품목 원가(ItemTable).</item>
/// <item>쓰기: 확정 원가 전송 — ItemTable.CostPrice(VAT포함) 갱신 + ItemCostHistory 기록. MiniERP2
///   ItemRepository.Upsert와 같은 방식으로 이력을 남긴다. 쓰기 직전 MiniERP2 백업 폴더에 DB 사본을 만든다.</item>
/// </list>
/// MiniERP2 스키마를 바꾸지 않는다(테이블 생성·열 추가 없음).
/// </summary>
public class MiniErp2Bridge
{
    private readonly string _dbPath;

    public MiniErp2Bridge(string dbPath) => _dbPath = dbPath;

    public bool IsAvailable => File.Exists(_dbPath);

    public string DatabasePath => _dbPath;

    private SqliteConnection Open(bool readOnly)
    {
        if (!IsAvailable) throw new FileNotFoundException("MiniERP2 DB 파일을 찾을 수 없습니다. 설정에서 경로를 확인하세요.", _dbPath);
        var mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite;
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath, Mode = mode, DefaultTimeout = 10 }.ToString());
        c.Open();
        return c;
    }

    /// <summary>MiniERP2 매입처 채널 목록(IsPurchase=1).</summary>
    public List<(string Code, string Name)> GetPurchaseChannels()
    {
        using var c = Open(readOnly: true);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ChannelCode, ChannelName FROM SalesChannelTable WHERE IsPurchase=1 ORDER BY ChannelName";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.IsDBNull(1) ? r.GetString(0) : r.GetString(1)));
        return list;
    }

    /// <summary>MiniERP2 품목 원가(SKU → (품목명, CostPrice VAT포함)).</summary>
    public Dictionary<string, (string Name, double Cost)> GetItemCosts()
    {
        using var c = Open(readOnly: true);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Sku, ItemName, CostPrice FROM ItemTable";
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<string, (string, double)>(StringComparer.Ordinal);
        while (r.Read())
        {
            map[r.GetString(0)] = (r.IsDBNull(1) ? string.Empty : r.GetString(1), r.IsDBNull(2) ? 0 : Convert.ToDouble(r.GetValue(2), CultureInfo.InvariantCulture));
        }
        return map;
    }

    /// <summary>
    /// 원가 전송. MiniERP2에 없는 SKU는 건너뛴다(품목 신규 등록은 MiniERP2에서). 값이 같으면 이력을 남기지 않는다.
    /// 반환: (반영 건수, 건너뛴 SKU 목록, 백업 파일 경로).
    /// </summary>
    public (int Updated, List<string> Skipped, string BackupPath) PushCosts(IReadOnlyList<(string Sku, double NewCost)> items)
    {
        var backupPath = BackupMiniErp2("mfg_push");
        var skipped = new List<string>();
        var updated = 0;

        using var c = Open(readOnly: false);
        using var tx = c.BeginTransaction();
        foreach (var (sku, newCost) in items)
        {
            using var get = c.CreateCommand();
            get.Transaction = tx;
            get.CommandText = "SELECT CostPrice FROM ItemTable WHERE Sku=$s";
            get.Parameters.AddWithValue("$s", sku);
            var old = get.ExecuteScalar();
            if (old is null || old is DBNull) { skipped.Add(sku); continue; }
            var oldCost = Convert.ToDouble(old, CultureInfo.InvariantCulture);
            if (Math.Abs(oldCost - newCost) < 0.0001) continue;

            using var upd = c.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE ItemTable SET CostPrice=$c WHERE Sku=$s";
            upd.Parameters.AddWithValue("$c", newCost);
            upd.Parameters.AddWithValue("$s", sku);
            upd.ExecuteNonQuery();

            using var hist = c.CreateCommand();
            hist.Transaction = tx;
            hist.CommandText = "INSERT INTO ItemCostHistory (Sku, OldCost, NewCost, ChangedAt) VALUES ($s, $o, $n, $t)";
            hist.Parameters.AddWithValue("$s", sku);
            hist.Parameters.AddWithValue("$o", oldCost);
            hist.Parameters.AddWithValue("$n", newCost);
            hist.Parameters.AddWithValue("$t", DateTime.Now);
            hist.ExecuteNonQuery();
            updated++;
        }
        tx.Commit();
        return (updated, skipped, backupPath);
    }

    /// <summary>
    /// MiniERP2 DB를 MiniERP2 백업 폴더(&lt;DB폴더&gt;\backups)에 MiniERP2 수동백업과 같은 이름 규칙
    /// (ERP_Database_yyyyMMdd_HHmmss_reason.sqlite)으로 복사한다. 그래서 MiniERP2의 백업 목록·복원 화면에서
    /// 그대로 보이고, MiniERP2의 "최신 3개 보관" 정리 규칙도 똑같이 적용된다.
    /// SQLite 온라인 백업 API를 써서 MiniERP2가 실행 중이어도 일관된 사본을 만든다.
    /// </summary>
    public string BackupMiniErp2(string reason)
    {
        var folder = Path.Combine(Path.GetDirectoryName(_dbPath)!, "backups");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"ERP_Database_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.sqlite");
        using var source = Open(readOnly: true);
        using var dest = new SqliteConnection($"Data Source={target}");
        dest.Open();
        source.BackupDatabase(dest);
        return target;
    }
}
