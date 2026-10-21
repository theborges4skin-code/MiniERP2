using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Data;

public class SupplierRepository
{
    public List<Supplier> GetAll()
    {
        using var c = MfgDb.Open();
        using var cmd = MfgDb.Cmd(c, "SELECT * FROM Supplier ORDER BY Name");
        using var r = cmd.ExecuteReader();
        var list = new List<Supplier>();
        while (r.Read())
        {
            list.Add(new Supplier { ChannelCode = r.Str("ChannelCode"), Name = r.Str("Name"), SyncedAt = r.NStr("SyncedAt"), Memo = r.Str("Memo") });
        }
        return list;
    }

    /// <summary>MiniERP2에서 불러온 매입처 목록을 반영한다. 이름은 MiniERP2 기준으로 갱신하고 MFG 메모는 보존한다.
    /// MiniERP2에서 사라진 채널은 지우지 않는다(이미 단가 이력이 그 코드를 참조할 수 있으므로).</summary>
    public (int Added, int Updated) Sync(IEnumerable<(string Code, string Name)> channels)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        int added = 0, updated = 0;
        var now = MfgDb.Now();
        foreach (var (code, name) in channels)
        {
            using var check = MfgDb.Cmd(c, "SELECT Name FROM Supplier WHERE ChannelCode=$c", tx, ("$c", code));
            var existing = check.ExecuteScalar() as string;
            if (existing is null)
            {
                MfgDb.Exec(c, "INSERT INTO Supplier(ChannelCode,Name,SyncedAt) VALUES($c,$n,$t)", tx, ("$c", code), ("$n", name), ("$t", now));
                added++;
            }
            else
            {
                MfgDb.Exec(c, "UPDATE Supplier SET Name=$n, SyncedAt=$t WHERE ChannelCode=$c", tx, ("$c", code), ("$n", name), ("$t", now));
                if (existing != name) updated++;
            }
        }
        tx.Commit();
        return (added, updated);
    }

    public void UpdateMemo(string code, string memo)
    {
        using var c = MfgDb.Open();
        MfgDb.Exec(c, "UPDATE Supplier SET Memo=$m WHERE ChannelCode=$c", null, ("$m", memo), ("$c", code));
    }
}
