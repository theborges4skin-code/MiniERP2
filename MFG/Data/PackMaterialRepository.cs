using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Data;

public class PackMaterialRepository
{
    public const string CodePrefix = "PK";
    public const int CodeDigits = 3;

    public List<PackMaterial> GetAll(bool includeInactive = true)
    {
        using var c = MfgDb.Open();
        return GetAll(c, includeInactive);
    }

    internal List<PackMaterial> GetAll(SqliteConnection c, bool includeInactive = true, SqliteTransaction? tx = null)
    {
        var list = new List<PackMaterial>();
        using (var cmd = MfgDb.Cmd(c, "SELECT * FROM PackMaterial" + (includeInactive ? "" : " WHERE IsActive=1") + " ORDER BY Category, Name", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                list.Add(new PackMaterial
                {
                    Id = r.Lng("Id"), Code = r.Str("Code"), Category = r.Str("Category"), Name = r.Str("Name"),
                    DefaultSupplierCode = r.NStr("DefaultSupplierCode"), IsTaxFree = r.Bool("IsTaxFree"), IsActive = r.Bool("IsActive"), Note = r.Str("Note"),
                });
            }
        }

        var prices = GetAllPrices(c, tx).ToLookup(p => p.PackMaterialId);
        foreach (var p in list)
        {
            var ordered = prices[p.Id].OrderByDescending(x => x.EffectiveDate, StringComparer.Ordinal).ThenByDescending(x => x.Id).ToList();
            p.CurrentPrice = (string.IsNullOrEmpty(p.DefaultSupplierCode) ? null : ordered.FirstOrDefault(x => x.SupplierCode == p.DefaultSupplierCode))
                             ?? ordered.FirstOrDefault();
        }
        return list;
    }

    public List<PackMaterialPrice> GetPrices(long packId)
    {
        using var c = MfgDb.Open();
        return GetAllPrices(c, null, packId).OrderByDescending(x => x.EffectiveDate, StringComparer.Ordinal).ThenByDescending(x => x.Id).ToList();
    }

    private static List<PackMaterialPrice> GetAllPrices(SqliteConnection c, SqliteTransaction? tx, long? packId = null)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT * FROM PackMaterialPrice" + (packId is null ? "" : " WHERE PackMaterialId=$p"), tx, ("$p", packId));
        using var r = cmd.ExecuteReader();
        var list = new List<PackMaterialPrice>();
        while (r.Read())
        {
            list.Add(new PackMaterialPrice
            {
                Id = r.Lng("Id"), PackMaterialId = r.Lng("PackMaterialId"), SupplierCode = r.NStr("SupplierCode"), UnitPrice = r.Dbl("UnitPrice"),
                EffectiveDate = r.Str("EffectiveDate"), Source = r.Str("Source"), Note = r.Str("Note"), CreatedAt = r.Str("CreatedAt"),
            });
        }
        return list;
    }

    public long Save(PackMaterial p)
    {
        using var c = MfgDb.Open();
        return Save(c, p, null);
    }

    internal long Save(SqliteConnection c, PackMaterial p, SqliteTransaction? tx)
    {
        var now = MfgDb.Now();
        if (p.Id == 0 && string.IsNullOrWhiteSpace(p.Code)) p.Code = CodeGenerator.Next(c, "PackMaterial", CodePrefix, CodeDigits, tx);
        var args = new (string, object?)[]
        {
            ("$code", p.Code), ("$cat", p.Category), ("$name", p.Name.Trim()), ("$sup", string.IsNullOrEmpty(p.DefaultSupplierCode) ? null : p.DefaultSupplierCode),
            ("$tf", p.IsTaxFree ? 1 : 0), ("$active", p.IsActive ? 1 : 0), ("$note", p.Note), ("$now", now), ("$id", p.Id),
        };
        if (p.Id == 0)
        {
            p.Id = MfgDb.Insert(c, """
                INSERT INTO PackMaterial(Code,Category,Name,DefaultSupplierCode,IsTaxFree,IsActive,Note,CreatedAt,UpdatedAt)
                VALUES($code,$cat,$name,$sup,$tf,$active,$note,$now,$now)
                """, tx, args);
        }
        else
        {
            MfgDb.Exec(c, "UPDATE PackMaterial SET Category=$cat,Name=$name,DefaultSupplierCode=$sup,IsTaxFree=$tf,IsActive=$active,Note=$note,UpdatedAt=$now WHERE Id=$id", tx, args);
        }
        return p.Id;
    }

    public long AddPrice(PackMaterialPrice p)
    {
        using var c = MfgDb.Open();
        return AddPrice(c, p, null);
    }

    internal long AddPrice(SqliteConnection c, PackMaterialPrice p, SqliteTransaction? tx) =>
        p.Id = MfgDb.Insert(c, """
            INSERT INTO PackMaterialPrice(PackMaterialId,SupplierCode,UnitPrice,EffectiveDate,Source,Note,CreatedAt)
            VALUES($p,$s,$u,$d,$src,$n,$now)
            """, tx, ("$p", p.PackMaterialId), ("$s", string.IsNullOrEmpty(p.SupplierCode) ? null : p.SupplierCode), ("$u", p.UnitPrice),
            ("$d", p.EffectiveDate), ("$src", p.Source), ("$n", p.Note), ("$now", MfgDb.Now()));

    public void DeletePrice(long priceId)
    {
        using var c = MfgDb.Open();
        MfgDb.Exec(c, "DELETE FROM PackMaterialPrice WHERE Id=$id", null, ("$id", priceId));
    }

    /// <summary>부자재별 사용처(제조법 코드) — 부자재 화면 표시용.</summary>
    public Dictionary<long, List<string>> GetUsage()
    {
        using var c = MfgDb.Open();
        using var cmd = MfgDb.Cmd(c, """
            SELECT DISTINCT sc.PackMaterialId, r.Code FROM RecipeSizeCost sc
            JOIN RecipeSize s ON s.Id=sc.RecipeSizeId JOIN Recipe r ON r.Id=s.RecipeId
            WHERE sc.PackMaterialId IS NOT NULL ORDER BY r.Code
            """);
        using var rd = cmd.ExecuteReader();
        var map = new Dictionary<long, List<string>>();
        while (rd.Read())
        {
            var id = rd.GetInt64(0);
            if (!map.TryGetValue(id, out var list)) map[id] = list = [];
            list.Add(rd.GetString(1));
        }
        return map;
    }
}
