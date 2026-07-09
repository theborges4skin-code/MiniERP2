using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class ExportSummaryLoaderTests
{
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        ExcelLicense.Ensure();
        _testFolder = Path.Combine(Path.GetTempPath(), "ExportSummaryTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_testFolder, recursive: true);
    }

    private string CreateDeclarationExcel(params (string Date, string Currency, decimal Amount)[] rows)
    {
        var path = Path.Combine(_testFolder, "declaration.xlsx");
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("Sheet1");
        ws.Cells[1, 1].Value = "신고일자";
        ws.Cells[1, 2].Value = "결제금액";
        ws.Cells[1, 3].Value = "통화코드";
        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cells[i + 2, 1].Value = rows[i].Date;
            ws.Cells[i + 2, 2].Value = (double)rows[i].Amount;
            ws.Cells[i + 2, 3].Value = rows[i].Currency;
        }
        package.SaveAs(new FileInfo(path));
        return path;
    }

    [TestMethod]
    public void LoadDeclarationFile_UsdRows_CapturedAsUsdUnmatchedEntry()
    {
        var config = new DeclarationTrackConfig
        {
            HeaderRow = 1,
            DateColumn = "신고일자",
            AmountColumn = "결제금액",
            CurrencyColumn = "통화코드",
            CurrencyToMarket = new Dictionary<string, string>
            {
                ["SGD"] = "SG",
                ["USD"] = "USD_UNMATCHED",
            },
        };

        var path = CreateDeclarationExcel(
            ("2026-01-10", "SGD", 100m),
            ("2026-01-15", "USD", 200m),
            ("2026-01-20", "USD", 300m));

        var result = ExportSummaryLoader.LoadDeclarationFile(path, config);

        // SGD → SG (정상 매핑)
        Assert.HasCount(3, result.Entries);
        Assert.AreEqual("SG", result.Entries[0].MarketCode);

        // USD → USD_UNMATCHED (정상 매핑이지만 상세도 함께 보존)
        Assert.AreEqual("USD_UNMATCHED", result.Entries[1].MarketCode);
        Assert.AreEqual(0, result.UnmatchedRowCount);

        // UsdRawEntries에 2건 보존
        Assert.IsNotNull(result.UsdRawEntries);
        Assert.HasCount(2, result.UsdRawEntries!);
        Assert.AreEqual("USD", result.UsdRawEntries[0].Currency);
        Assert.AreEqual(200m, result.UsdRawEntries[0].Amount);
        Assert.AreEqual(300m, result.UsdRawEntries[1].Amount);
    }

    [TestMethod]
    public void LoadDeclarationFile_NonUsdUnmatched_CountedAsUnmatched()
    {
        var config = new DeclarationTrackConfig
        {
            HeaderRow = 1,
            DateColumn = "신고일자",
            AmountColumn = "결제금액",
            CurrencyColumn = "통화코드",
            CurrencyToMarket = new Dictionary<string, string> { ["SGD"] = "SG" },
        };

        var path = CreateDeclarationExcel(
            ("2026-01-10", "SGD", 100m),
            ("2026-01-15", "XYZ", 50m)); // 알 수 없는 통화

        var result = ExportSummaryLoader.LoadDeclarationFile(path, config);

        Assert.HasCount(1, result.Entries);
        Assert.AreEqual(1, result.UnmatchedRowCount);
        // 미매핑(XYZ)은 UsdRawEntries에 포함 — 통화 미분리 상세로 취급
        Assert.IsNotNull(result.UsdRawEntries);
        Assert.HasCount(1, result.UsdRawEntries!);
        Assert.AreEqual("XYZ", result.UsdRawEntries[0].Currency);
    }

    [TestMethod]
    public void SalesMarketMapping_FileNamePatterns_MatchesCaseInsensitive()
    {
        var mapping = new SalesMarketMapping
        {
            MarketCode = "SG",
            FileNamePatterns = new List<string> { "SG_", "_SG_", "singapore" },
        };

        Assert.IsTrue(mapping.FileNamePatterns.Any(p => "SG_20260601".Contains(p, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(mapping.FileNamePatterns.Any(p => "shopee_SG_orders".Contains(p, StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(mapping.FileNamePatterns.Any(p => "Singapore_sales".Contains(p, StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(mapping.FileNamePatterns.Any(p => "MY_20260601".Contains(p, StringComparison.OrdinalIgnoreCase)));
    }
}
