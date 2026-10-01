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

        var layout = new AdFileLayout
        {
            LayoutName = "기본",
            FieldMappings = new Dictionary<AdStdField, FieldMapping>
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

        var items = await new AdSpendLoader().LoadFromFileAsync(engine, "CH-A", layout, _excelFilePath);

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

    private static AdFileLayout ElevenStLayout() => new()
    {
        LayoutName = "항목별보고서",
        MatchColumns = ["상품번호", "평균노출순위", "총비용"],
        FieldMappings = new Dictionary<AdStdField, FieldMapping>
        {
            [AdStdField.ProductName] = new FieldMapping { HeaderRow = 1, Column = "상품명" },
            [AdStdField.ProductId] = new FieldMapping { HeaderRow = 1, Column = "상품번호" },
            [AdStdField.Cost] = new FieldMapping { HeaderRow = 1, Column = "총비용" },
        },
    };

    [TestMethod]
    public async Task LoadFromFileAsync_Utf16TabSeparatedCsv_ParsesColumnsAndSkipsTotalRow()
    {
        // 11번가 광고 리포트: 확장자는 .csv지만 UTF-16(BOM) 탭 구분이고, 2행에 "합계" 행이 있다.
        var path = Path.Combine(_testFolder, "11st.csv");
        File.WriteAllText(path,
            "상품번호\t상품명\t평균노출순위\t총비용\r\n" +
            "합계\t\t2.1\t3000\r\n" +
            "111\t면도기 세정액\t3.5\t2000\r\n" +
            "222\t커피머신 클리너\t1.2\t1000\r\n" +
            "333\t노출만 된 상품\t2.5\t-\r\n" +
            "444\t과금 없는 상품\t4.0\t0\r\n",
            System.Text.Encoding.Unicode);

        var loader = new AdSpendLoader();
        var layout = ElevenStLayout();
        Assert.HasCount(1, loader.DetectLayout(path, [layout]));

        var engine = new AdMappingEngine(new AdMappingRepository(), "CH-A");
        var items = await loader.LoadFromFileAsync(engine, "CH-A", layout, path);

        Assert.IsFalse(loader.LastLoadHeaderRowLooksEmpty);
        Assert.HasCount(2, items);
        Assert.AreEqual("111", items[0].ProductId);
        Assert.AreEqual("면도기 세정액", items[0].ProductName);
        Assert.AreEqual(3000m, items.Sum(i => i.Cost));
    }

    [TestMethod]
    public async Task LoadFromFileAsync_Cp949Csv_DecodesKoreanHeaders()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var path = Path.Combine(_testFolder, "cp949.csv");
        File.WriteAllText(path, "상품번호,상품명,평균노출순위,총비용\r\n111,면도기,3.5,\"1,500\"\r\n", System.Text.Encoding.GetEncoding(949));

        var engine = new AdMappingEngine(new AdMappingRepository(), "CH-A");
        var items = await new AdSpendLoader().LoadFromFileAsync(engine, "CH-A", ElevenStLayout(), path);

        Assert.HasCount(1, items);
        Assert.AreEqual("면도기", items[0].ProductName);
        Assert.AreEqual(1500m, items[0].Cost);
    }
}
