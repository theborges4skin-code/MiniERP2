using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class SettlementLoaderFixedValueTests
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
    public async Task LoadFromFileAsync_FixedValueOnShippingFee_IgnoresSheetAndUsesFixedValue()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU1", ItemName = "상품A", CostPrice = 1000m });
        var mappingRepository = new MappingRepository();
        mappingRepository.SaveRules(MappingRuleType.Exact, "PARTNER", [new MappingRule { Key = "상품A", TargetSku = "SKU1" }]);

        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var main = package.Workbook.Worksheets.Add("메인");
            main.Cells[1, 1].Value = "상품명";
            main.Cells[1, 2].Value = "수량";
            main.Cells[1, 3].Value = "정산액";
            main.Cells[2, 1].Value = "상품A";
            main.Cells[2, 2].Value = 1;
            main.Cells[2, 3].Value = 10000;
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "PARTNER",
            ChannelName = "고정거래처",
            ChannelType = ChannelType.Partner,
            SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "상품명" },
                [StdField.Quantity] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "수량" },
                [StdField.SettlementAmount] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "정산액" },
                [StdField.ShippingFee] = new FieldMapping { FixedValue = "0" },
                [StdField.HandlingFee] = new FieldMapping { FixedValue = "500" },
            },
        };

        var skuMapper = new SkuMapper(mappingRepository, "PARTNER");
        var rows = await new SettlementLoader().LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath);

        Assert.HasCount(1, rows);
        Assert.AreEqual(0m, rows[0].Shipping);
        Assert.AreEqual(500m, rows[0].Fee);
        // 일반 공식: 10000 - 1000*1 = 9000 (Partner는 기본 공식 사용)
        Assert.AreEqual(9000m, rows[0].Profit);
    }
}
