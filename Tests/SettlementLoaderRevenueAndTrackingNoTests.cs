using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

/// <summary>
/// 2026-06-28 사용자 요청: 정산서 매핑 표준필드에 매출액(Revenue)/실제발송송장수(TrackingNo)를
/// 새로 추가했다(쿠팡그로스 제외). SettlementLoader가 두 필드를 실제로 읽어오는지 검증한다.
/// </summary>
[TestClass]
public class SettlementLoaderRevenueAndTrackingNoTests
{
    private string _testFolder = string.Empty;
    private string _excelFilePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _excelFilePath = Path.Combine(_testFolder, "settlement.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public async Task LoadFromFileAsync_ReadsRevenueAndTrackingNo_AndResolvesProductGroup()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU1", ItemName = "상품A", CostPrice = 1000m, ProductGroup = "01.피마자" });
        var mappingRepository = new MappingRepository();
        mappingRepository.SaveRules(MappingRuleType.Exact, "CH1", [new MappingRule { Key = "상품A", TargetSku = "SKU1" }]);

        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("메인");
            sheet.Cells[1, 1].Value = "상품명";
            sheet.Cells[1, 2].Value = "매출액";
            sheet.Cells[1, 3].Value = "송장번호";
            sheet.Cells[2, 1].Value = "상품A";
            sheet.Cells[2, 2].Value = 12000;
            sheet.Cells[2, 3].Value = "T-001";
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "CH1",
            ChannelName = "테스트채널",
            SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "상품명" },
                [StdField.Revenue] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "매출액" },
                [StdField.TrackingNo] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "송장번호" },
            },
        };

        var skuMapper = new SkuMapper(mappingRepository, "CH1");
        var rows = await new SettlementLoader().LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath);

        Assert.HasCount(1, rows);
        Assert.AreEqual(12000m, rows[0].Revenue);
        Assert.AreEqual("T-001", rows[0].TrackingNo);
        Assert.AreEqual("01.피마자", rows[0].ProductGroup);
    }
}
