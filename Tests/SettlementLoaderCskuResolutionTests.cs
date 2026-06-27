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
/// CSKU 코드 도입 이후, 매핑 규칙의 TargetSku가 마스터SKU가 아니라 CSKU 코드인 경우에도
/// 원가 조회(이익계산)가 CSKU → 마스터SKU 변환을 거쳐 정확히 동작하는지 검증한다.
/// 이 변환이 빠지면 모든 CSKU 매핑 건이 "원가 정보 없음"으로 처리되어 이익계산이 깨진다.
/// </summary>
[TestClass]
public class SettlementLoaderCskuResolutionTests
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
    public async Task LoadFromFileAsync_TargetSkuIsCskuCode_ResolvesMasterSkuForCostLookup()
    {
        // 마스터SKU "PRODA"의 채널 옵션1이 CSKU 코드 "CPG_PRODA_1"로 등록되어 있다.
        new ItemRepository().Upsert(new ItemModel { Sku = "PRODA", ItemName = "상품A", CostPrice = 1000m });
        var channelSkuRepository = new ChannelSkuRepository();
        channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = "CH01", CskuCode = "CPG_PRODA_1", Msku = "PRODA", SupplyPrice = 1500m });

        var mappingRepository = new MappingRepository();
        mappingRepository.SaveRules(MappingRuleType.Exact, "CH01", [new MappingRule { Key = "상품A옵션1", TargetSku = "CPG_PRODA_1" }]);

        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var main = package.Workbook.Worksheets.Add("메인");
            main.Cells[1, 1].Value = "상품명";
            main.Cells[1, 2].Value = "옵션명";
            main.Cells[1, 3].Value = "수량";
            main.Cells[1, 4].Value = "정산액";
            main.Cells[2, 1].Value = "상품A";
            main.Cells[2, 2].Value = "옵션1";
            main.Cells[2, 3].Value = 1;
            main.Cells[2, 4].Value = 10000;
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "CH01",
            ChannelName = "테스트채널",
            ChannelType = ChannelType.General,
            SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "상품명" },
                [StdField.OptionName] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "옵션명" },
                [StdField.Quantity] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "수량" },
                [StdField.SettlementAmount] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "정산액" },
            },
        };

        var skuMapper = new SkuMapper(mappingRepository, "CH01", channelSkuRepository);
        var rows = await new SettlementLoader().LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath, channelSkuRepository: channelSkuRepository);

        Assert.HasCount(1, rows);
        Assert.AreEqual("CPG_PRODA_1", rows[0].Msku);
        Assert.AreNotEqual("원가 정보 없음", rows[0].Status);
        // 일반 공식: 10000 - 1000(마스터SKU 원가) * 1 = 9000
        Assert.AreEqual(9000m, rows[0].Profit);
    }
}
