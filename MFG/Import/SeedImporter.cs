using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Data;
using MiniERP2.MFG.Domain;
using OfficeOpenXml;

namespace MiniERP2.MFG.Import;

/// <summary>
/// 초기 데이터 세팅(docs/MFG_PLAN.md §5). 원본 엑셀은 읽기만 한다.
/// <list type="number">
/// <item>신제조법 원료목록 → Material + MaterialPrice(×1.1)</item>
/// <item>신제조법 단가이력 → MaterialPrice 이력(×1.1) + 원본 표기명을 원료 별칭으로</item>
/// <item>신제조법 혼합물구성 → MaterialComponent</item>
/// <item>신제조법 배합 시트(C/S/H/D) → Recipe/RecipeLine/RecipeSize/RecipeSizeCost — 전부 미확정</item>
/// <item>product_master 기타(부자재) 중 SKU 없는 행 → PackMaterial(×1.1), 매입처 자동 연결</item>
/// <item>product_master 판매 품목 → FinishedProduct(수동값, ×1.1, 미확정) + 연결 후보(MappingHints)</item>
/// </list>
/// 엑셀 가격은 전부 VAT별도(사용자 확인) → VAT포함으로 변환해 저장한다. 반올림하지 않는다.
/// 전체가 한 트랜잭션이라 중간에 실패하면 아무것도 남지 않는다.
/// </summary>
public partial class SeedImporter
{
    /// <summary>신제조법 작성일. 원료목록 단가에 입력일이 없을 때 적용일로 쓴다.</summary>
    public const string RecipeWorkbookDate = "2026-10-20";

    public ImportReport Report { get; } = new();

    private readonly MaterialRepository _materials = new();
    private readonly PackMaterialRepository _packs = new();
    private readonly RecipeRepository _recipes = new();
    private readonly ProductRepository _products = new();

    /// <summary>mfgdb에 이미 데이터가 있는지(원료·제조법·완제품 중 하나라도).</summary>
    public static bool HasData()
    {
        using var c = MfgDb.Open();
        using var cmd = MfgDb.Cmd(c, "SELECT (SELECT COUNT(*) FROM Material)+(SELECT COUNT(*) FROM Recipe)+(SELECT COUNT(*) FROM FinishedProduct)");
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>매입처를 제외한 모든 데이터를 지운다(초기 세팅 다시 하기용 — 호출 전 백업 필수).</summary>
    public static void ClearAll()
    {
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        foreach (var t in new[] { "FinishedProductComponent", "FinishedProduct", "RecipeConfirmLog", "RecipeSizeCost", "RecipeSize", "RecipeLine", "Recipe",
                     "PackMaterialPrice", "PackMaterial", "MaterialComponent", "MaterialPrice", "Material" })
        {
            MfgDb.Exec(c, $"DELETE FROM {t}", tx);
        }
        MfgDb.Exec(c, "DELETE FROM sqlite_sequence WHERE name <> 'Supplier'", tx);
        tx.Commit();
    }

    public void Run(string recipeWorkbookPath, string? productMasterPath, IReadOnlyList<Supplier> suppliers)
    {
        ExcelPackage.License.SetNonCommercialPersonal("MiniERP2");
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();

        // 원본이 다른 프로그램(엑셀)에 열려 있어도 읽을 수 있게 공유 모드 스트림으로 연다.
        using (var package = OpenShared(recipeWorkbookPath))
        {
            var wb = package.Workbook;
            var byName = ImportMaterials(c, tx, wb.Worksheets["원료목록"] ?? throw new InvalidDataException("'원료목록' 시트가 없습니다."));
            ImportPriceHistory(c, tx, wb.Worksheets["단가이력"], byName);
            ImportMixtures(c, tx, wb.Worksheets["혼합물구성"], byName);
            foreach (var ws in wb.Worksheets.Where(w => RecipeSheetRegex().IsMatch(w.Name)))
            {
                ImportRecipe(c, tx, ws, byName);
            }
        }

        if (!string.IsNullOrWhiteSpace(productMasterPath))
        {
            using var package = OpenShared(productMasterPath);
            ImportProductMaster(c, tx, package.Workbook.Worksheets[0], suppliers);
        }

        tx.Commit();
    }

    private static ExcelPackage OpenShared(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new ExcelPackage(stream);
    }

    // ---------------- 원료 ----------------

    private Dictionary<string, Material> ImportMaterials(SqliteConnection c, SqliteTransaction tx, ExcelWorksheet ws)
    {
        var byName = new Dictionary<string, Material>();
        var rows = ws.Dimension?.End.Row ?? 0;
        for (var r = 5; r <= rows; r++)
        {
            var name = Text(ws, r, 2);
            if (name.Length == 0) continue;
            var note = Text(ws, r, 11);
            var m = new Material
            {
                // 원료목록 코드 RM001 → mfg_rm001(번호 유지). master SKU 차용은 이관 뒤 CodeBorrowService가 따로 적용한다.
                Code = MfgCodes.MaterialAutoPrefix + Regex.Match(Text(ws, r, 1), @"\d+$").Value.PadLeft(3, '0'), Name = name, Inci = Text(ws, r, 3), Cas = Text(ws, r, 4), Role = Text(ws, r, 7),
                IsMixture = Text(ws, r, 8).Equals("Y", StringComparison.OrdinalIgnoreCase), Density = Num(ws, r, 9), Note = note,
            };
            var supplierText = Text(ws, r, 10);
            if (supplierText.Length > 0) m.Note = Join(m.Note, "공급처(엑셀): " + supplierText);
            _materials.Save(c, m, tx);
            byName[MaterialRepository.Normalize(name)] = m;
            Report.Materials++;

            if (Num(ws, r, 5) is { } price)
            {
                // 원료목록 단가 = 신제조법이 작성일(2026-10-20) 기준 "현재가"로 쓰던 확정값이다. 단가이력에는 이보다 늦은
                // 날짜(예: "2026-06")의 옛 시트 기록도 섞여 있어, 원래 입력일을 적용일로 쓰면 그 옛 기록이 현재 단가로
                // 잡혀 배합 원가가 엑셀과 달라진다(19개 배합에서 확인). 그래서 적용일은 작성일로 두고 원래 입력일은 메모에 남긴다.
                var originalDate = DateText(ws.Cells[r, 6].Value);
                var uncertain = note.Contains("확실치 않음", StringComparison.Ordinal);
                // 단가이력보다 나중에 넣어야 같은 적용일일 때 Id가 커서 "현재 단가"로 잡힌다 → 모아 두었다가 이력 뒤에 넣는다.
                _pendingListPrices.Add(new MaterialPrice
                {
                    MaterialId = m.Id, PricePerKg = Vat.FromExclusive(price), EffectiveDate = RecipeWorkbookDate, Source = "엑셀이관(원료목록)",
                    IsUncertain = uncertain,
                    Note = originalDate is null ? "원료목록 확정가(입력일 미기재)" : $"원료목록 확정가(원료목록 입력일 {originalDate})",
                });
            }
            else
            {
                Report.Warn($"원료 {m.Code} {name}: 원료목록에 단가 없음");
            }
        }
        return byName;
    }

    private readonly List<MaterialPrice> _pendingListPrices = [];

    private void ImportPriceHistory(SqliteConnection c, SqliteTransaction tx, ExcelWorksheet? ws, Dictionary<string, Material> byName)
    {
        if (ws is not null)
        {
            var rows = ws.Dimension?.End.Row ?? 0;
            var listPriceByMaterial = _pendingListPrices.ToDictionary(p => p.MaterialId);
            for (var r = 5; r <= rows; r++)
            {
                var stdName = Text(ws, r, 1);
                if (stdName.Length == 0 || Num(ws, r, 3) is not { } price) continue;
                var original = Text(ws, r, 2);
                var m = Find(byName, stdName) ?? Find(byName, original);
                if (m is null)
                {
                    Report.Warn($"단가이력 {r}행 '{stdName}': 원료목록에 없는 이름이라 건너뜀");
                    continue;
                }

                var source = Text(ws, r, 5);
                var rawDate = Text(ws, r, 4);
                var date = DateText(ws.Cells[r, 4].Value) ?? string.Empty; // 날짜 미상 = 빈 문자열(가장 오래된 것으로 정렬됨)
                // "2026-06"처럼 월까지만 적힌 기록은 1일로 해석된다 → 원래 표기를 메모에 남긴다.
                if (rawDate.Length is > 0 and < 10) source = $"{source} (원본 날짜 표기 '{rawDate}')";
                // 원료목록 확정가(작성일)보다 늦은 날짜의 이력은 현재 단가를 덮지 않도록 작성일로 맞춘다(순서는 Id로 원료목록이 뒤).
                if (string.CompareOrdinal(date, RecipeWorkbookDate) > 0)
                {
                    source = $"{source} (원본 날짜 {date} — 신제조법 작성일 이후라 작성일로 기록)";
                    date = RecipeWorkbookDate;
                }
                var incl = Vat.FromExclusive(price);
                // '사용자 확정' 행은 원료목록 확정가와 같은 값이라 중복 기록하지 않는다.
                if (listPriceByMaterial.TryGetValue(m.Id, out var lp) && Math.Abs(lp.PricePerKg - incl) < 0.001 && source.Contains("확정", StringComparison.Ordinal))
                {
                    continue;
                }

                _materials.AddPrice(c, new MaterialPrice
                {
                    MaterialId = m.Id, PricePerKg = incl, EffectiveDate = date, Source = "엑셀이관(단가이력)",
                    Note = Join($"출처: {source}", Text(ws, r, 6)),
                }, tx);
                Report.PriceHistory++;

                if (original.Length > 0 && MaterialRepository.Normalize(original) != MaterialRepository.Normalize(m.Name) &&
                    !m.AliasList.Any(a => MaterialRepository.Normalize(a) == MaterialRepository.Normalize(original)))
                {
                    m.Aliases = string.IsNullOrEmpty(m.Aliases) ? original : m.Aliases + "\n" + original;
                    _materials.Save(c, m, tx);
                    byName.TryAdd(MaterialRepository.Normalize(original), m);
                }
            }
        }

        foreach (var p in _pendingListPrices)
        {
            _materials.AddPrice(c, p, tx);
        }
        _pendingListPrices.Clear();
    }

    private void ImportMixtures(SqliteConnection c, SqliteTransaction tx, ExcelWorksheet? ws, Dictionary<string, Material> byName)
    {
        if (ws is null) return;
        var rows = ws.Dimension?.End.Row ?? 0;
        Material? current = null;
        var comps = new List<MaterialComponent>();
        void Flush()
        {
            if (current is not null && comps.Count > 0)
            {
                _materials.ReplaceComponents(c, current.Id, comps, tx);
                if (!current.IsMixture) { current.IsMixture = true; _materials.Save(c, current, tx); }
                Report.Mixtures++;
            }
            comps = [];
        }

        for (var r = 5; r <= rows; r++)
        {
            var head = Text(ws, r, 1);
            if (head.Length > 0)
            {
                Flush();
                current = Find(byName, head);
                if (current is null) Report.Warn($"혼합물구성 '{head}': 원료목록에 없는 이름이라 건너뜀");
            }
            var comp = Text(ws, r, 2);
            if (current is null || comp.Length == 0) continue;
            comps.Add(new MaterialComponent { ComponentName = comp, Cas = Text(ws, r, 3), Ratio = Num(ws, r, 4) ?? 0 });
        }
        Flush();
    }

    // ---------------- 제조법 ----------------

    [GeneratedRegex(@"^([CSHD]\d\d)_")]
    private static partial Regex RecipeSheetRegex();

    private void ImportRecipe(SqliteConnection c, SqliteTransaction tx, ExcelWorksheet ws, Dictionary<string, Material> byName)
    {
        var code = RecipeSheetRegex().Match(ws.Name).Groups[1].Value;
        var rows = ws.Dimension?.End.Row ?? 0;
        var totalRow = FindLabelRow(ws, 11, rows, "합계") ?? throw new InvalidDataException($"{ws.Name}: '합계' 행을 찾지 못했습니다.");

        var memo = new StringBuilder();
        for (var r = 4; r <= 8; r++)
        {
            var t = Text(ws, r, 9);
            if (t.Length > 0) memo.AppendLine(t);
        }

        var recipe = new Recipe
        {
            Code = MfgCodes.Prefix + code.ToLowerInvariant(), Name = Text(ws, 3, 3), SourceSheet = Text(ws, 4, 3), Version = "", Status = RecipeStatus.Draft,
            BatchKg = Num(ws, 5, 3) ?? 10,
            // 적용 비중 칸(C6)이 수식(=C7, 계산 비중)이 아니라 숫자로 직접 입력되어 있으면 실측 비중으로 본다.
            MeasuredDensity = string.IsNullOrEmpty(ws.Cells[6, 3].Formula) ? Num(ws, 6, 3) : null,
        };

        for (var r = 11; r < totalRow; r++)
        {
            var matName = Text(ws, r, 3);
            if (matName.Length == 0) continue;
            var m = Find(byName, matName);
            if (m is null)
            {
                m = new Material { Name = matName, Note = $"배합 시트({ws.Name})에서 자동 생성 — 원료목록에 없던 원료" };
                _materials.Save(c, m, tx);
                byName[MaterialRepository.Normalize(matName)] = m;
                Report.Warn($"{code}: 원료 '{matName}'이 원료목록에 없어 새로 만듦({m.Code}, 단가 없음)");
            }

            var lineMemo = Text(ws, r, 14);
            // 시트에서 단가를 수식(원료목록 참조) 대신 숫자로 직접 넣은 행은 원료목록 단가와 다를 수 있다 → 메모로 남긴다.
            if (string.IsNullOrEmpty(ws.Cells[r, 6].Formula) && Num(ws, r, 6) is { } sheetPrice)
            {
                lineMemo = Join(lineMemo, $"원본 시트 단가 직접입력 {Fmt.Won(sheetPrice)}원/kg(VAT별도) — 원료목록 단가 대신 쓰였음");
                // 이 단가를 원료 단가로 기록하지는 않는다: 같은 원료를 다른 시트(예: S06)는 단가 없이 계산하고 있어,
                // 기록하면 그 제조법들의 원가가 엑셀과 달라진다. 원료 탭에서 실제 단가를 확인해 입력하도록 경고만 남긴다.
                Report.Warn($"{code}: '{matName}' 시트에 단가 {Fmt.Won(sheetPrice)}원 직접입력 — 원료 단가로 옮기지 않음(원료 탭에서 실제 단가 확인·입력 필요)");
            }
            recipe.Lines.Add(new RecipeLine { Stage = Text(ws, r, 2), MaterialId = m.Id, Percent = Num(ws, r, 5) ?? 0, Memo = lineMemo });
        }

        var volRow = FindLabelRow(ws, totalRow, rows, "용량(ml)");
        if (volRow is not null)
        {
            var labelRows = new Dictionary<string, int>();
            for (var r = volRow.Value; r <= rows; r++)
            {
                var label = Text(ws, r, 3);
                if (SizeCostCategory.All.Contains(label)) labelRows[label] = r;
            }

            for (var col = 4; col <= 7; col++)
            {
                if (Num(ws, volRow.Value, col) is not { } ml || ml <= 0) continue;
                var size = new RecipeSize { VolumeMl = ml };
                foreach (var (label, r) in labelRows)
                {
                    var amount = Num(ws, r, col);
                    var note = Text(ws, r, 8);
                    if (amount is null) continue;
                    size.Costs.Add(new RecipeSizeCost { Category = label, ManualAmount = Vat.FromExclusive(amount.Value), Memo = note });
                }
                recipe.Sizes.Add(size);
            }
        }

        var sheetMemo = memo.ToString().Trim();
        recipe.ProcessMemo = sheetMemo;
        _recipes.Save(c, recipe, tx);
        Report.Recipes++;

        // 검증: 이관 후 계산한 kg당 원료비(VAT별도)가 시트 F4와 같은지.
        var sheetKg = Num(ws, 4, 6);
        if (sheetKg is not null)
        {
            Report.RecipeChecks.Add((code, recipe.Name, sheetKg.Value, recipe.Id));
        }
    }

    private static int? FindLabelRow(ExcelWorksheet ws, int from, int to, string label)
    {
        for (var r = from; r <= to; r++)
        {
            if (Text(ws, r, 3) == label) return r;
        }
        return null;
    }

    // ---------------- product_master ----------------

    private void ImportProductMaster(SqliteConnection c, SqliteTransaction tx, ExcelWorksheet ws, IReadOnlyList<Supplier> suppliers)
    {
        var rows = ws.Dimension?.End.Row ?? 0;
        var usedSkus = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;
        for (var r = 4; r <= rows; r++)
        {
            var name = Text(ws, r, 4);
            if (name.Length == 0) continue;
            var category = Text(ws, r, 2);
            var group = Text(ws, r, 3);
            var skuText = Text(ws, r, 5);
            double V(int col) => Num(ws, r, col) ?? 0;

            var notes = new List<string>();
            for (var col = 17; col <= 20; col++)
            {
                var t = Text(ws, r, col);
                if (t.Length > 0) notes.Add($"비고{col - 16}: {t}");
            }
            for (var col = 11; col <= 16; col++)
            {
                if (ws.Cells[r, col].Value is string s && s.Trim().Length > 0 && Num(ws, r, col) is null) notes.Add($"{ColumnTitle(col)} 칸 메모: {s.Trim()}");
            }

            // SKU 칸에 한글/공백이 섞인 메모가 들어간 행이 있다(예: "원가 다시 계산해야함") → SKU 아님.
            string? sku = null;
            if (skuText.Length > 0)
            {
                if (skuText.Any(ch => ch >= '가' && ch <= '힣') || skuText.Contains(' '))
                {
                    notes.Add($"SKU 칸 메모: {skuText}");
                }
                else if (!usedSkus.Add(skuText))
                {
                    notes.Add($"SKU '{skuText}' 중복 — 앞 행에 이미 있어 이 행은 SKU 없이 가져옴");
                    Report.Warn($"product_master {r}행: SKU '{skuText}' 중복");
                }
                else
                {
                    sku = skuText;
                }
            }

            // 기타(부자재) 중 SKU 없는 행 = 부자재 단가표 → PackMaterial.
            if (category == "기타(부자재)" && sku is null)
            {
                ImportPackRow(c, tx, ws, r, name, V(10), notes, suppliers);
                continue;
            }

            var p = new FinishedProduct
            {
                Sku = sku, Name = name, Category = category, ProductGroup = group, ConsumerPrice = Num(ws, r, 6),
                SourceType = ProductSourceType.Manual,
                ManualRaw = Vat.FromExclusive(V(11)), ContainerCost = Vat.FromExclusive(V(12)), PackageCost = Vat.FromExclusive(V(13)),
                StickerCost = Vat.FromExclusive(V(14)), EtcCost = Vat.FromExclusive(V(15)), ShippingCost = Vat.FromExclusive(V(16)),
                Notes = string.Join("\n", notes), SuggestedLink = MappingHints.For(sku, name), Status = RecipeStatus.Draft, SortOrder = order++,
            };
            _products.Save(c, p, tx);
            Report.Products++;
        }
    }

    private void ImportPackRow(SqliteConnection c, SqliteTransaction tx, ExcelWorksheet ws, int r, string name, double totalExcl, List<string> notes, IReadOnlyList<Supplier> suppliers)
    {
        var parts = new List<string>();
        foreach (var col in new[] { 11, 12, 13, 14, 15, 16 })
        {
            if (Num(ws, r, col) is { } v && v != 0) parts.Add($"{ColumnTitle(col)} {Fmt.Won(v)}");
        }
        if (parts.Count > 1) notes.Insert(0, "PM 구성(VAT별도): " + string.Join(", ", parts));

        var supplier = GuessSupplier(name, suppliers);
        var pack = new PackMaterial
        {
            Name = name, Category = GuessPackCategory(name), DefaultSupplierCode = supplier?.ChannelCode,
            Note = Join("product_master에서 이관", string.Join("\n", notes)),
        };
        _packs.Save(c, pack, tx);
        _packs.AddPrice(c, new PackMaterialPrice
        {
            PackMaterialId = pack.Id, SupplierCode = supplier?.ChannelCode, UnitPrice = Vat.FromExclusive(totalExcl),
            EffectiveDate = RecipeWorkbookDate, Source = "엑셀이관(product_master)", Note = "PM 제조원가(VAT별도) 합계 기준, 입력일 미상",
        }, tx);
        Report.Packs++;
        if (supplier is null) Report.PacksWithoutSupplier.Add(name);
    }

    /// <summary>
    /// 부자재명 → 매입처 추정. PM 품명은 "동방, 500ml 시럽+뚜껑"처럼 쉼표 앞이 거래처 약칭이거나 "(서림)"처럼 괄호로
    /// 붙어 있다. 약칭을 포함하는 매입처가 정확히 하나일 때만 연결한다(애매하면 비워 둠).
    /// </summary>
    public static Supplier? GuessSupplier(string name, IReadOnlyList<Supplier> suppliers)
    {
        var keys = new List<string>();
        var comma = name.IndexOf(',');
        if (comma > 0) keys.Add(name[..comma].Trim());
        foreach (Match m in Regex.Matches(name, @"\(([^)]+)\)")) keys.Add(m.Groups[1].Value.Trim());

        foreach (var key in keys.Where(k => k.Length >= 2))
        {
            var hits = suppliers.Where(s => s.Name.Replace("(주)", "").Contains(key, StringComparison.Ordinal)).ToList();
            if (hits.Count == 1) return hits[0];
        }
        return null;
    }

    public static string GuessPackCategory(string name)
    {
        if (name.Contains("박스")) return SizeCostCategory.Box;
        if (name.Contains("스티커") || name.Contains("라벨")) return SizeCostCategory.Label;
        var isBottle = name.Contains("병") || name.Contains("용기") || name.Contains("말통") || Regex.IsMatch(name, @"\d+\s*(ml|L)\b", RegexOptions.IgnoreCase);
        if (!isBottle && (name.Contains("뚜껑") || name.Contains("캡") || name.Contains("펌프") || name.Contains("스포이드"))) return SizeCostCategory.Cap;
        return isBottle ? SizeCostCategory.Container : SizeCostCategory.EtcPack;
    }

    private static string ColumnTitle(int col) => col switch
    {
        11 => "원료", 12 => "용기", 13 => "패키지", 14 => "스티커 등", 15 => "기타", 16 => "택배비", _ => $"{col}열",
    };

    // ---------------- 셀 읽기 ----------------

    private static Material? Find(Dictionary<string, Material> byName, string name) =>
        name.Length == 0 ? null : byName.GetValueOrDefault(MaterialRepository.Normalize(name));

    private static string Text(ExcelWorksheet ws, int r, int c)
    {
        var v = ws.Cells[r, c].Value;
        return v switch
        {
            null => string.Empty,
            double d => d.ToString(CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
        };
    }

    private static double? Num(ExcelWorksheet ws, int r, int c)
    {
        var v = ws.Cells[r, c].Value;
        return v switch
        {
            double d => d,
            int i => i,
            long l => l,
            decimal m => (double)m,
            string s when Fmt.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? DateText(object? v) => v switch
    {
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double d => DateTime.FromOADate(d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => null,
    };

    private static string Join(string a, string b) =>
        string.IsNullOrWhiteSpace(a) ? b : string.IsNullOrWhiteSpace(b) ? a : a + "\n" + b;
}

public class ImportReport
{
    public int Materials { get; set; }
    public int PriceHistory { get; set; }
    public int Mixtures { get; set; }
    public int Recipes { get; set; }
    public int Packs { get; set; }
    public int Products { get; set; }
    public List<string> PacksWithoutSupplier { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>(제조법 코드, 이름, 시트 kg당 원료비[VAT별도], RecipeId) — 이관 후 계산값과 대조.</summary>
    public List<(string Code, string Name, double SheetKgCost, long RecipeId)> RecipeChecks { get; } = [];

    public void Warn(string message) => Warnings.Add(message);
}
