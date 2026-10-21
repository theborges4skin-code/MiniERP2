using System.Text.RegularExpressions;
using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;

namespace MiniERP2.MFG.Import;

/// <summary>
/// 원료 코드 차용(사용자 요청 2026-10-21): master DB(MiniERP2 ItemTable)에서 그 원료를 소분 판매하는 SKU 계열을 찾아
/// 기본 SKU를 원료 코드로 쓴다. 예) "코코글루코사이드" ↔ master {cocog(1L), cocog18, cocog50, …} → "mfg_cocog".
///
/// 계열 찾기: master 상품명에서 채널 접두어·용량·수식어(앰플/파우더/분말/괄호 설명 등)를 떼어 낸 "기본 이름"이 원료명·별칭·
/// 전성분명과 같으면 그 계열로 본다. 쿠팡/아마존/이공 채널 SKU(cpg/cpr/amz/ig로 시작)는 같은 원료의 채널용 사본이라 제외한다.
/// 기본 SKU 고르기: ① 상품명이 1L/1kg인 SKU(끝의 "1kg" 같은 용량 표기는 뗌) ② 없으면 계열 SKU들의 공통 앞부분.
/// 차용할 것이 없거나, 다른 원료가 이미 같은 코드를 쓰면 차용하지 않는다(사용자가 파일 일괄수정으로 정리).
/// </summary>
public static partial class CodeBorrowService
{
    /// <summary>원료명과 master 상품명의 표기가 달라 이름만으로는 못 잇는 알려진 대응(근거: 단가·비중 일치 확인).</summary>
    private static readonly Dictionary<string, string[]> KnownAliases = new()
    {
        ["글리세린"] = ["VG"],                 // master "VG 1L"
        ["프로판다이올"] = ["1,3-PG"],          // master "1,3-PG 1L" — PM 원료 8500×1.05 = 원료목록 8500원·비중 1.053과 일치
        ["에틸헥실글리세린"] = ["에칠헥실글리세린"], // master "에칠헥실글리세린 1KG"
    };

    public sealed record Proposal(long MaterialId, string OldCode, string MaterialName, string NewCode, string Basis);

    /// <summary>원료별 차용 코드 제안. 차용 못 한 원료는 목록에 없다.</summary>
    public static List<Proposal> Propose(IReadOnlyList<Material> materials, IEnumerable<(string Sku, string Name)> masterItems)
    {
        var families = new Dictionary<string, List<(string Sku, string Name)>>();
        foreach (var (sku, name) in masterItems)
        {
            if (ChannelSkuRegex().IsMatch(sku)) continue;
            var key = Normalize(BaseName(name));
            if (key.Length == 0) continue;
            if (!families.TryGetValue(key, out var list)) families[key] = list = [];
            list.Add((sku, name));
        }

        var proposals = new List<Proposal>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in materials)
        {
            var keys = new List<string> { m.Name, m.Inci };
            keys.AddRange(m.AliasList);
            if (KnownAliases.TryGetValue(m.Name, out var extra)) keys.AddRange(extra);
            // 원료명 괄호 안 설명(예: "D-판테놀(75%)")을 뗀 형태도 본다.
            keys.AddRange(keys.ToList().Select(k => ParenRegex().Replace(k, "")));

            var family = keys.Select(Normalize).Where(k => k.Length >= 2).Distinct()
                .Select(k => families.GetValueOrDefault(k)).FirstOrDefault(f => f is not null);
            if (family is null) continue;

            var stem = PickStem(family);
            if (stem is null) continue;
            var code = MfgCodes.Prefix + stem;
            if (!used.Add(code)) continue; // 같은 계열을 두 원료가 가리키면 먼저 나온 원료만 차용
            proposals.Add(new Proposal(m.Id, m.Code, m.Name, code,
                $"master {string.Join(", ", family.Select(f => f.Sku).Take(6))}{(family.Count > 6 ? " …" : "")}"));
        }
        return proposals;
    }

    /// <summary>계열에서 기본 SKU(줄기)를 고른다.</summary>
    internal static string? PickStem(List<(string Sku, string Name)> family)
    {
        var oneLiter = family.Where(f => OneUnitRegex().IsMatch(f.Name)).Select(f => f.Sku).OrderBy(s => s.Length).FirstOrDefault();
        if (oneLiter is not null)
        {
            var trimmed = TrailingOneUnitRegex().Replace(oneLiter, "");
            return trimmed.Length >= 2 ? trimmed : oneLiter;
        }

        var prefix = family.Select(f => f.Sku).Aggregate((a, b) =>
        {
            var n = 0;
            while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
            return a[..n];
        });
        if (family.Count == 1) prefix = TrailingSizeRegex().Replace(prefix, "");
        // 공통 앞부분이 숫자 중간에서 잘렸으면(예: "dp100","dp10amp" → "dp10") 그 숫자는 용량이므로 뗀다.
        // "tw80_100","tw80_18" → "tw80_"처럼 숫자 뒤가 구분자면 "tw80"의 80은 이름의 일부라 남긴다.
        if (family.Count > 1 && prefix.Length > 0 && char.IsDigit(prefix[^1]) &&
            family.Any(f => f.Sku.Length > prefix.Length && char.IsDigit(f.Sku[prefix.Length])))
        {
            prefix = prefix.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        }
        prefix = prefix.TrimEnd('_', '-', '.');
        return prefix.Length >= 2 ? prefix : null;
    }

    /// <summary>상품명 → 기본 이름(채널 접두어·용량·수식어·괄호 설명 제거).</summary>
    internal static string BaseName(string name)
    {
        var n = ChannelNameRegex().Replace(name, "");
        n = SizeRegex().Replace(n, " ");
        n = ParenRegex().Replace(n, " ");
        n = QualifierRegex().Replace(n, " ");
        return n.Trim();
    }

    internal static string Normalize(string s) => NormalizeRegex().Replace(s ?? string.Empty, "").ToUpperInvariant();

    /// <summary>
    /// 옛 형식 코드(RM###/PK###/H14)를 mfg_ 형식(mfg_rm###/mfg_pk###/mfg_h14)으로 바꾼다. 번호는 그대로 살린다.
    /// 이미 mfg_로 시작하는 코드는 건드리지 않는다(여러 번 실행해도 안전). 바꾼 건수를 돌려준다.
    /// </summary>
    public static (int Materials, int Packs, int Recipes) MigrateLegacyCodes()
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        var m = Rename(c, tx, "Material", code => MfgCodes.MaterialAutoPrefix + DigitsOr(code));
        var p = Rename(c, tx, "PackMaterial", code => MfgCodes.PackAutoPrefix + DigitsOr(code));
        var r = Rename(c, tx, "Recipe", code => MfgCodes.Prefix + code.ToLowerInvariant());
        MfgDb.Exec(c, "INSERT OR REPLACE INTO AppMeta(Key, Value) VALUES('CodeScheme', 'mfg_v1')", tx);
        tx.Commit();
        return (m, p, r);
    }

    private static string DigitsOr(string code)
    {
        var d = DigitsRegex().Match(code).Value;
        return d.Length > 0 ? d.PadLeft(3, '0') : Normalize(code).ToLowerInvariant();
    }

    private static int Rename(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, string table, Func<string, string> map)
    {
        var rows = new List<(long Id, string Code)>();
        using (var cmd = MfgDb.Cmd(c, $"SELECT Id, Code FROM {table}", tx))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) rows.Add((r.GetInt64(0), r.GetString(1)));
        }
        var targets = rows.Where(x => !MfgCodes.HasPrefix(x.Code)).ToList();
        // UNIQUE 충돌을 피하려고 임시 코드로 먼저 옮긴 뒤 최종 코드를 넣는다.
        foreach (var (id, _) in targets) MfgDb.Exec(c, $"UPDATE {table} SET Code=$t WHERE Id=$id", tx, ("$t", "__tmp_" + id), ("$id", id));
        var taken = new HashSet<string>(rows.Where(x => MfgCodes.HasPrefix(x.Code)).Select(x => x.Code), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, code) in targets)
        {
            var next = map(code);
            for (var n = 2; !taken.Add(next); n++) next = map(code) + "_" + n;
            MfgDb.Exec(c, $"UPDATE {table} SET Code=$c WHERE Id=$id", tx, ("$c", next), ("$id", id));
        }
        return targets.Count;
    }

    /// <summary>
    /// 원료 코드 차용 적용: 자동 번호 코드(mfg_rm###)인 원료 중 master에 소분 판매 SKU 계열이 있는 것을 "mfg_" + 기본 SKU로 바꾼다.
    /// 사용자가 이미 고친 코드(mfg_rm### 형식이 아닌 것)는 건드리지 않는다. 결과 리포트 텍스트를 돌려준다.
    /// </summary>
    public static (int Borrowed, string Report) ApplyBorrowedMaterialCodes(IEnumerable<(string Sku, string Name)> masterItems)
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        var materials = new MaterialRepository().GetAll(c, true, tx);
        var taken = new HashSet<string>(materials.Select(m => m.Code), StringComparer.OrdinalIgnoreCase);
        var targets = materials.Where(m => AutoMaterialCodeRegex().IsMatch(m.Code)).ToList();
        var lines = new List<string>();
        foreach (var p in Propose(targets, masterItems))
        {
            if (!taken.Add(p.NewCode)) { lines.Add($"  (건너뜀) {p.OldCode} {p.MaterialName} → {p.NewCode} 이미 다른 원료가 사용 중"); continue; }
            var m = materials.First(x => x.Id == p.MaterialId);
            foreach (var alias in KnownAliases.GetValueOrDefault(m.Name) ?? [])
            {
                if (!m.AliasList.Contains(alias)) m.Aliases = string.IsNullOrEmpty(m.Aliases) ? alias : m.Aliases + "\n" + alias;
            }
            MfgDb.Exec(c, "UPDATE Material SET Code=$code, Aliases=$al, UpdatedAt=$now WHERE Id=$id", tx,
                ("$code", p.NewCode), ("$al", m.Aliases), ("$now", MfgDb.Now()), ("$id", m.Id));
            lines.Add($"  {p.OldCode} {p.MaterialName} → {p.NewCode}   ({p.Basis})");
        }
        tx.Commit();
        var borrowed = lines.Count(l => !l.StartsWith("  (건너뜀)", StringComparison.Ordinal));
        var left = targets.Count - borrowed;
        return (borrowed, $"[원료 코드 master 차용] {borrowed}개 차용 / 차용 못 해 번호 코드(mfg_rm###)로 남은 원료 {left}개\n" +
                          "남은 원료는 [데이터 관리] 탭 → 원료 → 엑셀 내보내기 → 코드 열 수정 → 엑셀 불러오기 → 변경내역 저장으로 고치세요.\n" +
                          string.Join("\n", lines));
    }

    /// <summary>옛 형식 코드가 남아 있는지(시작 시 자동 변환 판단용).</summary>
    public static bool NeedsLegacyMigration()
    {
        using var c = MfgDb.Open();
        using var cmd = MfgDb.Cmd(c, """
            SELECT (SELECT COUNT(*) FROM Material WHERE substr(Code,1,4) <> 'mfg_')
                 + (SELECT COUNT(*) FROM PackMaterial WHERE substr(Code,1,4) <> 'mfg_')
                 + (SELECT COUNT(*) FROM Recipe WHERE substr(Code,1,4) <> 'mfg_')
            """);
        return Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    [GeneratedRegex(@"^mfg_rm\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex AutoMaterialCodeRegex();

    [GeneratedRegex(@"^(cpg|cpr|amz|ig)", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelSkuRegex();

    [GeneratedRegex(@"^(쿠팡그로스_|쿠팡로켓_|이공이공\s*|이공\s+)")]
    private static partial Regex ChannelNameRegex();

    [GeneratedRegex(@"\d+(\.\d+)?\s*(ml|l|kg|g)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ParenRegex();

    [GeneratedRegex(@"(앰플|말통|대용량|상차도|소분|파우더|분말|1\+1|무배|신규)")]
    private static partial Regex QualifierRegex();

    [GeneratedRegex(@"[\s\-_,.·/%]")]
    private static partial Regex NormalizeRegex();

    [GeneratedRegex(@"(^|\s)1\s*(L|l|kg|KG|Kg)\b")]
    private static partial Regex OneUnitRegex();

    [GeneratedRegex(@"1(kg|l)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingOneUnitRegex();

    [GeneratedRegex(@"\d+(\.\d+)?(kg|l|ml|g|amp)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingSizeRegex();

    [GeneratedRegex(@"\d+$")]
    private static partial Regex DigitsRegex();
}
