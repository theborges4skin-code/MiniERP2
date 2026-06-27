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
public class SettlementLoaderRawValuesTests
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
    public async Task LoadFromFileAsync_PopulatesRawValues_WithAllOriginalHeaders()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var main = package.Workbook.Worksheets.Add("메인");
            main.Cells[1, 1].Value = "상품명";
            main.Cells[1, 2].Value = "수량";
            main.Cells[1, 3].Value = "메모"; // 표준필드로 매핑되지 않은 열도 RawValues에는 남아야 한다
            main.Cells[2, 1].Value = "상품A";
            main.Cells[2, 2].Value = 3;
            main.Cells[2, 3].Value = "특이사항없음";
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "CH1",
            ChannelName = "테스트채널",
            SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
            {
                [StdField.ProductName] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "상품명" },
                [StdField.Quantity] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "수량" },
            },
        };

        var skuMapper = new SkuMapper(new MappingRepository(), "CH1");
        var rows = await new SettlementLoader().LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath);

        Assert.HasCount(1, rows);
        Assert.IsNotNull(rows[0].RawValues);
        Assert.AreEqual("상품A", rows[0].RawValues!["상품명"]);
        Assert.AreEqual("3", rows[0].RawValues!["수량"]);
        Assert.AreEqual("특이사항없음", rows[0].RawValues!["메모"]);
    }
}
