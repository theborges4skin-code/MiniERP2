using MiniERP2.Exporters;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerConsolidationExporterTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"PartnerConsolidationExporterTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [TestMethod]
    public void Export_WritesAllSixSheets()
    {
        PartnerConsolidationExporter.Export([], [], [], [], [], _ => 3000m, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheetNames = package.Workbook.Worksheets.Select(s => s.Name).ToList();

        CollectionAssert.Contains(sheetNames, "거래처요약");
        CollectionAssert.Contains(sheetNames, "CSKU상세");
        CollectionAssert.Contains(sheetNames, "채널별배송건수");
        CollectionAssert.Contains(sheetNames, "단가미배정");
        CollectionAssert.Contains(sheetNames, "미매핑·제외");
        CollectionAssert.Contains(sheetNames, "_META");
    }

    [TestMethod]
    public void Export_CompanySummarySheet_HasTotalsRow()
    {
        var summaries = new List<PartnerConsolidationCompanySummary>
        {
            new() { CompanyName = "펩투나", ChannelCount = 2, TotalQuantity = 10, TotalSupplyRevenue = 50000m, TotalSupplyProfit = 20000m, ShipmentCount = 5, ShippingFeeTotal = 15000m, UnassignedPriceCount = 1 },
            new() { CompanyName = "한결", ChannelCount = 1, TotalQuantity = 4, TotalSupplyRevenue = 8000m, TotalSupplyProfit = 3000m, ShipmentCount = 2, ShippingFeeTotal = 6000m, UnassignedPriceCount = 0 },
        };

        PartnerConsolidationExporter.Export(summaries, [], [], [], [], _ => 3000m, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["거래처요약"]!;

        Assert.AreEqual("펩투나", sheet.Cells[2, 1].Text);
        Assert.AreEqual("한결", sheet.Cells[3, 1].Text);
        Assert.AreEqual("합계", sheet.Cells[4, 1].Text);
        Assert.AreEqual(14d, sheet.Cells[4, 3].GetValue<double>());
        Assert.AreEqual(58000d, sheet.Cells[4, 4].GetValue<double>());
        Assert.AreEqual(23000d, sheet.Cells[4, 5].GetValue<double>());
        Assert.AreEqual(7d, sheet.Cells[4, 6].GetValue<double>());
        Assert.AreEqual(21000d, sheet.Cells[4, 7].GetValue<double>());
        Assert.AreEqual(1d, sheet.Cells[4, 8].GetValue<double>());
    }

    [TestMethod]
    public void Export_ChannelShipmentSheet_ComputesBillingAmountUsingResolver()
    {
        var shipments = new List<PartnerConsolidationChannelShipment>
        {
            new() { CompanyName = "펩투나", ChannelCode = "CH1", ChannelName = "쿠팡일반", ShipmentCount = 10, IsEstimated = false, ShippingTotal = 12000m },
        };

        PartnerConsolidationExporter.Export([], [], shipments, [], [], company => company == "펩투나" ? 3000m : 0m, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["채널별배송건수"]!;

        Assert.AreEqual("쿠팡일반", sheet.Cells[2, 2].Text);
        Assert.AreEqual(10d, sheet.Cells[2, 3].GetValue<double>());
        Assert.AreEqual("송장 기준", sheet.Cells[2, 4].Text);
        Assert.AreEqual(30000d, sheet.Cells[2, 6].GetValue<double>());
    }

    [TestMethod]
    public void Export_UnassignedSheet_OnlyIncludesUnassignedPriceRows()
    {
        var details = new List<PartnerConsolidationCskuDetail>
        {
            new() { CompanyName = "펩투나", CskuCode = "A", Msku = "MSKU-A", PriceSource = SupplyPriceSource.Own, SupplyPrice = 1000m },
            new() { CompanyName = "펩투나", CskuCode = "B", Msku = "MSKU-B", PriceSource = SupplyPriceSource.Unassigned },
        };

        PartnerConsolidationExporter.Export([], details, [], [], [], _ => 3000m, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["단가미배정"]!;

        Assert.AreEqual("B", sheet.Cells[2, 2].Text);
        Assert.AreEqual("", sheet.Cells[3, 2].Text); // 2번째 데이터 행은 없어야 한다.
    }

    [TestMethod]
    public void Export_MetaSheet_HasPartnerRollupSourceTypeAndFileList()
    {
        var files = new List<PartnerConsolidationFile>
        {
            new() { FilePath = "a.xlsx" },
            new() { FilePath = "b.xlsx" },
        };

        PartnerConsolidationExporter.Export([], [], [], [], files, _ => 3000m, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["_META"]!;

        // FileMeta.ChannelCode가 공란이라 MetaSheetHelper.TryRead(channel_code 필수)로는 못 읽으므로
        // 셀을 직접 확인한다 — 이 _META는 여러 채널을 아우르는 최종 산출물이라 애초에 재입력용이 아니다.
        Assert.AreEqual("source_type", sheet.Cells[2, 1].Text);
        Assert.AreEqual("partner_rollup", sheet.Cells[2, 2].Text);
        Assert.AreEqual("source_files", sheet.Cells[8, 1].Text);
        StringAssert.Contains(sheet.Cells[8, 2].Text, "a.xlsx");
        StringAssert.Contains(sheet.Cells[8, 2].Text, "b.xlsx");
    }
}
