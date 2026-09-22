using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.DataLoaders;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerConsolidationFileLoaderTests
{
    private static readonly string[] DetailHeaders =
        ["채널", "상품그룹", "상품명", "옵션명", "매핑SKU", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액", "상태"];

    private string _testFolder = string.Empty;
    private ChannelSkuRepository _channelSkuRepository = new();

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _channelSkuRepository = new ChannelSkuRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static ExcelPackage BuildPackage(
        IEnumerable<object?[]> detailRows,
        FileMeta? meta = null,
        bool includeDetailSheet = true)
    {
        ExcelLicense.Ensure();
        var package = new ExcelPackage();
        if (includeDetailSheet)
        {
            var sheet = package.Workbook.Worksheets.Add("분석결과상세");
            for (int i = 0; i < DetailHeaders.Length; i++) sheet.Cells[1, i + 1].Value = DetailHeaders[i];
            int row = 2;
            foreach (var values in detailRows)
            {
                for (int c = 0; c < values.Length; c++) sheet.Cells[row, c + 1].Value = values[c];
                row++;
            }
        }
        if (meta != null)
            MetaSheetHelper.WriteToPackage(package, meta);
        return package;
    }

    private void SaveCsku(string channel, string csku, string msku, decimal price = 0) =>
        _channelSkuRepository.Upsert(new ChannelSkuModel { ChannelCode = channel, CskuCode = csku, Msku = msku, SupplyPrice = price });

    [TestMethod]
    public void Load_MappedRow_DirectCskuMatch_ResolvesCsku()
    {
        SaveCsku("CH1", "CSKU-A", "MSKU-A");
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "CSKU-A", 3, 1000, 900, 100, 0, 500, "매핑(1:1)"]],
            new FileMeta { ChannelCode = "CH1", ChannelName = "쿠팡일반", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.IsNull(file.ErrorMessage);
        Assert.AreEqual(1, file.RowCount);
        var row = file.Rows[0];
        Assert.AreEqual(PartnerConsolidationRowKind.Mapped, row.Kind);
        Assert.AreEqual("CSKU-A", row.ResolvedCskuCode);
        Assert.AreEqual("MSKU-A", row.ResolvedMsku);
        Assert.AreEqual("펩투나", row.CompanyName);
        Assert.AreEqual(3, row.Quantity);
    }

    [TestMethod]
    public void Load_MappedRow_MasterSkuFallback_ResolvesUniqueCsku()
    {
        SaveCsku("CH1", "CH1-OPT-A", "MASTER-X");
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "MASTER-X", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"]],
            new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        var row = file.Rows[0];
        Assert.AreEqual(PartnerConsolidationRowKind.Mapped, row.Kind);
        Assert.AreEqual("CH1-OPT-A", row.ResolvedCskuCode);
    }

    [TestMethod]
    public void Load_MappedRow_MasterSkuAmbiguous_MarksCskuUnresolved()
    {
        SaveCsku("CH1", "CH1-OPT-A", "MASTER-X");
        SaveCsku("CH1", "CH1-OPT-B", "MASTER-X");
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "MASTER-X", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"]],
            new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.AreEqual(PartnerConsolidationRowKind.CskuUnresolved, file.Rows[0].Kind);
    }

    [TestMethod]
    public void Load_MappedRow_NoMatchAtAll_AutoCreatesAndPersistsNewCsku_StaysMapped()
    {
        // 채널에 이 CSKU 자체가 등록돼 있지 않은 경우(과거 방식 1:1 규칙 등) — CskuUnresolved로
        // 빠지면 "미매핑·제외" 탭에 묻혀 납품단가 입력 대상이 되지 못하므로 Mapped를 유지해야
        // 한다. 매핑SKU를 마스터SKU로 간주해 그 채널에 새 CSKU를 즉시 만들어 저장한다(2026-09
        // 사용자 피드백: 화면 표시용 가짜 코드만 보여주고 저장하지 않으면 CSKU 편집 화면에서
        // 일괄수정할 대상이 없어 매번 하나하나 새로 등록해야 했다) — SalesChannel이 등록되지
        // 않은 채널이라 코드 접두사는 채널코드 자체로 대체된다.
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "UNKNOWN-SKU", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"]],
            new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        var row = file.Rows[0];
        Assert.AreEqual(PartnerConsolidationRowKind.Mapped, row.Kind);
        Assert.AreEqual("CH1_UNKNOWN-SKU", row.ResolvedCskuCode);
        Assert.AreEqual("UNKNOWN-SKU", row.ResolvedMsku);

        var persisted = _channelSkuRepository.GetByChannelAndCskuCode("CH1", "CH1_UNKNOWN-SKU");
        Assert.IsNotNull(persisted);
        Assert.AreEqual("UNKNOWN-SKU", persisted!.Msku);
    }

    [TestMethod]
    public void Load_MappedRow_NoMatchAtAll_CalledTwiceForSameMsku_ReusesSameAutoCreatedCsku()
    {
        // 같은 파일(또는 같은 채널) 안에서 같은 미등록 마스터SKU가 여러 행에 반복되면, 매번 새
        // CSKU를 만드는 게 아니라 첫 행에서 만든 CSKU를 재사용해야 한다(캐시 갱신 확인).
        using var package = BuildPackage(
            [
                ["CH1", "그룹1", "상품A", "옵션1", "UNKNOWN-SKU", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"],
                ["CH1", "그룹1", "상품A", "옵션2", "UNKNOWN-SKU", 2, 1000, 900, 100, 0, 500, "매핑(1:1)"],
            ],
            new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.AreEqual(file.Rows[0].ResolvedCskuCode, file.Rows[1].ResolvedCskuCode);
        Assert.AreEqual(1, _channelSkuRepository.GetAllByChannel("CH1").Count(c => c.Msku == "UNKNOWN-SKU"));
    }

    [TestMethod]
    public void Load_BlankMappedSku_MarksUnmapped()
    {
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "", 1, 1000, 900, 100, 0, 0, "매핑 키 없음"]],
            new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.AreEqual(PartnerConsolidationRowKind.Unmapped, file.Rows[0].Kind);
        Assert.IsNull(file.Rows[0].ResolvedCskuCode);
    }

    [TestMethod]
    public void Load_ExceptionExcludedStatus_MarksExcluded()
    {
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "CSKU-A", 1, 1000, 900, 100, 0, 0, "제외(배송비 등)"]],
            new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.AreEqual(PartnerConsolidationRowKind.Excluded, file.Rows[0].Kind);
    }

    [TestMethod]
    public void Load_NoMetaSheet_ReadsRowsButFlagsMissingMeta()
    {
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "CSKU-A", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"]]);

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.IsFalse(file.HasMetaSheet);
        Assert.AreEqual("", file.CompanyName);
        Assert.AreEqual(1, file.RowCount);
        // _META가 없어도 행 자체의 '채널' 컬럼으로 CSKU 조회는 정상 동작해야 한다.
        Assert.AreEqual("CH1", file.Rows[0].ChannelCode);
    }

    [TestMethod]
    public void Load_MissingDetailSheet_ReturnsErrorMessage()
    {
        using var package = BuildPackage([], includeDetailSheet: false);

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.IsTrue(file.LoadFailed);
        StringAssert.Contains(file.ErrorMessage, "분석결과상세");
    }

    [TestMethod]
    public void Load_BlankTrailingRow_IsSkipped()
    {
        using var package = BuildPackage(
        [
            ["CH1", "그룹1", "상품A", "옵션1", "CSKU-A", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"],
            ["", "", "", "", "", 0, null, null, null, null, null, ""],
        ], new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.AreEqual(1, file.RowCount);
    }

    [TestMethod]
    public void Load_TracksNumbers_FromRawDataSheet_UsingChannelFieldMapping()
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var detail = package.Workbook.Worksheets.Add("분석결과상세");
        for (int i = 0; i < DetailHeaders.Length; i++) detail.Cells[1, i + 1].Value = DetailHeaders[i];
        detail.Cells[2, 1].Value = "CH1"; detail.Cells[2, 5].Value = "CSKU-A"; detail.Cells[2, 6].Value = 1; detail.Cells[2, 12].Value = "매핑(1:1)";

        var raw = package.Workbook.Worksheets.Add("원본데이터");
        raw.Cells[1, 1].Value = "운송장번호"; raw.Cells[1, 2].Value = "기타";
        raw.Cells[2, 1].Value = "TRK001"; raw.Cells[3, 1].Value = "TRK002";

        MetaSheetHelper.WriteToPackage(package, new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var channelConfigService = new ChannelConfigService();
        channelConfigService.Save(
        [
            new ChannelConfig
            {
                ChannelCode = "CH1",
                ChannelName = "쿠팡일반",
                SettlementFieldMappings = new Dictionary<StdField, FieldMapping>
                {
                    [StdField.TrackingNo] = new FieldMapping { Column = "운송장번호" },
                },
            },
        ]);

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository, channelConfigService);

        CollectionAssert.AreEquivalent(new[] { "TRK001", "TRK002" }, file.TrackingNumbers);
    }

    [TestMethod]
    public void Load_NoFieldMappingForTrackingNo_ReturnsEmptyTrackingNumbers()
    {
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "CSKU-A", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"]],
            new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var channelConfigService = new ChannelConfigService();
        channelConfigService.Save([new ChannelConfig { ChannelCode = "CH1", ChannelName = "쿠팡일반" }]);

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository, channelConfigService);

        Assert.IsEmpty(file.TrackingNumbers);
    }

    [TestMethod]
    public void Load_ShippingColumn_SummedIntoFileShippingTotal()
    {
        using var package = BuildPackage(
        [
            ["CH1", "그룹1", "상품A", "옵션1", "CSKU-A", 1, 1000, 900, 1500, 0, 500, "매핑(1:1)"],
            ["CH1", "그룹1", "상품B", "옵션1", "CSKU-B", 1, 1000, 900, 2500, 0, 500, "매핑(1:1)"],
        ], new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.AreEqual(4000m, file.ShippingTotal);
    }

    [TestMethod]
    public void Load_SchemaV1Meta_FlagsIsSchemaV1()
    {
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "CSKU-A", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"]],
            new FileMeta { ChannelCode = "CH1", CompanyName = "", SchemaVersion = 1 });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.IsTrue(file.HasMetaSheet);
        Assert.IsTrue(file.IsSchemaV1);
    }
}
