using MiniERP2.DataLoaders;
using MiniERP2.Mapping;
using MiniERP2.Models;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class CfsFeeLoaderTests
{
    private static GrowthCfsFeeConfig DefaultCfg => new();

    // 인메모리 CFS 파일 생성 헬퍼
    private static ExcelPackage MakeCfsPackage(
        IEnumerable<(string OptionId, decimal HandlingRaw)> handlingRows,
        IEnumerable<(string OptionId, decimal ShippingRaw)> shippingRows)
    {
        var pkg = new ExcelPackage();
        var cfg = DefaultCfg;

        var whSheet = pkg.Workbook.Worksheets.Add(cfg.HandlingSheetName);
        whSheet.Cells[cfg.HandlingHeaderRow, 1].Value = cfg.CfsOptionIdHeader;
        whSheet.Cells[cfg.HandlingHeaderRow, 2].Value = cfg.HandlingFeeHeader;
        int r = cfg.HandlingHeaderRow + 1;
        foreach (var (id, val) in handlingRows)
        {
            whSheet.Cells[r, 1].Value = id;
            whSheet.Cells[r, 2].Value = val;
            r++;
        }

        var shipSheet = pkg.Workbook.Worksheets.Add(cfg.ShippingSheetName);
        shipSheet.Cells[cfg.ShippingHeaderRow, 1].Value = cfg.CfsOptionIdHeader;
        shipSheet.Cells[cfg.ShippingHeaderRow, 2].Value = cfg.ShippingFeeHeader;
        r = cfg.ShippingHeaderRow + 1;
        foreach (var (id, val) in shippingRows)
        {
            shipSheet.Cells[r, 1].Value = id;
            shipSheet.Cells[r, 2].Value = val;
            r++;
        }

        return pkg;
    }

    [TestMethod]
    public void IsCfsFile_DetectsBySheetsName()
    {
        var cfg = DefaultCfg;
        using var pkg = MakeCfsPackage([], []);
        Assert.IsTrue(CfsFeeLoader.IsCfsFile(pkg, cfg));
    }

    [TestMethod]
    public void IsCfsFile_ReturnsFalse_WhenNoMatchingSheets()
    {
        var cfg = DefaultCfg;
        var pkg = new ExcelPackage();
        pkg.Workbook.Worksheets.Add("주문내역");
        Assert.IsFalse(CfsFeeLoader.IsCfsFile(pkg, cfg));
    }

    [TestMethod]
    public void AccumulateSheet_MultipliesRawByVat()
    {
        // 옵션 A: 입출고비 원본 1000 → VAT포함 1100
        using var pkg = MakeCfsPackage(
            [("OPT-A", 1000m)],
            [("OPT-A", 2000m)]);

        // LoadAndMerge는 파일경로 기반이라 내부 헬퍼 직접 접근 불가.
        // 대신 결과를 임시 파일로 저장 후 로드하는 방식 사용.
        var tempPath = Path.GetTempFileName() + ".xlsx";
        try
        {
            pkg.SaveAs(new FileInfo(tempPath));
            var result = CfsFeeLoader.LoadAndMerge([tempPath], DefaultCfg);
            Assert.AreEqual(1100m, result.HandlingByOptionId["OPT-A"], 0.001m);
            Assert.AreEqual(2200m, result.ShippingByOptionId["OPT-A"], 0.001m);
        }
        finally { File.Delete(tempPath); }
    }

    [TestMethod]
    public void LoadAndMerge_SumsAcrossMultipleFiles()
    {
        // 2주차 파일: 같은 옵션 ID 합산
        using var pkg1 = MakeCfsPackage([("OPT-A", 1000m)], []);
        using var pkg2 = MakeCfsPackage([("OPT-A", 500m)], []);

        var t1 = Path.GetTempFileName() + ".xlsx";
        var t2 = Path.GetTempFileName() + ".xlsx";
        try
        {
            pkg1.SaveAs(new FileInfo(t1));
            pkg2.SaveAs(new FileInfo(t2));
            var result = CfsFeeLoader.LoadAndMerge([t1, t2], DefaultCfg);
            // (1000 + 500) * 1.1 = 1650
            Assert.AreEqual(1650m, result.HandlingByOptionId["OPT-A"], 0.001m);
            Assert.AreEqual(2, result.CfsFileCount);
        }
        finally { File.Delete(t1); File.Delete(t2); }
    }

    [TestMethod]
    public void ApplyCoupangGrowthCfsFees_AssignsToFirstOccurrenceOnly()
    {
        // 스펙 §4 시나리오: 옵션ID 91742514324가 4건 등장 → 최초(행 51)에만 배분
        var optId = "91742514324";
        var rows = new List<SettlementData>
        {
            MakeRow(optId, rowNum: 51),   // 최초 → 배분 대상
            MakeRow(optId, rowNum: 92),
            MakeRow(optId, rowNum: 198),
            MakeRow(optId, rowNum: 215),
        };

        var handling = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            { [optId] = 10_150m };
        var shipping = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            { [optId] = 16_800m };

        var affected = ProfitCalculator.ApplyCoupangGrowthCfsFees(
            ChannelType.CoupangGrowth, rows, "옵션ID", handling, shipping);

        Assert.AreEqual(1, affected.Count);
        Assert.AreEqual(10_150m, rows[0].Fee);
        Assert.AreEqual(16_800m, rows[0].Shipping);
        // 나머지 행은 0 유지
        Assert.AreEqual(0m, rows[1].Fee);
        Assert.AreEqual(0m, rows[2].Shipping);
    }

    [TestMethod]
    public void ApplyCoupangGrowthCfsFees_IgnoresNonGrowthChannel()
    {
        var rows = new List<SettlementData> { MakeRow("OPT-A") };
        var fees = new Dictionary<string, decimal> { ["OPT-A"] = 1000m };
        var affected = ProfitCalculator.ApplyCoupangGrowthCfsFees(
            ChannelType.CoupangGeneral, rows, "옵션ID", fees, []);
        Assert.AreEqual(0, affected.Count);
        Assert.AreEqual(0m, rows[0].Fee);
    }

    [TestMethod]
    public void Calculate_CoupangGrowth_CfsMode_DoesNotApplyVatRate()
    {
        // CFS 모드: 배송비·입출고비가 이미 VAT포함 → 그대로 차감
        // settlement=10000, cost=3000, qty=2, shipping=500(VAT포함), fee=100(VAT포함)
        // 기대 이익: 10000 - 3000*2 - 500 - 100 = 3400
        var profit = ProfitCalculator.Calculate(
            ChannelType.CoupangGrowth, settlement: 10000m,
            costPrice: 3000m, qty: 2, shipping: 500m, fee: 100m, cfsMode: true);
        Assert.AreEqual(3400m, profit);
    }

    [TestMethod]
    public void Calculate_CoupangGrowth_NonCfsMode_AppliesVatRate()
    {
        // 기존 모드(cfsMode=false): VAT별도 → × 1.1
        // 기대 이익: 10000 - 3000*2 - 500*1.1 - 100*1.1 = 10000 - 6000 - 550 - 110 = 3340
        var profit = ProfitCalculator.Calculate(
            ChannelType.CoupangGrowth, settlement: 10000m,
            costPrice: 3000m, qty: 2, shipping: 500m, fee: 100m, cfsMode: false);
        Assert.AreEqual(3340m, profit);
    }

    private static SettlementData MakeRow(string optionId, int rowNum = 0) => new()
    {
        RawValues = new Dictionary<string, string> { ["옵션ID"] = optionId },
        ChannelCode = "CH01",
    };
}
