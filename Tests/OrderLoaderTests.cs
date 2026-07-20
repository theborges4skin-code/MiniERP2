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
/// 누적발주서 채널의 "최근 N일 이내" 선택창에 쓰일 발주일(StdField.OrderDate) 파싱을 검증한다.
/// 엑셀에서 날짜는 날짜서식 셀(EPPlus가 DateTime으로 인식)로 올 수도, 텍스트("2026-06-20")로
/// 입력될 수도 있어 둘 다 처리되어야 한다.
/// </summary>
[TestClass]
public class OrderLoaderTests
{
    private string _testFolder = string.Empty;
    private string _excelFilePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _excelFilePath = Path.Combine(_testFolder, "orders.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static ChannelConfig BuildChannelConfig() => new()
    {
        ChannelCode = "CH-A",
        ChannelName = "테스트채널",
        OrderFieldMappings = new Dictionary<StdField, FieldMapping>
        {
            [StdField.ProductNo] = new FieldMapping { HeaderRow = 1, Column = "주문번호" },
            [StdField.ProductName] = new FieldMapping { HeaderRow = 1, Column = "상품명" },
            [StdField.OrderDate] = new FieldMapping { HeaderRow = 1, Column = "발주일" },
        },
    };

    [TestMethod]
    public async Task LoadFromFileAsync_DateFormattedCell_ParsesOrderDate()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "주문번호";
            sheet.Cells[1, 2].Value = "상품명";
            sheet.Cells[1, 3].Value = "발주일";
            sheet.Cells[2, 1].Value = "ORDER-1";
            sheet.Cells[2, 2].Value = "상품A";
            sheet.Cells[2, 3].Value = new DateTime(2026, 6, 20);
            sheet.Cells[2, 3].Style.Numberformat.Format = "yyyy-mm-dd";
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var skuMapper = new SkuMapper(new MappingRepository(), "CH-A");
        var items = await new OrderLoader().LoadFromFileAsync(skuMapper, BuildChannelConfig(), _excelFilePath);

        Assert.HasCount(1, items);
        Assert.AreEqual(new DateTime(2026, 6, 20), items[0].OrderDate);
    }

    [TestMethod]
    public async Task LoadFromFileAsync_PopulatesSourceRowKey_StableAcrossReload()
    {
        // 버그2(발주처 이력 충돌) 근본원인 수정 검증: 같은 파일을 두 번 로드해도(=재로드 시나리오)
        // 같은 엑셀 행에서 온 항목은 SourceRowKey가 같아야 ShipmentGroupKey도 일치해 재저장 시
        // 같은 이력 레코드로 매칭된다.
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "주문번호";
            sheet.Cells[1, 2].Value = "상품명";
            sheet.Cells[2, 1].Value = "";
            sheet.Cells[2, 2].Value = "상품A";
            sheet.Cells[3, 1].Value = "";
            sheet.Cells[3, 2].Value = "상품B";
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var skuMapper = new SkuMapper(new MappingRepository(), "CH-A");
        var firstLoad = await new OrderLoader().LoadFromFileAsync(skuMapper, BuildChannelConfig(), _excelFilePath);
        var reloaded = await new OrderLoader().LoadFromFileAsync(skuMapper, BuildChannelConfig(), _excelFilePath);

        Assert.HasCount(2, firstLoad);
        Assert.AreEqual($"{Path.GetFileName(_excelFilePath)}#2", firstLoad[0].SourceRowKey);
        Assert.AreEqual($"{Path.GetFileName(_excelFilePath)}#3", firstLoad[1].SourceRowKey);
        Assert.AreEqual(firstLoad[0].SourceRowKey, reloaded[0].SourceRowKey);
        Assert.AreEqual(firstLoad[1].SourceRowKey, reloaded[1].SourceRowKey);
        Assert.AreNotEqual(firstLoad[0].SourceRowKey, firstLoad[1].SourceRowKey);
    }

    [TestMethod]
    public async Task LoadFromFileAsync_TextFormattedDateCell_ParsesOrderDate()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "주문번호";
            sheet.Cells[1, 2].Value = "상품명";
            sheet.Cells[1, 3].Value = "발주일";
            sheet.Cells[2, 1].Value = "ORDER-2";
            sheet.Cells[2, 2].Value = "상품B";
            sheet.Cells[2, 3].Value = "2026-06-15";
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var skuMapper = new SkuMapper(new MappingRepository(), "CH-A");
        var items = await new OrderLoader().LoadFromFileAsync(skuMapper, BuildChannelConfig(), _excelFilePath);

        Assert.HasCount(1, items);
        Assert.AreEqual(new DateTime(2026, 6, 15), items[0].OrderDate);
    }

    [TestMethod]
    public async Task LoadFromFileAsync_NoOrderDateMapping_LeavesOrderDateNull()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "주문번호";
            sheet.Cells[1, 2].Value = "상품명";
            sheet.Cells[2, 1].Value = "ORDER-3";
            sheet.Cells[2, 2].Value = "상품C";
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "CH-A",
            ChannelName = "테스트채널",
            OrderFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductNo] = new FieldMapping { HeaderRow = 1, Column = "주문번호" },
                [StdField.ProductName] = new FieldMapping { HeaderRow = 1, Column = "상품명" },
            },
        };

        var skuMapper = new SkuMapper(new MappingRepository(), "CH-A");
        var items = await new OrderLoader().LoadFromFileAsync(skuMapper, channelConfig, _excelFilePath);

        Assert.HasCount(1, items);
        Assert.IsNull(items[0].OrderDate);
    }

    [TestMethod]
    public async Task LoadFromFileAsync_RowWithAllMappedFieldsBlank_IsSkipped()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "주문번호";
            sheet.Cells[1, 2].Value = "상품명";
            sheet.Cells[1, 3].Value = "발주일";
            sheet.Cells[2, 1].Value = "ORDER-1";
            sheet.Cells[2, 2].Value = "상품A";
            sheet.Cells[2, 3].Value = new DateTime(2026, 6, 20);
            sheet.Cells[2, 3].Style.Numberformat.Format = "yyyy-mm-dd";
            // 3행: 셀 서식만 있고 값은 전부 공란인 줄(엑셀에서 흔히 남는 빈 줄)
            sheet.Cells[3, 1].Style.Font.Bold = true;
            sheet.Cells[4, 1].Value = "ORDER-2";
            sheet.Cells[4, 2].Value = "상품B";
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var skuMapper = new SkuMapper(new MappingRepository(), "CH-A");
        var items = await new OrderLoader().LoadFromFileAsync(skuMapper, BuildChannelConfig(), _excelFilePath);

        Assert.HasCount(2, items);
        Assert.AreEqual("ORDER-1", items[0].OrderNo);
        Assert.AreEqual("ORDER-2", items[1].OrderNo);
    }

    /// <summary>
    /// 고정값(FixedValue) 채널에서 꼬리 빈 행을 올바르게 건너뛰어야 한다.
    /// 구버전 IsBlankRow(item)는 item.Recipient = 고정값으로 항상 채워져 빈 행 판단 실패했음.
    /// </summary>
    [TestMethod]
    public async Task LoadFromFileAsync_TrailingBlankRow_FixedValueChannel_IsSkipped()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "상품명";
            sheet.Cells[2, 1].Value = "상품A";          // 실제 데이터 행
            sheet.Cells[3, 1].Style.Font.Bold = true;   // 빈 행(서식만 있고 값 없음)
            sheet.Cells[4, 1].Value = "상품B";          // 실제 데이터 행
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "CH-FIX",
            ChannelName = "고정수취인채널",
            OrderFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { HeaderRow = 1, Column = "상품명" },
                [StdField.Recipient]   = new FieldMapping { HeaderRow = 1, FixedValue = "물류창고" },
                [StdField.Address]     = new FieldMapping { HeaderRow = 1, FixedValue = "서울시 강남구" },
            },
        };

        var skuMapper = new SkuMapper(new MappingRepository(), "CH-FIX");
        var items = await new OrderLoader().LoadFromFileAsync(skuMapper, channelConfig, _excelFilePath);

        Assert.HasCount(2, items);
        Assert.AreEqual("상품A", items[0].ProductName);
        Assert.AreEqual("물류창고", items[0].Recipient);
        Assert.AreEqual("상품B", items[1].ProductName);
    }

    [TestMethod]
    public void IsBlankRow_AllMappedFieldsBlank_ReturnsTrue()
    {
        Assert.IsTrue(OrderLoader.IsBlankRow(new OfsOrderItem { Quantity = 0 }));
    }

    [TestMethod]
    public void IsBlankRow_OnlyOneFieldFilled_ReturnsFalse()
    {
        Assert.IsFalse(OrderLoader.IsBlankRow(new OfsOrderItem { ProductName = "상품A" }));
        Assert.IsFalse(OrderLoader.IsBlankRow(new OfsOrderItem { OrderDate = new DateTime(2026, 6, 20) }));
    }
}
