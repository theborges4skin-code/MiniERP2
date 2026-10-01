using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

/// <summary>
/// 이공그로스수동마감(ManualGrowthClosing_Spec.md G1/G2). 시트 레이아웃은 샘플 '2609 제트' 시트의 구조
/// (Y-SEP 헤더, &lt;로켓그로스(제트)&gt; 구분행, 총계행)를 축소 재현한다.
/// </summary>
[TestClass]
public class ManualGrowthClosingEngineTests
{
    [TestMethod]
    [DataRow("2609 제트", true, "2026-09")]
    [DataRow("2608 그로스", true, "2026-08")]
    [DataRow("2612제트(2)", true, "2026-12")]
    [DataRow("2609 쿠팡", false, "")]
    [DataRow("제트 2609", false, "")]
    [DataRow("2613 제트", false, "")]
    [DataRow("26090 제트", false, "")]
    public void TryGetTargetPeriod_FiltersBySheetNameKeywordAndYymm(string sheetName, bool expected, string expectedPeriod)
    {
        Assert.AreEqual(expected, ManualGrowthClosingEngine.TryGetTargetPeriod(sheetName, out var period));
        Assert.AreEqual(expectedPeriod, period);
    }

    [TestMethod]
    public void FindTargetSheets_ReturnsOnlyTargets_LatestFirst()
    {
        var result = ManualGrowthClosingEngine.FindTargetSheets(new[] { "2608 제트", "2609 쿠팡", "2609 제트", "Sheet1", "2607 그로스" });

        CollectionAssert.AreEqual(new[] { "2609 제트", "2608 제트", "2607 그로스" }, result.Select(r => r.SheetName).ToArray());
        CollectionAssert.AreEqual(new[] { "2026-09", "2026-08", "2026-07" }, result.Select(r => r.Period).ToArray());
    }

    [TestMethod]
    public void NormalizeItemName_TrimsAndCollapsesSpacesOnly()
    {
        Assert.AreEqual("핸드워시 4L + 300ml 2개", ManualGrowthClosingEngine.NormalizeItemName("  핸드워시  4L +   300ml 2개 "));
        // 한 글자라도 다르면 다른 CSKU(사용자 확정) — 대소문자/기호는 건드리지 않는다.
        Assert.AreNotEqual(ManualGrowthClosingEngine.NormalizeItemName("요석제거제 1L"), ManualGrowthClosingEngine.NormalizeItemName("요석제거제 1l"));
    }

    private static ExcelWorksheet BuildJetSheet(ExcelPackage pkg, string name, int month)
    {
        var ws = pkg.Workbook.Worksheets.Add(name);
        ws.Cells[3, 17].Value = "공급받는자";
        ws.Cells[3, 18].Value = "등록번호"; ws.Cells[3, 21].Value = "269-88-01547";
        ws.Cells[5, 18].Value = "상   호"; ws.Cells[5, 21].Value = "주식회사 이공이공인터내셔널";

        ws.Cells[11, 1].Value = "연"; ws.Cells[11, 2].Value = "월"; ws.Cells[11, 3].Value = "일";
        ws.Cells[11, 4].Value = "품목"; ws.Cells[11, 13].Value = "규격"; ws.Cells[11, 15].Value = "수량";
        ws.Cells[11, 17].Value = "단가"; ws.Cells[11, 20].Value = "공급가액"; ws.Cells[11, 24].Value = "세액";
        ws.Cells[11, 27].Value = "비고";

        ws.Cells[12, 4].Value = "<로켓그로스(제트)>";

        void Line(int row, int day, string item, int qty, int unit)
        {
            ws.Cells[row, 1].Value = 26; ws.Cells[row, 2].Value = month; ws.Cells[row, 3].Value = day;
            ws.Cells[row, 4].Value = item; ws.Cells[row, 15].Value = qty; ws.Cells[row, 17].Value = unit;
            ws.Cells[row, 20].Value = qty * unit; ws.Cells[row, 24].Value = qty * unit / 10;
        }
        Line(13, 3, "요석제거제 4L", 100, 10450);
        Line(14, 3, "요석제거제 1L", 200, 3000);
        Line(15, 10, "요석제거제 500ml", 50, 2050);
        Line(16, 17, "핸드워시 4L + 300ml 2개", 10, 8500);
        Line(17, 24, "요석제거제 4L", 20, 10450);

        int qtySum = 100 + 200 + 50 + 10 + 20;
        int supplySum = 100 * 10450 + 200 * 3000 + 50 * 2050 + 10 * 8500 + 20 * 10450;
        ws.Cells[30, 1].Value = "총계"; ws.Cells[30, 15].Value = qtySum;
        ws.Cells[30, 20].Value = supplySum; ws.Cells[30, 24].Value = supplySum / 10;
        return ws;
    }

    [TestMethod]
    public void ParseSheet_JetLayout_ExcludesDividerRow_AndReconcilesTotals()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = BuildJetSheet(pkg, "2609 제트", 9);

        var result = ManualGrowthClosingEngine.ParseSheet(ws, "거래명세표_이공이공인터내셔널.xlsx", "2026-09");

        Assert.AreEqual(5, result.Lines.Count);
        Assert.AreEqual(1, result.ExcludedDividerCount);
        Assert.IsTrue(result.TotalsReconciled);
        Assert.AreEqual(380m, result.Lines.Sum(l => l.Qty));
        var first = result.Lines[0];
        Assert.AreEqual("2026-09-03", first.LineDate);
        Assert.AreEqual("요석제거제 4L", first.CskuCode);
        Assert.AreEqual(1045000m, first.SupplyAmount);
        Assert.AreEqual(104500m, first.Tax);
        Assert.AreEqual(1149500m, first.Revenue);
        Assert.IsFalse(result.Lines.Any(l => l.DateMismatch));
    }

    [TestMethod]
    public void ParseSheet_LineMonthDiffersFromSheetPeriod_FlagsWarningOnly()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = BuildJetSheet(pkg, "2609 제트", 8);

        var result = ManualGrowthClosingEngine.ParseSheet(ws, "f.xlsx", "2026-09");

        Assert.AreEqual(5, result.Lines.Count);
        Assert.IsTrue(result.Lines.All(l => l.DateMismatch));
        Assert.IsTrue(result.Lines.All(l => l.Period == "2026-09"));
    }

    private static ManualGrowthLine Line(string item, decimal qty, decimal unit, string period = "2026-09") => new()
    {
        Period = period,
        ItemName = item,
        CskuCode = ManualGrowthClosingEngine.NormalizeItemName(item),
        Qty = qty,
        UnitPrice = unit,
        SupplyAmount = qty * unit,
        Tax = qty * unit / 10,
    };

    [TestMethod]
    public void Resolve_UsesVatIncludedRevenue_CostOverrideFirst_AndFlagsUnassignedAndPriceMismatch()
    {
        var lines = new List<ManualGrowthLine>
        {
            Line("요석제거제 4L", 10, 10450),   // CSKU 개별원가 5000
            Line("요석제거제 1L", 10, 3000),    // 마스터 원가 1000, 납품가 불일치
            Line("핸드워시 4L + 300ml 2개", 1, 8500), // 미배정
        };
        var cskus = new Dictionary<string, ChannelSkuModel>
        {
            ["요석제거제 4L"] = new() { ChannelCode = "G", CskuCode = "요석제거제 4L", Msku = "M4L", SupplyPrice = 11495m, CostPriceOverride = 5000m },
            ["요석제거제 1L"] = new() { ChannelCode = "G", CskuCode = "요석제거제 1L", Msku = "M1L", SupplyPrice = 3000m },
        };
        var items = new Dictionary<string, ItemModel>
        {
            ["M4L"] = new() { Sku = "M4L", CostPrice = 7000m, ProductGroup = "요석제거제" },
            ["M1L"] = new() { Sku = "M1L", CostPrice = 1000m, ProductGroup = "요석제거제" },
        };

        ManualGrowthClosingEngine.Resolve(lines, c => cskus.GetValueOrDefault(c), s => items.GetValueOrDefault(s));

        Assert.AreEqual(5000m, lines[0].CostPrice);
        Assert.AreEqual(114950m - 50000m, lines[0].Profit);
        Assert.IsFalse(lines[0].PriceMismatch); // 10450×1.1 = 11495 = 납품가(VAT포함)
        Assert.AreEqual("정상", lines[0].StatusText);

        Assert.AreEqual(1000m, lines[1].CostPrice);
        Assert.AreEqual(33000m - 10000m, lines[1].Profit);
        Assert.IsTrue(lines[1].PriceMismatch);

        Assert.IsTrue(lines[2].IsUnassigned);
        Assert.AreEqual("미배정", lines[2].StatusText);
        Assert.AreEqual(0m, lines[2].CostPrice);
    }

    [TestMethod]
    public void BuildProfitFacts_GroupsByProductGroup_WithSettlementLabels()
    {
        var lines = new List<ManualGrowthLine>
        {
            new() { MasterSku = "A", ProductGroup = "요석제거제", Qty = 2, SupplyAmount = 1000, Tax = 100, Profit = 500 },
            new() { MasterSku = "B", ProductGroup = "요석제거제", Qty = 1, SupplyAmount = 500, Tax = 50, Profit = 200 },
            new() { MasterSku = "C", ProductGroup = "", Qty = 1, SupplyAmount = 100, Tax = 10, Profit = 10 },
            new() { MasterSku = "", Qty = 3, SupplyAmount = 300, Tax = 30, Profit = 330 },
        };

        var facts = ManualGrowthClosingEngine.BuildProfitFacts(lines).ToDictionary(f => f.ProductGroup);

        Assert.AreEqual(3, facts["요석제거제"].Qty);
        Assert.AreEqual(1650m, facts["요석제거제"].Revenue);
        Assert.AreEqual(700m, facts["요석제거제"].GrossProfit);
        Assert.IsTrue(facts.ContainsKey("(미지정)"));
        Assert.IsTrue(facts.ContainsKey("(미매핑)"));
    }

    [TestMethod]
    public void BuildClosings_MultipleSheets_OneClosingPerPeriod_SamePeriodSheetsMerged()
    {
        var sheets = new[]
        {
            new ManualGrowthSheetResult { SourceFileName = "f.xlsx", SheetName = "2609 제트", Period = "2026-09", Lines = { Line("A", 1, 100), Line("B", 2, 100) } },
            new ManualGrowthSheetResult { SourceFileName = "f.xlsx", SheetName = "2608 제트", Period = "2026-08", Lines = { Line("A", 5, 100, "2026-08") } },
            new ManualGrowthSheetResult { SourceFileName = "f.xlsx", SheetName = "2609 그로스", Period = "2026-09", Lines = { Line("C", 1, 100) } },
        };

        var closings = ManualGrowthClosingEngine.BuildClosings("GROWTH", sheets);

        Assert.AreEqual(2, closings.Count);
        Assert.AreEqual("2026-08", closings[0].Period);
        Assert.AreEqual(5m, closings[0].TotalQty);
        Assert.AreEqual("2026-09", closings[1].Period);
        Assert.AreEqual(3, closings[1].Lines.Count);
        Assert.AreEqual("2609 제트, 2609 그로스", closings[1].SourceSheetName);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, closings[1].Lines.Select(l => l.RowNo).ToArray());
        Assert.AreEqual(400m, closings[1].TotalSupply);
    }
}
