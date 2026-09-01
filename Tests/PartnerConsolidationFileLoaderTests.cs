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
    public void Load_MappedRow_NoMatchAtAll_MarksCskuUnresolved()
    {
        using var package = BuildPackage(
            [["CH1", "그룹1", "상품A", "옵션1", "UNKNOWN-SKU", 1, 1000, 900, 100, 0, 500, "매핑(1:1)"]],
            new FileMeta { ChannelCode = "CH1", CompanyName = "펩투나" });

        var file = PartnerConsolidationFileLoader.LoadFromPackage(package, "a.xlsx", _channelSkuRepository);

        Assert.AreEqual(PartnerConsolidationRowKind.CskuUnresolved, file.Rows[0].Kind);
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
