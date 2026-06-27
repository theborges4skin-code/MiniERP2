using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class CourierExporterTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"CourierExporterTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [TestMethod]
    public async Task ExportAsync_WritesHeadersAndMappedValuesPerCourierFormat()
    {
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = """{ "받는분": "Recipient", "연락처": "Phone", "운송장번호": "TrackingNo" }"""
        };
        var orders = new List<OfsOrderItem>
        {
            new() { Recipient = "홍길동", Phone = "010-1234-5678", TrackingNo = "T001" },
            new() { Recipient = "김철수", Phone = "010-9876-5432", TrackingNo = "T002" },
        };

        var exporter = new CourierExporter();
        await exporter.ExportAsync(orders, courier, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];

        Assert.AreEqual("받는분", sheet.Cells[1, 1].Value);
        Assert.AreEqual("연락처", sheet.Cells[1, 2].Value);
        Assert.AreEqual("운송장번호", sheet.Cells[1, 3].Value);

        Assert.AreEqual("홍길동", sheet.Cells[2, 1].Value);
        Assert.AreEqual("010-1234-5678", sheet.Cells[2, 2].Value);
        Assert.AreEqual("T001", sheet.Cells[2, 3].Value);

        Assert.AreEqual("김철수", sheet.Cells[3, 1].Value);
        Assert.AreEqual("T002", sheet.Cells[3, 3].Value);
    }

    [TestMethod]
    public async Task ExportAsync_WithChannelOverride_UsesFixedValueInsteadOfOrderData()
    {
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = """{ "받는분": "Recipient", "도착지코드": "Recipient" }"""
        };
        var orders = new List<OfsOrderItem> { new() { ChannelCode = "PARTNER", Recipient = "홍길동" } };
        var channelConfigs = new Dictionary<string, ChannelConfig>
        {
            ["PARTNER"] = new ChannelConfig
            {
                ChannelCode = "PARTNER",
                CourierHeaderOverrides = [new CourierHeaderOverride { CourierName = "테스트택배", Header = "도착지코드", FixedValue = "DEPOT-01" }],
            },
        };

        var exporter = new CourierExporter();
        await exporter.ExportAsync(orders, courier, _filePath, channelConfigs);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];

        Assert.AreEqual("홍길동", sheet.Cells[2, 1].Value); // 받는분: 고정값 없음 -> 주문 데이터 그대로
        Assert.AreEqual("DEPOT-01", sheet.Cells[2, 2].Value); // 도착지코드: 채널별 고정값 적용
    }

    [TestMethod]
    public async Task ExportAsync_SameOrderNoWithoutExplicitGroup_StaysAsTwoSeparateRows()
    {
        // 합포장은 택배사 프로그램이 다운스트림에서 자동으로 처리하므로, MiniERP2는 같은 주문번호라도
        // 명시적으로 합포장을 지정하지 않으면 임의로 합치지 않는다(기본값 = 줄마다 별도 송장).
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = """{ "받는분": "Recipient", "품목": "ProductName" }"""
        };
        var orders = new List<OfsOrderItem>
        {
            new() { OrderNo = "ORDER-1", Recipient = "홍길동", ProductName = "상품A", Quantity = 2 },
            new() { OrderNo = "ORDER-1", Recipient = "홍길동", ProductName = "상품B", Quantity = 1 },
        };

        var exporter = new CourierExporter();
        var overflow = await exporter.ExportAsync(orders, courier, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];

        Assert.IsEmpty(overflow);
        Assert.AreEqual("홍길동", sheet.Cells[2, 1].Value);
        Assert.AreEqual("상품A 2개", sheet.Cells[2, 2].Value);
        Assert.AreEqual("홍길동", sheet.Cells[3, 1].Value);
        Assert.AreEqual("상품B 1개", sheet.Cells[3, 2].Value);
    }

    [TestMethod]
    public async Task ExportAsync_DifferentOrderNoSameShipmentGroupId_MergesIntoOneRow()
    {
        // 합포장: 서로 다른 주문이라도 같은 ShipmentGroupId를 주면 한 송장으로 합쳐져야 한다.
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = """{ "품목": "ProductName" }"""
        };
        var orders = new List<OfsOrderItem>
        {
            new() { OrderNo = "ORDER-1", ProductName = "상품A", Quantity = 1, ShipmentGroupId = "BOX-1" },
            new() { OrderNo = "ORDER-2", ProductName = "상품B", Quantity = 1, ShipmentGroupId = "BOX-1" },
        };

        var exporter = new CourierExporter();
        await exporter.ExportAsync(orders, courier, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];

        Assert.AreEqual("((상품A 1개))   +   ((상품B 1개))", sheet.Cells[2, 1].Value);
        Assert.IsNull(sheet.Cells[3, 1].Value);
    }

    [TestMethod]
    public async Task ExportAsync_SameOrderNoDifferentShipmentGroupId_SplitsIntoTwoRows()
    {
        // 분리배송: 같은 주문이라도 ShipmentGroupId가 다르면 별도 송장(행)으로 나가야 한다.
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = """{ "품목": "ProductName" }"""
        };
        var orders = new List<OfsOrderItem>
        {
            new() { OrderNo = "ORDER-1", ProductName = "상품A", Quantity = 1, ShipmentGroupId = "ORDER-1-분리1" },
            new() { OrderNo = "ORDER-1", ProductName = "상품B", Quantity = 1, ShipmentGroupId = "ORDER-1-분리2" },
        };

        var exporter = new CourierExporter();
        await exporter.ExportAsync(orders, courier, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];

        Assert.AreEqual("상품A 1개", sheet.Cells[2, 1].Value);
        Assert.AreEqual("상품B 1개", sheet.Cells[3, 1].Value);
    }

    [TestMethod]
    public async Task ExportAsync_GroupWithMoreThanFourLines_ReturnsOverflowWarningButStillExports()
    {
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = """{ "품목": "ProductName" }"""
        };
        // 명시적으로 합포장(같은 ShipmentGroupId)을 지정한 경우에만 한 묶음으로 모여, 5줄짜리
        // 묶음이 만들어질 수 있다(기본값으로는 합쳐지지 않으므로 일부러 합포장 상태를 만든다).
        var orders = Enumerable.Range(1, 5)
            .Select(i => new OfsOrderItem { OrderNo = "ORDER-1", ProductName = $"상품{i}", Quantity = 1, ShipmentGroupId = "BOX-1" })
            .ToList();

        var exporter = new CourierExporter();
        var overflow = await exporter.ExportAsync(orders, courier, _filePath);

        Assert.HasCount(1, overflow);
        Assert.AreEqual("ORDER-1", overflow[0]);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];
        Assert.AreEqual("((상품1 1개))   +   ((상품2 1개))   +   ((상품3 1개))   +   ((상품4 1개))   +   ((상품5 1개))", sheet.Cells[2, 1].Value);
    }

    [TestMethod]
    public async Task ExportAsync_UsesInvoiceLabelOverProductNameWhenSet()
    {
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = """{ "품목": "InvoiceLabel" }"""
        };
        var orders = new List<OfsOrderItem>
        {
            new() { OrderNo = "ORDER-1", ProductName = "원본상품명", InvoiceLabel = "샴푸 500ml 2개" },
        };

        var exporter = new CourierExporter();
        await exporter.ExportAsync(orders, courier, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];
        Assert.AreEqual("샴푸 500ml 2개", sheet.Cells[2, 1].Value);
    }

    [TestMethod]
    public async Task ExportAsync_PreservesSampleHeaderOrderExactly()
    {
        // CourierConfigForm이 저장하는 순서가 보장되는 형식(JSON 배열) — 샘플 양식에서 불러온
        // 순서 그대로 출력되어야 택배사 프로그램이 파일을 인식할 수 있다는 요구사항의 회귀 테스트.
        var courier = new CourierMaster
        {
            CourierName = "테스트택배",
            HeaderMappingJson = CourierHeaderMapping.Serialize(new[]
            {
                new HeaderMappingEntry("E열", ""),
                new HeaderMappingEntry("받는분", "Recipient"),
                new HeaderMappingEntry("C열", ""),
                new HeaderMappingEntry("운송장번호", "TrackingNo"),
                new HeaderMappingEntry("A열", ""),
            })
        };
        var orders = new List<OfsOrderItem> { new() { Recipient = "홍길동", TrackingNo = "T001" } };

        var exporter = new CourierExporter();
        await exporter.ExportAsync(orders, courier, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["Sheet1"];

        Assert.AreEqual("E열", sheet.Cells[1, 1].Value);
        Assert.AreEqual("받는분", sheet.Cells[1, 2].Value);
        Assert.AreEqual("C열", sheet.Cells[1, 3].Value);
        Assert.AreEqual("운송장번호", sheet.Cells[1, 4].Value);
        Assert.AreEqual("A열", sheet.Cells[1, 5].Value);
        Assert.AreEqual("홍길동", sheet.Cells[2, 2].Value);
        Assert.AreEqual("T001", sheet.Cells[2, 4].Value);
    }
}
