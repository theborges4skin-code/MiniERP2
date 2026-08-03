using MiniERP2.DataLoaders;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

/// <summary>
/// 채널 일괄등록(엑셀) 기획서 §6 테스트 계획 검증. 파싱/검증/최종객체 조립 로직(DB에 실제로
/// 쓰지 않는 부분)만 다룬다 — 커밋(SQLite 트랜잭션 + JSON 저장)은 ChannelBulkImportPreviewDialog가
/// 담당하며 UI 레이어라 여기서는 다루지 않는다.
/// </summary>
[TestClass]
public class ChannelBulkImportLoaderTests
{
    private string _testFolder = string.Empty;
    private string _excelFilePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        _excelFilePath = Path.Combine(_testFolder, "channels.xlsx");
        ExcelLicense.Ensure();
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_testFolder, recursive: true);
    }

    private static void WriteHeaders(ExcelWorksheet sheet, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];
    }

    private static void WriteMeta(ExcelPackage package, int schemaVersion = 1)
    {
        var sheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.MetaSheet);
        sheet.Cells[1, 1].Value = "schema_version";
        sheet.Cells[1, 2].Value = schemaVersion;
    }

    private ExcelPackage NewPackage(out ExcelWorksheet channelSheet, out ExcelWorksheet orderSheet, out ExcelWorksheet settlementSheet, out ExcelWorksheet partySheet)
    {
        var package = new ExcelPackage();
        channelSheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.ChannelSheet);
        WriteHeaders(channelSheet, ChannelBulkImportSchema.ChannelHeaders);
        orderSheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.OrderMappingSheet);
        WriteHeaders(orderSheet, ChannelBulkImportSchema.MappingHeaders);
        settlementSheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.SettlementMappingSheet);
        WriteHeaders(settlementSheet, ChannelBulkImportSchema.MappingHeaders);
        partySheet = package.Workbook.Worksheets.Add(ChannelBulkImportSchema.PartySheet);
        WriteHeaders(partySheet, ChannelBulkImportSchema.PartyHeaders);
        return package;
    }

    // 채널 시트 열 순서: 채널코드,채널명,채널유형,그룹,표시순서,즐겨찾기,매입,매출,환율,누적발주서,누적조회일수,자동발주채널힌트,설정복사원본
    private static void WriteChannelRow(ExcelWorksheet sheet, int row, string code, string name, string type,
        string? group = null, string favorite = "N", string purchase = "N", string sales = "Y",
        string exchangeRate = "", string cumulative = "N", string cumulativeDays = "", string autoOrderHints = "", string copySource = "")
    {
        sheet.Cells[row, 1].Value = code;
        sheet.Cells[row, 2].Value = name;
        sheet.Cells[row, 3].Value = type;
        sheet.Cells[row, 4].Value = group;
        sheet.Cells[row, 6].Value = favorite;
        sheet.Cells[row, 7].Value = purchase;
        sheet.Cells[row, 8].Value = sales;
        sheet.Cells[row, 9].Value = exchangeRate;
        sheet.Cells[row, 10].Value = cumulative;
        sheet.Cells[row, 11].Value = cumulativeDays;
        sheet.Cells[row, 12].Value = autoOrderHints;
        sheet.Cells[row, 13].Value = copySource;
    }

    private static void WriteMappingRow(ExcelWorksheet sheet, int row, string channelName, string fieldLabel, string? column, int headerRow = 1, string? sheetName = null, string? fixedValue = null)
    {
        sheet.Cells[row, 1].Value = channelName;
        sheet.Cells[row, 2].Value = fieldLabel;
        sheet.Cells[row, 3].Value = sheetName;
        sheet.Cells[row, 4].Value = headerRow;
        sheet.Cells[row, 5].Value = column;
        sheet.Cells[row, 6].Value = fixedValue;
    }

    private ChannelBulkImportResult LoadResult(ExcelPackage package, List<SalesChannel>? existingChannels = null,
        List<ChannelConfig>? existingConfigs = null, List<DocParty>? existingParties = null, int schemaVersion = 1)
    {
        WriteMeta(package, schemaVersion);
        package.SaveAs(new FileInfo(_excelFilePath));
        package.Dispose();
        return new ChannelBulkImportLoader().Load(_excelFilePath,
            existingChannels ?? new List<SalesChannel>(),
            existingConfigs ?? new List<ChannelConfig>(),
            existingParties ?? new List<DocParty>());
    }

    [TestMethod]
    public void Load_NewChannelsWithoutCode_AssignsSequentialCodesWithoutCollision()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        WriteChannelRow(channelSheet, 2, "", "신규채널A", "온라인");
        WriteChannelRow(channelSheet, 3, "", "신규채널B", "온라인");

        var existingChannels = new List<SalesChannel> { new() { ChannelCode = "CH001", ChannelName = "기존채널" } };
        var result = LoadResult(package, existingChannels);

        Assert.IsFalse(result.HasBlockingErrors);
        Assert.AreEqual(2, result.NewCount);
        var codes = result.ChannelRows.Select(r => r.ResolvedChannelCode).ToList();
        Assert.AreEqual("CH002", codes[0]);
        Assert.AreEqual("CH003", codes[1]);
    }

    [TestMethod]
    public void Load_DuplicateChannelNameInFile_BlocksWithFileError()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        WriteChannelRow(channelSheet, 2, "", "쿠팡", "온라인");
        WriteChannelRow(channelSheet, 3, "", "쿠팡", "온라인");

        var result = LoadResult(package);

        Assert.IsTrue(result.HasBlockingErrors);
        Assert.IsTrue(result.FileErrors.Count > 0);
        Assert.IsTrue(result.ChannelRows.All(r => r.HasErrors));
    }

    [TestMethod]
    public void Load_ChannelCodeNotInDb_RowError()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        WriteChannelRow(channelSheet, 2, "CH999", "존재안함", "온라인");

        var result = LoadResult(package);

        Assert.IsTrue(result.HasBlockingErrors);
        Assert.AreEqual(ChannelImportRowStatus.Error, result.ChannelRows[0].Status);
    }

    [TestMethod]
    public void Load_BlankCodeMatchesExistingName_ResolvesAsUpdateWithWarningNotError()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        WriteChannelRow(channelSheet, 2, "", "쿠팡", "쿠팡 일반", sales: "Y");

        var existingChannels = new List<SalesChannel> { new() { ChannelCode = "CH001", ChannelName = "쿠팡", IsSales = true } };
        var existingConfigs = new List<ChannelConfig> { new() { ChannelCode = "CH001", ChannelName = "쿠팡", ChannelType = ChannelType.General } };
        var result = LoadResult(package, existingChannels, existingConfigs);

        Assert.IsFalse(result.HasBlockingErrors);
        var row = result.ChannelRows[0];
        Assert.AreEqual("CH001", row.ResolvedChannelCode);
        Assert.AreEqual(ChannelImportRowStatus.Update, row.Status);
        Assert.IsTrue(row.Warnings.Any(w => w.Contains("이름만으로")));
    }

    [TestMethod]
    public void Load_AmbiguousNameDifferentCode_RowError()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        // CH002 행인데 채널명이 CH001의 이름과 같음 → 모호
        WriteChannelRow(channelSheet, 2, "CH002", "쿠팡", "온라인");

        var existingChannels = new List<SalesChannel>
        {
            new() { ChannelCode = "CH001", ChannelName = "쿠팡" },
            new() { ChannelCode = "CH002", ChannelName = "11번가" },
        };
        var result = LoadResult(package, existingChannels);

        Assert.IsTrue(result.HasBlockingErrors);
        Assert.AreEqual(ChannelImportRowStatus.Error, result.ChannelRows[0].Status);
    }

    [TestMethod]
    public void Load_SettlementFieldNotAllowedForChannelType_WarnsAndExcludesFromMapping()
    {
        var package = NewPackage(out var channelSheet, out _, out var settlementSheet, out _);
        WriteChannelRow(channelSheet, 2, "", "그로스채널", "쿠팡 그로스");
        // TrackingNo(실제발송송장수)는 쿠팡그로스 허용 목록에 없다(§2.4).
        WriteMappingRow(settlementSheet, 2, "그로스채널", StdFieldLabels.GetLabel(StdField.TrackingNo), "송장번호");
        WriteMappingRow(settlementSheet, 3, "그로스채널", StdFieldLabels.GetLabel(StdField.Revenue), "매출액");

        var result = LoadResult(package);

        Assert.IsFalse(result.HasBlockingErrors);
        var mappingWarning = result.MappingRows.First(m => m.ResolvedField == StdField.TrackingNo);
        Assert.IsTrue(mappingWarning.Warnings.Count > 0);

        var row = result.ChannelRows[0];
        Assert.IsFalse(row.FinalConfig!.SettlementFieldMappings.ContainsKey(StdField.TrackingNo));
        Assert.IsTrue(row.FinalConfig!.SettlementFieldMappings.ContainsKey(StdField.Revenue));
    }

    [TestMethod]
    public void Load_CopySourceDeepCopiesAuxCollections_AndKeepsBlankColumnsFromSource()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        // 채널유형 칸을 비워두면(설정복사원본 지정 시 허용) 원본의 채널유형을 그대로 이어받아야 한다.
        WriteChannelRow(channelSheet, 2, "", "복제채널", "", copySource: "원본채널");

        var existingChannels = new List<SalesChannel> { new() { ChannelCode = "CH001", ChannelName = "원본채널" } };
        var existingConfigs = new List<ChannelConfig>
        {
            new()
            {
                ChannelCode = "CH001",
                ChannelName = "원본채널",
                ChannelType = ChannelType.CoupangGrowth,
                GrowthAuxSources = [new GrowthAuxSource { SheetName = "보조" }],
            },
        };

        var result = LoadResult(package, existingChannels, existingConfigs);

        Assert.IsFalse(result.HasBlockingErrors);
        var row = result.ChannelRows[0];
        Assert.AreEqual(ChannelType.CoupangGrowth, row.FinalConfig!.ChannelType);
        Assert.HasCount(1, row.FinalConfig!.GrowthAuxSources);
        // 복사본은 원본과 별개 인스턴스여야 한다(얕은 참조 공유 금지).
        Assert.AreNotSame(existingConfigs[0].GrowthAuxSources, row.FinalConfig!.GrowthAuxSources);
    }

    [TestMethod]
    public void Load_CopySourceWithAutoOrderPreset_ResetsFlagOnCopyAndWarns()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        WriteChannelRow(channelSheet, 2, "", "복제채널", "온라인", copySource: "자동발주표준");

        var existingChannels = new List<SalesChannel> { new() { ChannelCode = "CH001", ChannelName = "자동발주표준" } };
        var existingConfigs = new List<ChannelConfig>
        {
            new() { ChannelCode = "CH001", ChannelName = "자동발주표준", IsAutoOrderStandardPreset = true },
        };

        var result = LoadResult(package, existingChannels, existingConfigs);

        Assert.IsFalse(result.HasBlockingErrors);
        var row = result.ChannelRows[0];
        Assert.IsFalse(row.FinalConfig!.IsAutoOrderStandardPreset);
        Assert.IsTrue(row.Warnings.Any(w => w.Contains("자동발주(표준)")));
        // 원본 채널 자체는 그대로 유지되어야 한다.
        Assert.IsTrue(existingConfigs[0].IsAutoOrderStandardPreset);
    }

    [TestMethod]
    public void Load_ChainedCopySource_RowError()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        WriteChannelRow(channelSheet, 2, "", "B채널", "온라인", copySource: "A채널");
        WriteChannelRow(channelSheet, 3, "", "C채널", "온라인", copySource: "B채널");

        var result = LoadResult(package, new List<SalesChannel> { new() { ChannelCode = "CH001", ChannelName = "A채널" } });

        Assert.IsTrue(result.HasBlockingErrors);
        var cRow = result.ChannelRows.First(r => r.ChannelName == "C채널");
        Assert.AreEqual(ChannelImportRowStatus.Error, cRow.Status);
    }

    [TestMethod]
    public void Load_SelfReferenceCopySource_RowError()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        WriteChannelRow(channelSheet, 2, "", "채널A", "온라인", copySource: "채널A");

        var result = LoadResult(package);

        Assert.IsTrue(result.HasBlockingErrors);
        Assert.AreEqual(ChannelImportRowStatus.Error, result.ChannelRows[0].Status);
    }

    [TestMethod]
    public void Load_UnknownSchemaVersion_BlocksAsFileError()
    {
        var package = NewPackage(out var channelSheet, out _, out _, out _);
        WriteChannelRow(channelSheet, 2, "", "채널A", "온라인");

        var result = LoadResult(package, schemaVersion: 999);

        Assert.IsTrue(result.HasBlockingErrors);
        Assert.IsTrue(result.FileErrors.Count > 0);
    }

    [TestMethod]
    public void ExportCurrent_ThenReimportUnchanged_RoundTripIsIdempotent()
    {
        // §4.6 라이트립 멱등성: 현재 설정을 내보낸 그대로 재업로드하면 전부 "변경없음"이어야 한다.
        var channels = new List<SalesChannel>
        {
            new() { ChannelCode = "CH001", ChannelName = "쿠팡", GroupName = "오픈마켓", DisplayOrder = 1, IsFavorite = true, IsPurchase = false, IsSales = true },
        };
        var configs = new List<ChannelConfig>
        {
            new()
            {
                ChannelCode = "CH001",
                ChannelName = "쿠팡",
                ChannelType = ChannelType.CoupangGeneral,
                ExchangeRate = 1m,
                OrderFieldMappings = new Dictionary<StdField, FieldMapping>
                {
                    [StdField.ProductName] = new FieldMapping { Column = "상품명" },
                },
            },
        };
        var parties = new List<DocParty>
        {
            new() { ChannelCode = "CH001", ProfileName = "쿠팡", CompanyName = "㈜쿠팡", RegNo = "123-45-67890" },
        };

        ChannelTemplateExporter.ExportCurrent(_excelFilePath, channels, configs, parties);

        var result = new ChannelBulkImportLoader().Load(_excelFilePath, channels, configs, parties);

        Assert.IsFalse(result.HasBlockingErrors);
        Assert.AreEqual(1, result.UnchangedCount);
        Assert.AreEqual(0, result.UpdateCount);
        Assert.AreEqual(0, result.NewCount);
    }
}
