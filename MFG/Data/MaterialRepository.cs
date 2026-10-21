using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Data;

public class MaterialRepository
{
    public const string CodePrefix = "RM";
    public const int CodeDigits = 3;

    /// <summary>원료 전체 + 현재 단가. <paramref name="includeInactive"/>가 false면 사용중인 원료만.</summary>
    public List<Material> GetAll(bool includeInactive = true)
    {
        using var c = MfgDb.Open();
        return GetAll(c, includeInactive);
    }

    internal List<Material> GetAll(SqliteConnection c, bool includeInactive = true, SqliteTransaction? tx = null)
    {
        var list = new List<Material>();
        using (var cmd = MfgDb.Cmd(c, "SELECT * FROM Material" + (includeInactive ? "" : " WHERE IsActive=1") + " ORDER BY Code", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) list.Add(Read(r));
        }

        var prices = GetAllPrices(c, tx).ToLookup(p => p.MaterialId);
        foreach (var m in list) m.CurrentPrice = PickCurrent(prices[m.Id], m.DefaultSupplierCode);
        return list;
    }

    public Material? Get(long id) => GetAll().FirstOrDefault(m => m.Id == id);

    /// <summary>
    /// 현재 단가 선택 규칙: 기본 매입처가 지정되어 있고 그 매입처 단가가 있으면 그중 최신, 아니면 전체 중 최신.
    /// 최신 = 적용일 내림차순, 같은 날이면 나중에 입력한 것(Id 큰 것).
    /// </summary>
    public static MaterialPrice? PickCurrent(IEnumerable<MaterialPrice> prices, string? defaultSupplier)
    {
        var ordered = prices.OrderByDescending(p => p.EffectiveDate, StringComparer.Ordinal).ThenByDescending(p => p.Id).ToList();
        if (!string.IsNullOrEmpty(defaultSupplier))
        {
            var bySupplier = ordered.FirstOrDefault(p => p.SupplierCode == defaultSupplier);
            if (bySupplier is not null) return bySupplier;
        }
        return ordered.FirstOrDefault();
    }

    public List<MaterialPrice> GetPrices(long materialId)
    {
        using var c = MfgDb.Open();
        return GetAllPrices(c, null, materialId).OrderByDescending(p => p.EffectiveDate, StringComparer.Ordinal).ThenByDescending(p => p.Id).ToList();
    }

    private static List<MaterialPrice> GetAllPrices(SqliteConnection c, SqliteTransaction? tx, long? materialId = null)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT * FROM MaterialPrice" + (materialId is null ? "" : " WHERE MaterialId=$m"), tx, ("$m", materialId));
        using var r = cmd.ExecuteReader();
        var list = new List<MaterialPrice>();
        while (r.Read())
        {
            list.Add(new MaterialPrice
            {
                Id = r.Lng("Id"), MaterialId = r.Lng("MaterialId"), SupplierCode = r.NStr("SupplierCode"),
                PricePerKg = r.Dbl("PricePerKg"), EffectiveDate = r.Str("EffectiveDate"), Source = r.Str("Source"),
                IsUncertain = r.Bool("IsUncertain"), Note = r.Str("Note"), CreatedAt = r.Str("CreatedAt"),
            });
        }
        return list;
    }

    private static Material Read(SqliteDataReader r) => new()
    {
        Id = r.Lng("Id"), Code = r.Str("Code"), Name = r.Str("Name"), Aliases = r.Str("Aliases"), Inci = r.Str("Inci"),
        Cas = r.Str("Cas"), Role = r.Str("Role"), Density = r.NDbl("Density"), IsMixture = r.Bool("IsMixture"),
        IsTaxFree = r.Bool("IsTaxFree"), IsActive = r.Bool("IsActive"), DefaultSupplierCode = r.NStr("DefaultSupplierCode"), Note = r.Str("Note"),
    };

    /// <summary>원료명 또는 별칭이 같은(공백·대소문자 무시) 다른 원료들. 신규 등록·이름 변경 시 중복 경고용.</summary>
    public List<Material> FindByNameOrAlias(string name, long excludeId = 0)
    {
        var key = Normalize(name);
        if (key.Length == 0) return [];
        return GetAll().Where(m => m.Id != excludeId && (Normalize(m.Name) == key || m.AliasList.Any(a => Normalize(a) == key))).ToList();
    }

    public static string Normalize(string s) => new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();

    /// <summary>신규 원료 저장 시 코드가 비어 있으면 자동 채번한다. 저장된 Id를 돌려준다.</summary>
    public long Save(Material m)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        var id = Save(c, m, tx);
        tx.Commit();
        return id;
    }

    internal long Save(SqliteConnection c, Material m, SqliteTransaction? tx)
    {
        var now = MfgDb.Now();
        var args = new (string, object?)[]
        {
            ("$code", m.Code), ("$name", m.Name.Trim()), ("$aliases", m.Aliases), ("$inci", m.Inci), ("$cas", m.Cas), ("$role", m.Role),
            ("$density", m.Density), ("$mix", m.IsMixture ? 1 : 0), ("$taxfree", m.IsTaxFree ? 1 : 0), ("$active", m.IsActive ? 1 : 0),
            ("$sup", string.IsNullOrEmpty(m.DefaultSupplierCode) ? null : m.DefaultSupplierCode), ("$note", m.Note), ("$now", now), ("$id", m.Id),
        };

        if (m.Id == 0)
        {
            if (string.IsNullOrWhiteSpace(m.Code)) m.Code = CodeGenerator.Next(c, "Material", CodePrefix, CodeDigits, tx);
            args[0] = ("$code", m.Code);
            m.Id = MfgDb.Insert(c, """
                INSERT INTO Material(Code,Name,Aliases,Inci,Cas,Role,Density,IsMixture,IsTaxFree,IsActive,DefaultSupplierCode,Note,CreatedAt,UpdatedAt)
                VALUES($code,$name,$aliases,$inci,$cas,$role,$density,$mix,$taxfree,$active,$sup,$note,$now,$now)
                """, tx, args);
        }
        else
        {
            MfgDb.Exec(c, """
                UPDATE Material SET Name=$name,Aliases=$aliases,Inci=$inci,Cas=$cas,Role=$role,Density=$density,IsMixture=$mix,
                  IsTaxFree=$taxfree,IsActive=$active,DefaultSupplierCode=$sup,Note=$note,UpdatedAt=$now WHERE Id=$id
                """, tx, args);
        }
        return m.Id;
    }

    public long AddPrice(MaterialPrice p)
    {
        using var c = MfgDb.Open();
        return AddPrice(c, p, null);
    }

    internal long AddPrice(SqliteConnection c, MaterialPrice p, SqliteTransaction? tx) =>
        p.Id = MfgDb.Insert(c, """
            INSERT INTO MaterialPrice(MaterialId,SupplierCode,PricePerKg,EffectiveDate,Source,IsUncertain,Note,CreatedAt)
            VALUES($m,$s,$p,$d,$src,$u,$n,$now)
            """, tx, ("$m", p.MaterialId), ("$s", string.IsNullOrEmpty(p.SupplierCode) ? null : p.SupplierCode), ("$p", p.PricePerKg),
            ("$d", p.EffectiveDate), ("$src", p.Source), ("$u", p.IsUncertain ? 1 : 0), ("$n", p.Note), ("$now", MfgDb.Now()));

    public void DeletePrice(long priceId)
    {
        using var c = MfgDb.Open();
        MfgDb.Exec(c, "DELETE FROM MaterialPrice WHERE Id=$id", null, ("$id", priceId));
    }

    public List<MaterialComponent> GetComponents(long materialId)
    {
        using var c = MfgDb.Open();
        return GetAllComponents(c).Where(x => x.MaterialId == materialId).ToList();
    }

    internal static List<MaterialComponent> GetAllComponents(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT * FROM MaterialComponent ORDER BY MaterialId, SortOrder, Id");
        using var r = cmd.ExecuteReader();
        var list = new List<MaterialComponent>();
        while (r.Read())
        {
            list.Add(new MaterialComponent
            {
                Id = r.Lng("Id"), MaterialId = r.Lng("MaterialId"), SortOrder = (int)r.Lng("SortOrder"),
                ComponentName = r.Str("ComponentName"), Cas = r.Str("Cas"), Ratio = r.Dbl("Ratio"),
            });
        }
        return list;
    }

    public void ReplaceComponents(long materialId, IEnumerable<MaterialComponent> components)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        ReplaceComponents(c, materialId, components, tx);
        tx.Commit();
    }

    internal void ReplaceComponents(SqliteConnection c, long materialId, IEnumerable<MaterialComponent> components, SqliteTransaction? tx)
    {
        MfgDb.Exec(c, "DELETE FROM MaterialComponent WHERE MaterialId=$m", tx, ("$m", materialId));
        var i = 0;
        foreach (var comp in components)
        {
            MfgDb.Exec(c, "INSERT INTO MaterialComponent(MaterialId,SortOrder,ComponentName,Cas,Ratio) VALUES($m,$o,$n,$c,$r)", tx,
                ("$m", materialId), ("$o", i++), ("$n", comp.ComponentName), ("$c", comp.Cas), ("$r", comp.Ratio));
        }
    }

    /// <summary>원료별 사용 제조법 코드 목록(원료 화면의 "사용 제조법" 표시용).</summary>
    public Dictionary<long, List<string>> GetUsage()
    {
        using var c = MfgDb.Open();
        using var cmd = MfgDb.Cmd(c, "SELECT DISTINCT l.MaterialId, r.Code FROM RecipeLine l JOIN Recipe r ON r.Id=l.RecipeId ORDER BY r.Code");
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
