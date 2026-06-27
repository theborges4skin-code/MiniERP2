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
}
