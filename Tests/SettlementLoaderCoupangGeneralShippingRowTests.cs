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
/// 실제 사고 재현: 쿠팡일반 정산파일의 "배송비 전용 행"은 상품명/옵션명 열이 모두 비어 있고
/// (마커 텍스트가 매핑 안 된 옵션ID 열에만 있음), 매출액 열에만 금액이 들어있다. SettlementLoader가
/// "상품명+옵션명이 둘 다 비면 서식용 빈 행"으로 보고 건너뛰어버리면 이 행의 데이터가 통째로
/// 유실되어 배송비가 항상 0으로 계산된다.
/// </summary>
[TestClass]
public class SettlementLoaderCoupangGeneralShippingRowTests
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
    public async Task LoadFromFileAsync_KeepsBlankNameRowWithRevenue_AndFoldsItIntoShipping()
    {
        new ItemRepository().Upsert(new ItemModel { Sku = "SKU1", ItemName = "상품A", CostPrice = 1000m });
        var mappingRepository = new MappingRepository();
        mappingRepository.SaveRules(MappingRuleType.Exact, "COUPANGGENERAL", [new MappingRule { Key = "상품A옵션A", TargetSku = "SKU1" }]);

        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Order Detail Report");
            sheet.Cells[1, 1].Value = "상품명";
            sheet.Cells[1, 2].Value = "옵션ID";
            sheet.Cells[1, 3].Value = "옵션명";
            sheet.Cells[1, 4].Value = "판매수량";
            sheet.Cells[1, 5].Value = "매출액";
            sheet.Cells[1, 6].Value = "정산금액";

            // 실제 상품 행
            sheet.Cells[2, 1].Value = "상품A";
            sheet.Cells[2, 2].Value = "OPT1";
            sheet.Cells[2, 3].Value = "옵션A";
            sheet.Cells[2, 4].Value = 1;
            sheet.Cells[2, 5].Value = 10900;
            sheet.Cells[2, 6].Value = 9965;

            // 배송비 전용 행 — 상품명/옵션명은 비어 있고, 마커는 매핑 안 된 옵션ID 열에만 있음
            sheet.Cells[3, 1].Value = null;
            sheet.Cells[3, 2].Value = "<기본배송료>";
            sheet.Cells[3, 3].Value = null;
            sheet.Cells[3, 4].Value = 0;
            sheet.Cells[3, 5].Value = 3000;
            sheet.Cells[3, 6].Value = 2901;

            // 완전히 빈 추가배송 행(모든 값이 0) — 이건 실질적으로 아무 영향 없어야 함
            sheet.Cells[4, 1].Value = null;
            sheet.Cells[4, 2].Value = "<추가배송료>";
            sheet.Cells[4, 3].Value = null;
            sheet.Cells[4, 4].Value = 0;
            sheet.Cells[4, 5].Value = 0;
            sheet.Cells[4, 6].Value = 0;

            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "COUPANGGENERAL",
            ChannelName = "쿠팡일반",
            ChannelType = ChannelType.CoupangGeneral,
            SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { SheetName = "Order Detail Report", HeaderRow = 1, Column = "상품명" },
                [StdField.OptionName] = new FieldMapping { SheetName = "Order Detail Report", HeaderRow = 1, Column = "옵션명" },
                [StdField.Quantity] = new FieldMapping { SheetName = "Order Detail Report", HeaderRow = 1, Column = "판매수량" },
                [StdField.Revenue] = new FieldMapping { SheetName = "Order Detail Report", HeaderRow = 1, Column = "매출액" },
                [StdField.SettlementAmount] = new FieldMapping { SheetName = "Order Detail Report", HeaderRow = 1, Column = "정산금액" },
            },
        };

        var skuMapper = new SkuMapper(mappingRepository, "COUPANGGENERAL");
        var loader = new SettlementLoader();

        var rows = await loader.LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath);

        Assert.HasCount(1, rows);
        Assert.AreEqual("SKU1", rows[0].Msku);
        Assert.AreEqual(3000m, rows[0].Shipping);
        Assert.AreEqual(10900m, rows[0].Revenue); // 배송비 전용 행의 매출액(3000)은 합계에서 빠져야 함
    }
}
