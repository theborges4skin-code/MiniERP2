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
public class SettlementLoaderGrowthAuxJoinTests
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
    public async Task LoadFromFileAsync_JoinsShippingFeeFromAuxSheet_AndAppliesCoupangGrowthFormula()
    {
        // 마스터SKU/매핑 규칙 준비
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU1", ItemName = "상품A", CostPrice = 1000m });
        var mappingRepository = new MappingRepository();
        mappingRepository.SaveRules(MappingRuleType.Exact, "COUPANGGROWTH", [new MappingRule { Key = "상품A", TargetSku = "SKU1" }]);

        // 메인시트(상품명/옵션ID/수량/정산액) + 보조시트(배송비: 옵션ID/금액) 구성
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var main = package.Workbook.Worksheets.Add("메인");
            main.Cells[1, 1].Value = "상품명";
            main.Cells[1, 2].Value = "옵션ID";
            main.Cells[1, 3].Value = "수량";
            main.Cells[1, 4].Value = "정산액";
            main.Cells[2, 1].Value = "상품A";
            main.Cells[2, 2].Value = "OPT1";
            main.Cells[2, 3].Value = 2;
            main.Cells[2, 4].Value = 10000;

            var aux = package.Workbook.Worksheets.Add("배송비");
            aux.Cells[1, 1].Value = "옵션ID";
            aux.Cells[1, 2].Value = "금액";
            aux.Cells[2, 1].Value = "OPT1";
            aux.Cells[2, 2].Value = 500;

            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "COUPANGGROWTH",
            ChannelName = "쿠팡그로스",
            ChannelType = ChannelType.CoupangGrowth,
            SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "상품명" },
                [StdField.Quantity] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "수량" },
                [StdField.SettlementAmount] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "정산액" },
            },
            GrowthAuxSources =
            [
                new GrowthAuxSource
                {
                    Enabled = true,
                    TargetStdField = StdField.ShippingFee,
                    SheetName = "배송비",
                    HeaderRow = 1,
                    KeyHeader = "옵션ID",
                    ValueHeader = "금액",
                }
            ],
        };

        var skuMapper = new SkuMapper(mappingRepository, "COUPANGGROWTH");
        var loader = new SettlementLoader();

        var rows = await loader.LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath);

        Assert.HasCount(1, rows);
        Assert.AreEqual("SKU1", rows[0].Msku);
        Assert.AreEqual(500m, rows[0].Shipping); // 메인시트에는 배송비 컬럼이 없으므로 보조시트 JOIN으로만 채워져야 함

        // 쿠팡그로스 공식: 10000 - 1000*2 - (500*1.1) - (0*1.1) = 7450
        Assert.AreEqual(7450m, rows[0].Profit);
    }
}
