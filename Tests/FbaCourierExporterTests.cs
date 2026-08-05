using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class FbaCourierExporterTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"FbaCourierExporterTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    private static FbaOrder MakeOrder(string? shipmentId = "FBA15ABCDEFG") => new()
    {
        FbaNo = "FBA-20260805-01",
        OrderDate = new DateTime(2026, 8, 5),
        ShipmentId = shipmentId,
        ReceiverName = "설레는유통",
        Phone = "02-1234-5678",
        Address = "서울시 어딘가 123",
    };

    private static FbaConfigModel MakeConfig() => new()
    {
        Phone2 = "010-9999-8888",
        DeliveryMessage = "부재시 문앞",
        TransferType = "일반",
        BoxTypeLabel = "중",
        Etc1 = "FBA전용",
    };

    [TestMethod]
    public void Export_WritesHeadersAndReceiverSnapshotFromOrder()
    {
        var order = MakeOrder();
        var config = MakeConfig();
        var boxes = new List<FbaBox> { new() { FbaNo = order.FbaNo, BoxSeq = 1, MatchKey = "[SEND] FBA15ABCDEFG 총 1박스중 1번째" } };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = order.FbaNo, BoxSeq = 1, ItemSeq = 1, Csku = "CSKU-1", ItemName = "샴푸", Qty = 1 },
        };

        FbaCourierExporter.Export(order, config, boxes, items, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["하배출고이서"];

        Assert.AreEqual("반품부성명", sheet.Cells[1, 1].Value);
        Assert.AreEqual("고객주문번호", sheet.Cells[1, 11].Value);

        Assert.AreEqual("설레는유통1", sheet.Cells[2, 1].Value); // 반품부성명은 박스번호가 붙는다(박스1 → "설레는유통1")
        Assert.AreEqual("02-1234-5678", sheet.Cells[2, 2].Value);
        Assert.AreEqual("010-9999-8888", sheet.Cells[2, 3].Value);
        Assert.AreEqual("서울시 어딘가 123", sheet.Cells[2, 4].Value);
        Assert.AreEqual("부재시 문앞", sheet.Cells[2, 5].Value);
        Assert.AreEqual("일반", sheet.Cells[2, 8].Value);
        Assert.AreEqual("중", sheet.Cells[2, 9].Value);
        Assert.AreEqual("FBA전용", sheet.Cells[2, 10].Value);
    }

    [TestMethod]
    public void Export_UsesInvoiceDisplayNamePlusQuantityTagForItemName()
    {
        var order = MakeOrder();
        var config = MakeConfig();
        var boxes = new List<FbaBox> { new() { FbaNo = order.FbaNo, BoxSeq = 1, MatchKey = "key" } };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = order.FbaNo, BoxSeq = 1, ItemSeq = 1, Csku = "CSKU-1", ItemName = "내부명", InvoiceDisplayName = "샴푸 500ml", Qty = 7 },
        };

        FbaCourierExporter.Export(order, config, boxes, items, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["하배출고이서"];
        Assert.AreEqual("샴푸 500ml ▶vii[7개]", sheet.Cells[2, 6].Value);
        Assert.AreEqual(string.Empty, sheet.Cells[2, 7].Value); // 반입수량은 공란
    }

    [TestMethod]
    public void Export_WritesBoxMatchKeyAsCustomerOrderNumber_MatchingTotalBoxesAndSeq()
    {
        var order = MakeOrder();
        var config = MakeConfig();
        var boxes = new List<FbaBox>
        {
            new() { FbaNo = order.FbaNo, BoxSeq = 1, MatchKey = FbaKeyGenerator.BuildMatchKey(order.ShipmentId, 2, 1) },
            new() { FbaNo = order.FbaNo, BoxSeq = 2, MatchKey = FbaKeyGenerator.BuildMatchKey(order.ShipmentId, 2, 2) },
        };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = order.FbaNo, BoxSeq = 1, ItemSeq = 1, Csku = "CSKU-1", ItemName = "A", Qty = 1 },
            new() { FbaNo = order.FbaNo, BoxSeq = 2, ItemSeq = 1, Csku = "CSKU-2", ItemName = "B", Qty = 1 },
        };

        FbaCourierExporter.Export(order, config, boxes, items, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["하배출고이서"];
        Assert.AreEqual("[SEND] FBA15ABCDEFG 총 2박스중 1번째", sheet.Cells[2, 11].Value);
        Assert.AreEqual("[SEND] FBA15ABCDEFG 총 2박스중 2번째", sheet.Cells[3, 11].Value);
    }

    [TestMethod]
    public void Export_ReceiverNameHasBoxSeqSuffix_SoCourierSystemMergesOnlyWithinSameBox()
    {
        // 수취지가 전 박스 공통이라 성명을 그대로 두면 택배시스템이 전 박스를 하나로 합포장해버린다.
        // 박스별로 성명 뒤에 박스번호를 붙여, 같은 박스 안 여러 품목 줄끼리만(이름·전화·주소 모두
        // 동일) 자동 합포장되고 박스 사이는 섞이지 않게 한다.
        var order = MakeOrder();
        var config = MakeConfig();
        var boxes = new List<FbaBox>
        {
            new() { FbaNo = order.FbaNo, BoxSeq = 1, MatchKey = "key1" },
            new() { FbaNo = order.FbaNo, BoxSeq = 2, MatchKey = "key2" },
        };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = order.FbaNo, BoxSeq = 1, ItemSeq = 1, Csku = "A", ItemName = "A", Qty = 1 },
            new() { FbaNo = order.FbaNo, BoxSeq = 1, ItemSeq = 2, Csku = "B", ItemName = "B", Qty = 1 },
            new() { FbaNo = order.FbaNo, BoxSeq = 2, ItemSeq = 1, Csku = "C", ItemName = "C", Qty = 1 },
        };

        FbaCourierExporter.Export(order, config, boxes, items, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["하배출고이서"];

        Assert.AreEqual("설레는유통1", sheet.Cells[2, 1].Value);
        Assert.AreEqual("설레는유통1", sheet.Cells[3, 1].Value);
        Assert.AreEqual("설레는유통2", sheet.Cells[4, 1].Value);
    }
}
