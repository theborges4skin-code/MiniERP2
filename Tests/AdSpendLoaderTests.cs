using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Mapping;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class AdSpendLoaderTests
{
    private string _testFolder = string.Empty;
    private string _excelFilePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _excelFilePath = Path.Combine(_testFolder, "ad_spend.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public async Task LoadFromFileAsync_ParsesCostAndAppliesMapping()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("Sheet1");
            sheet.Cells[1, 1].Value = "상품명";
            sheet.Cells[1, 2].Value = "캠페인";
            sheet.Cells[1, 3].Value = "총비용";
            sheet.Cells[2, 1].Value = "전기면도기";
            sheet.Cells[2, 2].Value = "CAMPAIGN-1";
            sheet.Cells[2, 3].Value = "1,234원";
            package.SaveAs(new FileInfo(_excelFilePath));
        }

        var channelConfig = new ChannelConfig
        {
            ChannelCode = "CH-A",
            ChannelName = "테스트채널",
            AdFieldMappings = new Dictionary<AdStdField, FieldMapping>
            {
                [AdStdField.ProductName] = new FieldMapping { HeaderRow = 1, Column = "상품명" },
                [AdStdField.ProductId] = new FieldMapping { HeaderRow = 1, Column = "캠페인" },
                [AdStdField.Cost] = new FieldMapping { HeaderRow = 1, Column = "총비용" },
            },
        };

        var repository = new AdMappingRepository();
        repository.AddConditionRuleWithDetails("CH-A", "면도규칙", "14.면도",
            [new AdConditionDetail { HeaderField = AdStdField.ProductName, Operator = AdConditionOperator.Contains, TargetValue = "면도", Logic = ConditionLogic.And }]);
        var engine = new AdMappingEngine(repository, "CH-A");

        var items = await new AdSpendLoader().LoadFromFileAsync(engine, channelConfig, _excelFilePath);

        Assert.HasCount(1, items);
        Assert.AreEqual(1234m, items[0].Cost);
        Assert.AreEqual("14.면도", items[0].MappedGroup);
        Assert.AreEqual("조건부", items[0].MatchType);

        // "광고매핑상세" 내보내기가 원본 열을 그대로 실어야 하므로, 표준필드로 매핑되지 않은
        // "캠페인" 열도 RawValues에 남아있어야 한다.
        Assert.IsNotNull(items[0].RawValues);
        Assert.AreEqual("전기면도기", items[0].RawValues!["상품명"]);
        Assert.AreEqual("CAMPAIGN-1", items[0].RawValues!["캠페인"]);
    }
}
