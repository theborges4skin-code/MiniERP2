using System.Diagnostics;
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
/// 2026-06-28 성능 신고: 데이터가 적은 정산파일도 로드가 1분 이상 걸렸다. 원인은 엑셀 파일에
/// 실제 데이터보다 훨씬 뒤까지 서식이 적용돼 있으면 EPPlus의 worksheet.Dimension이 그 범위까지를
/// "데이터 범위"로 보고해, 실제로는 비어있는 행을 끝까지 순회하기 때문이었다. 상품명/옵션명이
/// 모두 빈 행이 일정 수 이상 연속되면 더 일찍 멈추도록 한 가드를 검증한다.
/// </summary>
[TestClass]
public class SettlementLoaderBlankRowGuardTests
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
    public async Task LoadFromFileAsync_RealDataFollowedByFormattedButEmptyRows_StopsEarlyAndLoadsQuickly()
    {
        ExcelLicense.Ensure();
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("메인");
            sheet.Cells[1, 1].Value = "상품명";
            sheet.Cells[1, 2].Value = "수량";
            sheet.Cells[1, 3].Value = "정산액";

            // 실제 데이터는 3행뿐이다.
            for (int row = 2; row <= 4; row++)
            {
                sheet.Cells[row, 1].Value = $"상품{row}";
                sheet.Cells[row, 2].Value = 1;
                sheet.Cells[row, 3].Value = 1000;
            }

            // 실무에서 자주 보이는 상황: 데이터 없는 행에도 서식만 길게 적용되어 있어
            // worksheet.Dimension이 실제 데이터보다 훨씬 뒤(예: 5,000행)까지 잡힌다.
            for (int row = 5; row <= 5000; row++)
            {
                sheet.Cells[row, 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                sheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.White);
            }

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
                [StdField.SettlementAmount] = new FieldMapping { SheetName = "메인", HeaderRow = 1, Column = "정산액" },
            },
        };

        var skuMapper = new SkuMapper(new MappingRepository(), "CH1");

        var stopwatch = Stopwatch.StartNew();
        var rows = await new SettlementLoader().LoadFromFileAsync(skuMapper, new ItemRepository(), channelConfig, _excelFilePath);
        stopwatch.Stop();

        Assert.HasCount(3, rows);
        // 5000행짜리 서식 범위를 셀 단위로 다 훑었다면 훨씬 오래 걸렸을 것 — 연속 빈 행 가드가
        // 동작했다는 것을 시간으로도 확인한다(여유 있게 5초로 잡음, CI 환경 변동 감안).
        Assert.IsLessThan(5000, stopwatch.ElapsedMilliseconds);
    }
}
