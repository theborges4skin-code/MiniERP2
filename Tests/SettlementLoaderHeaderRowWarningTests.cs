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
public class SettlementLoaderHeaderRowWarningTests
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
    public async Task LoadFromFileAsync_WhenConfiguredHeaderRowIsBlank_SetsLastLoadHeaderRowLooksEmpty()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var main = package.Workbook.Worksheets.Add("메인");
            // 실제 헤더는 3행에 있지만, 채널설정은 1행(빈 행)으로 잘못 지정된 상황을 흉내낸다.
            main.Cells[3, 1].Value = "상품명";
            main.Cells[3, 2].Value = "정산액";
            main.Cells[4, 1].Value = "상품A";
            main.Cells[4, 2].Value = 10000;
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "TESTCH",
            ChannelName = "테스트채널",
            SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "상품명" },
                [StdField.SettlementAmount] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "정산액" },
            },
        };

        var skuMapper = new SkuMapper(new MappingRepository(), "TESTCH");
        var loader = new SettlementLoader();
        var rows = await loader.LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath);

        Assert.IsTrue(loader.LastLoadHeaderRowLooksEmpty);
        Assert.HasCount(0, rows); // 헤더를 못 찾았으니 상품명/옵션명이 모두 비어 행 자체가 스킵됨
    }

    [TestMethod]
    public async Task LoadFromFileAsync_WhenHeaderRowIsCorrect_DoesNotSetWarningFlag()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var main = package.Workbook.Worksheets.Add("메인");
            main.Cells[1, 1].Value = "상품명";
            main.Cells[1, 2].Value = "정산액";
            main.Cells[2, 1].Value = "상품A";
            main.Cells[2, 2].Value = 10000;
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "TESTCH",
            ChannelName = "테스트채널",
            SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "상품명" },
                [StdField.SettlementAmount] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "정산액" },
            },
        };

        var skuMapper = new SkuMapper(new MappingRepository(), "TESTCH");
        var loader = new SettlementLoader();
        await loader.LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath);

        Assert.IsFalse(loader.LastLoadHeaderRowLooksEmpty);
    }
}
