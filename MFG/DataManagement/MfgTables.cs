using System.Data;
using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.DataManagement;

// mfgdb 테이블별 [데이터 관리] 어댑터. 다른 표를 가리키는 칸은 Id 대신 코드(원료코드·부자재코드·제조법코드·SKU)로 보여 주고
// 저장할 때 Id로 바꾼다 — 사람이 엑셀에서 읽고 고칠 수 있게. 금액은 저장 규칙대로 VAT포함이다(열 이름에 표시).

internal static class Cols
{
    public static void Add(DataTable t, params (string Name, Type Type)[] cols)
    {
        foreach (var (name, type) in cols) t.Columns.Add(name, type);
    }

    public static object N(object? v) => v ?? DBNull.Value;

    public static string Now() => MfgDb.Now();
}

public class MaterialTable : ManagedTable
{
    public override string DisplayName => "원료";
    public override string Description => "코드를 고치면 배합·단가 등 모든 곳에 그대로 반영됩니다(내부는 Id로 연결). 코드를 비워 두고 새 행을 넣으면 mfg_rm### 자동 부여. 별칭은 줄바꿈 또는 ; 로 구분.";
    public override string[] FallbackKeyColumns => ["코드"];
    public override string[] InfoColumns => ["현재단가_원kg_VAT포함", "현재단가_적용일"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("코드", typeof(string)), ("원료명", typeof(string)),
        ("별칭", typeof(string)), ("전성분명", typeof(string)), ("CAS", typeof(string)), ("역할", typeof(string)), ("비중", typeof(double)),
        ("혼합물", typeof(bool)), ("면세", typeof(bool)), ("사용", typeof(bool)), ("기본매입처코드", typeof(string)), ("비고", typeof(string)),
        ("현재단가_원kg_VAT포함", typeof(double)), ("현재단가_적용일", typeof(string)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c) =>
        new MaterialRepository().GetAll(c).Select(m => new object?[]
        {
            m.Id, m.Code, m.Name, m.Aliases, m.Inci, m.Cas, m.Role, Cols.N(m.Density), m.IsMixture, m.IsTaxFree, m.IsActive,
            Cols.N(m.DefaultSupplierCode), m.Note, Cols.N(m.CurrentPrice?.PricePerKg), Cols.N(m.CurrentPrice?.EffectiveDate),
        });

    private static Material From(DataRow r, long id) => new()
    {
        Id = id, Code = S(r, "코드"), Name = Required(r, "원료명"), Aliases = S(r, "별칭").Replace(";", "\n").Replace("\r", ""),
        Inci = S(r, "전성분명"), Cas = S(r, "CAS"), Role = S(r, "역할"), Density = ND(r, "비중"), IsMixture = B(r, "혼합물"),
        IsTaxFree = B(r, "면세"), IsActive = B(r, "사용", true), DefaultSupplierCode = NS(r, "기본매입처코드"), Note = S(r, "비고"),
    };

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => new MaterialRepository().Save(c, From(r, 0), tx);

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx)
    {
        var m = From(r, CurrentId(r));
        if (m.Code.Length == 0) throw new InvalidDataException("코드를 비울 수 없습니다.");
        MfgDb.Exec(c, "UPDATE Material SET Code=$code WHERE Id=$id", tx, ("$code", m.Code), ("$id", m.Id));
        new MaterialRepository().Save(c, m, tx);
    }

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM Material WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class MaterialPriceTable : ManagedTable
{
    public override string DisplayName => "원료단가";
    public override string Description => "단가는 원/kg, VAT포함. 단가 변동은 새 행(새 적용일)으로 추가하세요 — 가장 최근 적용일이 현재 단가입니다. 적용일 형식 yyyy-MM-dd.";
    public override string[] InfoColumns => ["원료명"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("원료코드", typeof(string)), ("원료명", typeof(string)),
        ("매입처코드", typeof(string)), ("단가_원kg_VAT포함", typeof(double)), ("적용일", typeof(string)), ("출처", typeof(string)),
        ("불확실", typeof(bool)), ("메모", typeof(string)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT p.*, m.Code, m.Name FROM MaterialPrice p JOIN Material m ON m.Id=p.MaterialId ORDER BY m.Code, p.EffectiveDate DESC, p.Id DESC");
        using var r = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (r.Read())
        {
            list.Add([r.Lng("Id"), r.Str("Code"), r.Str("Name"), Cols.N(r.NStr("SupplierCode")), r.Dbl("PricePerKg"), r.Str("EffectiveDate"),
                r.Str("Source"), r.Bool("IsUncertain"), r.Str("Note")]);
        }
        return list;
    }

    private static (string, object?)[] Args(DataRow r, SqliteConnection c, SqliteTransaction tx, long id) =>
    [
        ("$m", IdByCode(c, tx, "Material", Required(r, "원료코드"), "원료코드")), ("$s", NullIfEmpty(S(r, "매입처코드"))),
        ("$p", ND(r, "단가_원kg_VAT포함") ?? throw new InvalidDataException("단가가 비어 있습니다.")), ("$d", S(r, "적용일")),
        ("$src", S(r, "출처")), ("$u", B(r, "불확실") ? 1 : 0), ("$n", S(r, "메모")), ("$now", Cols.Now()), ("$id", id),
    ];

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "INSERT INTO MaterialPrice(MaterialId,SupplierCode,PricePerKg,EffectiveDate,Source,IsUncertain,Note,CreatedAt) VALUES($m,$s,$p,$d,$src,$u,$n,$now)", tx, Args(r, c, tx, 0));

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "UPDATE MaterialPrice SET MaterialId=$m,SupplierCode=$s,PricePerKg=$p,EffectiveDate=$d,Source=$src,IsUncertain=$u,Note=$n WHERE Id=$id", tx, Args(r, c, tx, CurrentId(r)));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM MaterialPrice WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class MaterialComponentTable : ManagedTable
{
    public override string DisplayName => "혼합물구성";
    public override string Description => "혼합 원료의 구성성분(전성분 표기용). 구성비율은 % 단위(원료별 합계 100%).";
    public override string[] InfoColumns => ["원료명"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("원료코드", typeof(string)), ("원료명", typeof(string)),
        ("순서", typeof(long)), ("구성성분", typeof(string)), ("CAS", typeof(string)), ("구성비율_%", typeof(double)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT x.*, m.Code, m.Name FROM MaterialComponent x JOIN Material m ON m.Id=x.MaterialId ORDER BY m.Code, x.SortOrder, x.Id");
        using var r = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (r.Read()) list.Add([r.Lng("Id"), r.Str("Code"), r.Str("Name"), r.Lng("SortOrder"), r.Str("ComponentName"), r.Str("Cas"), r.Dbl("Ratio") * 100]);
        return list;
    }

    private static (string, object?)[] Args(DataRow r, SqliteConnection c, SqliteTransaction tx, long id) =>
    [
        ("$m", IdByCode(c, tx, "Material", Required(r, "원료코드"), "원료코드")), ("$o", NL(r, "순서") ?? 0), ("$n", Required(r, "구성성분")),
        ("$c", S(r, "CAS")), ("$r", D(r, "구성비율_%") / 100), ("$id", id),
    ];

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "INSERT INTO MaterialComponent(MaterialId,SortOrder,ComponentName,Cas,Ratio) VALUES($m,$o,$n,$c,$r)", tx, Args(r, c, tx, 0));

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "UPDATE MaterialComponent SET MaterialId=$m,SortOrder=$o,ComponentName=$n,Cas=$c,Ratio=$r WHERE Id=$id", tx, Args(r, c, tx, CurrentId(r)));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM MaterialComponent WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class PackMaterialTable : ManagedTable
{
    public override string DisplayName => "부자재";
    public override string Description => $"분류: {string.Join(" / ", SizeCostCategory.PackCategories)}. 코드를 비워 두고 새 행을 넣으면 mfg_pk### 자동 부여.";
    public override string[] FallbackKeyColumns => ["코드"];
    public override string[] InfoColumns => ["현재단가_원개_VAT포함", "현재단가_적용일"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("코드", typeof(string)), ("분류", typeof(string)),
        ("품명", typeof(string)), ("기본매입처코드", typeof(string)), ("면세", typeof(bool)), ("사용", typeof(bool)), ("비고", typeof(string)),
        ("현재단가_원개_VAT포함", typeof(double)), ("현재단가_적용일", typeof(string)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c) =>
        new PackMaterialRepository().GetAll(c).OrderBy(p => p.Code).Select(p => new object?[]
        {
            p.Id, p.Code, p.Category, p.Name, Cols.N(p.DefaultSupplierCode), p.IsTaxFree, p.IsActive, p.Note,
            Cols.N(p.CurrentPrice?.UnitPrice), Cols.N(p.CurrentPrice?.EffectiveDate),
        });

    private static PackMaterial From(DataRow r, long id)
    {
        var cat = S(r, "분류");
        if (cat.Length > 0 && !SizeCostCategory.PackCategories.Contains(cat)) throw new InvalidDataException($"분류 '{cat}'은(는) 없는 분류입니다({string.Join("/", SizeCostCategory.PackCategories)}).");
        return new PackMaterial
        {
            Id = id, Code = S(r, "코드"), Category = cat.Length > 0 ? cat : SizeCostCategory.EtcPack, Name = Required(r, "품명"),
            DefaultSupplierCode = NS(r, "기본매입처코드"), IsTaxFree = B(r, "면세"), IsActive = B(r, "사용", true), Note = S(r, "비고"),
        };
    }

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => new PackMaterialRepository().Save(c, From(r, 0), tx);

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx)
    {
        var p = From(r, CurrentId(r));
        if (p.Code.Length == 0) throw new InvalidDataException("코드를 비울 수 없습니다.");
        MfgDb.Exec(c, "UPDATE PackMaterial SET Code=$code WHERE Id=$id", tx, ("$code", p.Code), ("$id", p.Id));
        new PackMaterialRepository().Save(c, p, tx);
    }

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM PackMaterial WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class PackPriceTable : ManagedTable
{
    public override string DisplayName => "부자재단가";
    public override string Description => "단가는 원/개, VAT포함. 단가 변동은 새 행(새 적용일)으로 추가하세요.";
    public override string[] InfoColumns => ["품명"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("부자재코드", typeof(string)), ("품명", typeof(string)),
        ("매입처코드", typeof(string)), ("단가_원개_VAT포함", typeof(double)), ("적용일", typeof(string)), ("출처", typeof(string)), ("메모", typeof(string)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT p.*, k.Code, k.Name FROM PackMaterialPrice p JOIN PackMaterial k ON k.Id=p.PackMaterialId ORDER BY k.Code, p.EffectiveDate DESC, p.Id DESC");
        using var r = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (r.Read()) list.Add([r.Lng("Id"), r.Str("Code"), r.Str("Name"), Cols.N(r.NStr("SupplierCode")), r.Dbl("UnitPrice"), r.Str("EffectiveDate"), r.Str("Source"), r.Str("Note")]);
        return list;
    }

    private static (string, object?)[] Args(DataRow r, SqliteConnection c, SqliteTransaction tx, long id) =>
    [
        ("$p", IdByCode(c, tx, "PackMaterial", Required(r, "부자재코드"), "부자재코드")), ("$s", NullIfEmpty(S(r, "매입처코드"))),
        ("$u", ND(r, "단가_원개_VAT포함") ?? throw new InvalidDataException("단가가 비어 있습니다.")), ("$d", S(r, "적용일")),
        ("$src", S(r, "출처")), ("$n", S(r, "메모")), ("$now", Cols.Now()), ("$id", id),
    ];

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "INSERT INTO PackMaterialPrice(PackMaterialId,SupplierCode,UnitPrice,EffectiveDate,Source,Note,CreatedAt) VALUES($p,$s,$u,$d,$src,$n,$now)", tx, Args(r, c, tx, 0));

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "UPDATE PackMaterialPrice SET PackMaterialId=$p,SupplierCode=$s,UnitPrice=$u,EffectiveDate=$d,Source=$src,Note=$n WHERE Id=$id", tx, Args(r, c, tx, CurrentId(r)));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM PackMaterialPrice WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class RecipeTable : ManagedTable
{
    public override string DisplayName => "제조법";
    public override string Description => "제조법 머리(이름·버전·비중 등). 상태(확정/미확정)는 화면의 확정 버튼으로만 바꿉니다(파일에서 고쳐도 무시). 코드는 mfg_ + 분류(c/s/h/d) + 번호 권장.";
    public override string[] FallbackKeyColumns => ["코드"];
    public override string[] InfoColumns => ["상태"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("코드", typeof(string)), ("제품명", typeof(string)),
        ("버전", typeof(string)), ("원본시트", typeof(string)), ("상태", typeof(string)), ("실측비중", typeof(double)), ("배치kg", typeof(double)),
        ("공정메모", typeof(string)), ("보관", typeof(bool)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c) =>
        new RecipeRepository().GetAll(c, withDetails: false).Select(x => new object?[]
        {
            x.Id, x.Code, x.Name, x.Version, x.SourceSheet, x.Status, Cols.N(x.MeasuredDensity), x.BatchKg, x.ProcessMemo, x.IsArchived,
        });

    private static (string, object?)[] Args(DataRow r, long id) =>
    [
        ("$code", Required(r, "코드")), ("$name", Required(r, "제품명")), ("$ver", S(r, "버전")), ("$src", S(r, "원본시트")),
        ("$md", ND(r, "실측비중")), ("$bk", D(r, "배치kg", 10)), ("$pm", S(r, "공정메모")), ("$ar", B(r, "보관") ? 1 : 0), ("$now", Cols.Now()), ("$id", id),
    ];

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c, """
        INSERT INTO Recipe(Code,Name,Version,SourceSheet,Status,MeasuredDensity,BatchKg,ProcessMemo,IsArchived,CreatedAt,UpdatedAt)
        VALUES($code,$name,$ver,$src,'미확정',$md,$bk,$pm,$ar,$now,$now)
        """, tx, Args(r, 0));

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c, """
        UPDATE Recipe SET Code=$code,Name=$name,Version=$ver,SourceSheet=$src,MeasuredDensity=$md,BatchKg=$bk,ProcessMemo=$pm,IsArchived=$ar,UpdatedAt=$now WHERE Id=$id
        """, tx, Args(r, CurrentId(r)));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM Recipe WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class RecipeLineTable : ManagedTable
{
    public override string DisplayName => "배합행";
    public override string Description => "제조법별 원료와 투입량(% 단위, 제조법별 합계 100%). 순서는 작은 수부터.";
    public override string[] InfoColumns => ["원료명"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("제조법코드", typeof(string)), ("순서", typeof(long)),
        ("단계", typeof(string)), ("원료코드", typeof(string)), ("원료명", typeof(string)), ("투입량_%", typeof(double)), ("메모", typeof(string)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT l.*, r.Code RCode, m.Code MCode, m.Name MName FROM RecipeLine l JOIN Recipe r ON r.Id=l.RecipeId JOIN Material m ON m.Id=l.MaterialId ORDER BY r.Code, l.SortOrder, l.Id");
        using var rd = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (rd.Read()) list.Add([rd.Lng("Id"), rd.Str("RCode"), rd.Lng("SortOrder"), rd.Str("Stage"), rd.Str("MCode"), rd.Str("MName"), rd.Dbl("Percent") * 100, rd.Str("Memo")]);
        return list;
    }

    private static (string, object?)[] Args(DataRow r, SqliteConnection c, SqliteTransaction tx, long id) =>
    [
        ("$r", IdByCode(c, tx, "Recipe", Required(r, "제조법코드"), "제조법코드")), ("$o", NL(r, "순서") ?? 0), ("$s", S(r, "단계")),
        ("$m", IdByCode(c, tx, "Material", Required(r, "원료코드"), "원료코드")), ("$p", D(r, "투입량_%") / 100), ("$memo", S(r, "메모")), ("$id", id),
    ];

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "INSERT INTO RecipeLine(RecipeId,SortOrder,Stage,MaterialId,Percent,Memo) VALUES($r,$o,$s,$m,$p,$memo)", tx, Args(r, c, tx, 0));

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "UPDATE RecipeLine SET RecipeId=$r,SortOrder=$o,Stage=$s,MaterialId=$m,Percent=$p,Memo=$memo WHERE Id=$id", tx, Args(r, c, tx, CurrentId(r)));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM RecipeLine WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class RecipeSizeTable : ManagedTable
{
    public override string DisplayName => "제조법용량";
    public override string Description => "제조법별 완제품 용량(ml). 용량을 지우면 그 용량에 연결된 완제품의 연결이 풀립니다. 용량별 부자재·가공비는 '용량별원가' 표.";

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("제조법코드", typeof(string)), ("순서", typeof(long)),
        ("용량ml", typeof(double)), ("메모", typeof(string)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT s.*, r.Code FROM RecipeSize s JOIN Recipe r ON r.Id=s.RecipeId ORDER BY r.Code, s.SortOrder, s.Id");
        using var rd = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (rd.Read()) list.Add([rd.Lng("Id"), rd.Str("Code"), rd.Lng("SortOrder"), rd.Dbl("VolumeMl"), rd.Str("Memo")]);
        return list;
    }

    private static (string, object?)[] Args(DataRow r, SqliteConnection c, SqliteTransaction tx, long id) =>
    [
        ("$r", IdByCode(c, tx, "Recipe", Required(r, "제조법코드"), "제조법코드")), ("$o", NL(r, "순서") ?? 0),
        ("$v", ND(r, "용량ml") is > 0 and var v ? v : throw new InvalidDataException("용량ml은 0보다 커야 합니다.")), ("$m", S(r, "메모")), ("$id", id),
    ];

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "INSERT INTO RecipeSize(RecipeId,SortOrder,VolumeMl,Memo) VALUES($r,$o,$v,$m)", tx, Args(r, c, tx, 0));

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "UPDATE RecipeSize SET RecipeId=$r,SortOrder=$o,VolumeMl=$v,Memo=$m WHERE Id=$id", tx, Args(r, c, tx, CurrentId(r)));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM RecipeSize WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class RecipeSizeCostTable : ManagedTable
{
    public override string DisplayName => "용량별원가";
    public override string Description => $"용량별 부자재·가공비. 항목: {string.Join(" / ", SizeCostCategory.All)}. 부자재코드를 넣으면 그 부자재의 현재 단가×수량, 비우면 직접금액(VAT포함). 용량Id는 '제조법용량' 표의 Id.";
    public override string[] InfoColumns => ["제조법코드", "용량ml"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("용량Id", typeof(long)), ("제조법코드", typeof(string)),
        ("용량ml", typeof(double)), ("항목", typeof(string)), ("부자재코드", typeof(string)), ("직접금액_VAT포함", typeof(double)), ("수량", typeof(double)), ("메모", typeof(string)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, """
            SELECT x.*, r.Code RCode, s.VolumeMl, k.Code KCode FROM RecipeSizeCost x JOIN RecipeSize s ON s.Id=x.RecipeSizeId
            JOIN Recipe r ON r.Id=s.RecipeId LEFT JOIN PackMaterial k ON k.Id=x.PackMaterialId ORDER BY r.Code, s.SortOrder, x.Id
            """);
        using var rd = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (rd.Read())
        {
            list.Add([rd.Lng("Id"), rd.Lng("RecipeSizeId"), rd.Str("RCode"), rd.Dbl("VolumeMl"), rd.Str("Category"), Cols.N(rd.NStr("KCode")),
                Cols.N(rd.NDbl("ManualAmount")), rd.Dbl("Quantity"), rd.Str("Memo")]);
        }
        return list;
    }

    private static (string, object?)[] Args(DataRow r, SqliteConnection c, SqliteTransaction tx, long id)
    {
        var cat = Required(r, "항목");
        if (!SizeCostCategory.All.Contains(cat)) throw new InvalidDataException($"항목 '{cat}'은(는) 없는 항목입니다.");
        var sizeId = NL(r, "용량Id") ?? throw new InvalidDataException("용량Id가 비어 있습니다.");
        using (var chk = MfgDb.Cmd(c, "SELECT COUNT(*) FROM RecipeSize WHERE Id=$s", tx, ("$s", sizeId)))
        {
            if (Convert.ToInt64(chk.ExecuteScalar()) == 0) throw new InvalidDataException($"용량Id {sizeId}이(가) 없습니다.");
        }
        return
        [
            ("$s", sizeId), ("$c", cat), ("$p", OptionalIdByCode(c, tx, "PackMaterial", NS(r, "부자재코드"), "부자재코드")),
            ("$a", ND(r, "직접금액_VAT포함")), ("$q", D(r, "수량", 1)), ("$m", S(r, "메모")), ("$id", id),
        ];
    }

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "INSERT INTO RecipeSizeCost(RecipeSizeId,Category,PackMaterialId,ManualAmount,Quantity,Memo) VALUES($s,$c,$p,$a,$q,$m)", tx, Args(r, c, tx, 0));

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "UPDATE RecipeSizeCost SET RecipeSizeId=$s,Category=$c,PackMaterialId=$p,ManualAmount=$a,Quantity=$q,Memo=$m WHERE Id=$id", tx, Args(r, c, tx, CurrentId(r)));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM RecipeSizeCost WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class ProductTable : ManagedTable
{
    public override string DisplayName => "완제품";
    public override string Description => "판매 SKU 원가표(product_master와 같은 열). 금액은 VAT포함. 연결유형: 수동값 / 제조법 용량(용량Id) / 원료 소분(원료코드·수량·단위 ml/L/g/kg) / 묶음('묶음구성' 표). 상태·확정원가는 화면의 확정으로만 바뀝니다.";
    public override string[] FallbackKeyColumns => ["SKU"];
    public override string[] InfoColumns => ["상태", "확정원가_VAT포함"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("SKU", typeof(string)), ("상품명", typeof(string)),
        ("구분", typeof(string)), ("상품그룹", typeof(string)), ("소비자가", typeof(double)), ("연결유형", typeof(string)), ("용량Id", typeof(long)),
        ("원료코드", typeof(string)), ("원료수량", typeof(double)), ("원료단위", typeof(string)), ("원료_수동값", typeof(double)),
        ("용기", typeof(double)), ("패키지", typeof(double)), ("스티커등", typeof(double)), ("기타", typeof(double)), ("택배비", typeof(double)),
        ("비고", typeof(string)), ("연결후보", typeof(string)), ("상태", typeof(string)), ("확정원가_VAT포함", typeof(double)), ("순서", typeof(long)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        var materials = new MaterialRepository().GetAll(c).ToDictionary(m => m.Id, m => m.Code);
        return new ProductRepository().GetAll(c).Select(p => new object?[]
        {
            p.Id, Cols.N(p.Sku), p.Name, p.Category, p.ProductGroup, Cols.N(p.ConsumerPrice), ProductSourceType.Label(p.SourceType), Cols.N(p.RecipeSizeId),
            p.MaterialId is { } mid ? materials.GetValueOrDefault(mid) : DBNull.Value, Cols.N(p.MaterialQty), p.MaterialQtyUnit, p.ManualRaw,
            p.ContainerCost, p.PackageCost, p.StickerCost, p.EtcCost, p.ShippingCost, p.Notes, p.SuggestedLink, p.Status, Cols.N(p.ConfirmedTotal), (long)p.SortOrder,
        });
    }

    private static string ParseSourceType(string text) => text.Trim() switch
    {
        "" or "수동값" or "Manual" => ProductSourceType.Manual,
        "제조법 용량" or "제조법용량" or "RecipeSize" => ProductSourceType.RecipeSize,
        "원료 소분" or "원료소분" or "Material" => ProductSourceType.Material,
        "묶음" or "Bundle" => ProductSourceType.Bundle,
        _ => throw new InvalidDataException($"연결유형 '{text}'은(는) 없는 유형입니다(수동값/제조법 용량/원료 소분/묶음)."),
    };

    private static void Write(DataRow r, SqliteConnection c, SqliteTransaction tx, FinishedProduct p)
    {
        var unit = S(r, "원료단위");
        if (unit.Length > 0 && unit is not ("ml" or "L" or "g" or "kg")) throw new InvalidDataException($"원료단위 '{unit}' — ml/L/g/kg 중 하나");
        p.Sku = NullIfEmpty(S(r, "SKU"));
        p.Name = Required(r, "상품명");
        p.Category = S(r, "구분");
        p.ProductGroup = S(r, "상품그룹");
        p.ConsumerPrice = ND(r, "소비자가");
        p.SourceType = ParseSourceType(S(r, "연결유형"));
        p.RecipeSizeId = NL(r, "용량Id");
        p.MaterialId = OptionalIdByCode(c, tx, "Material", NS(r, "원료코드"), "원료코드");
        p.MaterialQty = ND(r, "원료수량");
        p.MaterialQtyUnit = unit.Length > 0 ? unit : "ml";
        p.ManualRaw = D(r, "원료_수동값");
        p.ContainerCost = D(r, "용기");
        p.PackageCost = D(r, "패키지");
        p.StickerCost = D(r, "스티커등");
        p.EtcCost = D(r, "기타");
        p.ShippingCost = D(r, "택배비");
        p.Notes = S(r, "비고");
        p.SuggestedLink = S(r, "연결후보");
        p.SortOrder = (int)(NL(r, "순서") ?? p.SortOrder);
        if (p.SourceType == ProductSourceType.RecipeSize && p.RecipeSizeId is null) throw new InvalidDataException("연결유형이 '제조법 용량'인데 용량Id가 비어 있습니다.");
        if (p.SourceType == ProductSourceType.Material && (p.MaterialId is null || p.MaterialQty is null)) throw new InvalidDataException("연결유형이 '원료 소분'인데 원료코드/원료수량이 비어 있습니다.");
        new ProductRepository().SaveHeaderOnly(c, p, tx);
    }

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => Write(r, c, tx, new FinishedProduct());

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx)
    {
        var existing = new ProductRepository().GetAll(c, tx).FirstOrDefault(p => p.Id == CurrentId(r)) ?? throw new InvalidDataException("행을 찾을 수 없습니다.");
        Write(r, c, tx, existing);
    }

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx)
    {
        MfgDb.Exec(c, "DELETE FROM FinishedProductComponent WHERE ComponentProductId=$id OR ProductId=$id", tx, ("$id", OriginalId(r)));
        MfgDb.Exec(c, "DELETE FROM FinishedProduct WHERE Id=$id", tx, ("$id", OriginalId(r)));
    }
}

public class BundleComponentTable : ManagedTable
{
    public override string DisplayName => "묶음구성";
    public override string Description => "연결유형 '묶음'인 완제품의 구성품. 묶음/구성품은 SKU로 지정(SKU 없는 품목은 상품Id 열 사용). 원료 열 = Σ(구성품 합계 × 수량).";
    public override string[] InfoColumns => ["묶음상품명", "구성상품명"];

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("Id", typeof(long)), ("묶음SKU", typeof(string)), ("묶음상품Id", typeof(long)),
        ("묶음상품명", typeof(string)), ("구성SKU", typeof(string)), ("구성상품Id", typeof(long)), ("구성상품명", typeof(string)), ("수량", typeof(double)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, """
            SELECT x.*, a.Sku ASku, a.Name AName, b.Sku BSku, b.Name BName FROM FinishedProductComponent x
            JOIN FinishedProduct a ON a.Id=x.ProductId JOIN FinishedProduct b ON b.Id=x.ComponentProductId ORDER BY a.SortOrder, x.Id
            """);
        using var rd = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (rd.Read())
        {
            list.Add([rd.Lng("Id"), Cols.N(rd.NStr("ASku")), rd.Lng("ProductId"), rd.Str("AName"), Cols.N(rd.NStr("BSku")), rd.Lng("ComponentProductId"), rd.Str("BName"), rd.Dbl("Quantity")]);
        }
        return list;
    }

    private static long Resolve(DataRow r, SqliteConnection c, SqliteTransaction tx, string skuCol, string idCol)
    {
        if (NS(r, skuCol) is { } sku)
        {
            using var cmd = MfgDb.Cmd(c, "SELECT Id FROM FinishedProduct WHERE Sku=$s", tx, ("$s", sku));
            return cmd.ExecuteScalar() is long id ? id : throw new InvalidDataException($"{skuCol} '{sku}'인 완제품이 없습니다.");
        }
        return NL(r, idCol) ?? throw new InvalidDataException($"{skuCol}와 {idCol}이 모두 비어 있습니다.");
    }

    private static (string, object?)[] Args(DataRow r, SqliteConnection c, SqliteTransaction tx, long id)
    {
        var a = Resolve(r, c, tx, "묶음SKU", "묶음상품Id");
        var b = Resolve(r, c, tx, "구성SKU", "구성상품Id");
        if (a == b) throw new InvalidDataException("묶음이 자기 자신을 구성품으로 가질 수 없습니다.");
        return [("$a", a), ("$b", b), ("$q", D(r, "수량", 1)), ("$id", id)];
    }

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "INSERT INTO FinishedProductComponent(ProductId,ComponentProductId,Quantity) VALUES($a,$b,$q)", tx, Args(r, c, tx, 0));

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) => MfgDb.Exec(c,
        "UPDATE FinishedProductComponent SET ProductId=$a,ComponentProductId=$b,Quantity=$q WHERE Id=$id", tx, Args(r, c, tx, CurrentId(r)));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "DELETE FROM FinishedProductComponent WHERE Id=$id", tx, ("$id", OriginalId(r)));
}

public class SupplierTable : ManagedTable
{
    public override string DisplayName => "매입처";
    public override string Description => "매입처는 MiniERP2 매입 채널을 불러온 것이라 여기서는 메모만 고칠 수 있습니다(추가·삭제·이름 변경은 MiniERP2 채널 설정).";
    public override string[] KeyColumns => ["채널코드"];
    public override string[] InfoColumns => ["매입처명", "마지막동기화"];
    public override bool AllowInsert => false;
    public override bool AllowDelete => false;

    protected override void DefineColumns(DataTable t) => Cols.Add(t, ("채널코드", typeof(string)), ("매입처명", typeof(string)), ("마지막동기화", typeof(string)), ("메모", typeof(string)));

    protected override IEnumerable<object?[]> LoadRows(SqliteConnection c)
    {
        using var cmd = MfgDb.Cmd(c, "SELECT * FROM Supplier ORDER BY Name");
        using var rd = cmd.ExecuteReader();
        var list = new List<object?[]>();
        while (rd.Read()) list.Add([rd.Str("ChannelCode"), rd.Str("Name"), Cols.N(rd.NStr("SyncedAt")), rd.Str("Memo")]);
        return list;
    }

    public override void Insert(DataRow r, SqliteConnection c, SqliteTransaction tx) => throw new InvalidOperationException("매입처는 MiniERP2에서 등록합니다.");

    public override void Update(DataRow r, SqliteConnection c, SqliteTransaction tx) =>
        MfgDb.Exec(c, "UPDATE Supplier SET Memo=$m WHERE ChannelCode=$c", tx, ("$m", S(r, "메모")), ("$c", S(r, "채널코드")));

    public override void Delete(DataRow r, SqliteConnection c, SqliteTransaction tx) => throw new InvalidOperationException("매입처는 MiniERP2에서 관리합니다.");
}

public static class MfgTables
{
    public static ManagedTable[] All() =>
    [
        new MaterialTable(), new MaterialPriceTable(), new MaterialComponentTable(), new PackMaterialTable(), new PackPriceTable(),
        new RecipeTable(), new RecipeLineTable(), new RecipeSizeTable(), new RecipeSizeCostTable(), new ProductTable(), new BundleComponentTable(), new SupplierTable(),
    ];
}
