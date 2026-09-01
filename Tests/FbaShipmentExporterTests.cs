using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class FbaShipmentExporterTests
{
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"FbaShipmentExporterTests_{Guid.NewGuid()}.xlsx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_filePath)) File.Delete(_filePath);
    }

    [TestMethod]
    public void Export_WritesHeadersInSpecifiedOrder()
    {
        FbaShipmentExporter.Export([], [], [], _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["선적명세"];

        string[] expected =
        [
            "상품명(내부관리용)",
            "ASIN", "Commodity Descriptions", "hs code", "carton no.", "carton", "unit", "qty",
            "item price", "weight (kg)", "weight (lb)", "size (cm)", "size (inch)",
            "유통기한", "MOCRA Listing No.",
        ];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], sheet.Cells[1, i + 1].Value);
        }
    }

    [TestMethod]
    public void Export_FirstColumnUsesInvoiceDisplayNameFallingBackToItemName()
    {
        var box = new FbaBox { FbaNo = "FBA-1", BoxSeq = 1, WeightG = 10 };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 1, Csku = "A", ItemName = "내부명", InvoiceDisplayName = "샴푸 500ml", Qty = 1 },
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 2, Csku = "B", ItemName = "내부명2", Qty = 1 },
        };

        FbaShipmentExporter.Export([box], items, [], _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["선적명세"];
        Assert.AreEqual("샴푸 500ml", sheet.Cells[2, 1].Value);
        Assert.AreEqual("내부명2", sheet.Cells[3, 1].Value); // InvoiceDisplayName 비어있으면 ItemName으로 대체
    }

    [TestMethod]
    public void Export_LastTwoColumns_FormatExpiryDateAndLookUpMocraListingNo()
    {
        var box = new FbaBox { FbaNo = "FBA-1", BoxSeq = 1, WeightG = 10 };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 1, Csku = "A", ItemName = "A", Qty = 1, ExpiryDate = "20260805" },
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 2, Csku = "B", ItemName = "B", Qty = 1, ExpiryDate = null },
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 3, Csku = "C", ItemName = "C", Qty = 1, ExpiryDate = "2028.07.07." },
        };
        var cskus = new List<FbaCskuModel>
        {
            new() { Csku = "A", MocraListingNo = "M12345" },
        };

        FbaShipmentExporter.Export([box], items, cskus, _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["선적명세"];

        Assert.AreEqual("AUG.05.2026.EXP.", sheet.Cells[2, 14].Value);
        Assert.AreEqual("M12345", sheet.Cells[2, 15].Value);

        // 유통기한 미입력, CSKU 마스터에 없는 품목(B)은 둘 다 빈 칸이어야 한다.
        Assert.AreEqual(string.Empty, sheet.Cells[3, 14].Value);
        Assert.AreEqual(string.Empty, sheet.Cells[3, 15].Value);

        // 실제 그리드에 흔한 "yyyy.MM.dd."(끝에 점) 입력 형식도 파싱돼야 한다(2026-08-05 등록 후
        // 유효기간이 공란으로 나오던 사용자 신고 케이스 재현).
        Assert.AreEqual("JUL.07.2028.EXP.", sheet.Cells[4, 14].Value);
    }

    [TestMethod]
    public void Export_WeightIsPerItemRow_NotJustBoxRepresentativeRow()
    {
        // 박스 총무게(WeightG=250)가 아니라 CSKU 행별로 UnitWeightG×Qty를 계산해 모든 행에 채워야
        // 한다(요청사항). size/carton은 여전히 대표행에만.
        var box = new FbaBox { FbaNo = "FBA-1", BoxSeq = 1, WidthMm = 500, DepthMm = 280, HeightMm = 300, WeightG = 250 };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 1, Csku = "A", ItemName = "A", Asin = "ASIN-A", Qty = 2, UnitWeightG = 150, ItemPrice = 1.5m },
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 2, Csku = "B", ItemName = "B", Asin = "ASIN-B", Qty = 1, UnitWeightG = 400, ItemPrice = 2.5m },
        };

        FbaShipmentExporter.Export([box], items, [], _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["선적명세"];

        // 대표행(2행): carton=1(진짜 숫자), 무게=150g×2=300g=0.3kg, 치수 채움
        Assert.AreEqual(1, Convert.ToInt32(sheet.Cells[2, 6].Value));
        Assert.AreEqual(0.3, Convert.ToDouble(sheet.Cells[2, 10].Value));
        Assert.AreEqual(0.66, Convert.ToDouble(sheet.Cells[2, 11].Value));
        Assert.AreEqual("50.0*28.0*30.0", sheet.Cells[2, 12].Value);

        // 두번째 행(3행): carton/치수는 대표행 전용이라 빈 셀이지만, 무게는 이 행의
        // UnitWeightG×Qty(400g×1=0.4kg)로 채워져야 한다 — 박스 총무게가 아니다.
        Assert.IsNull(sheet.Cells[3, 6].Value);
        Assert.IsNull(sheet.Cells[3, 12].Value);
        Assert.IsNull(sheet.Cells[3, 13].Value);
        Assert.AreEqual(0.4, Convert.ToDouble(sheet.Cells[3, 10].Value));
        Assert.AreEqual(0.88, Convert.ToDouble(sheet.Cells[3, 11].Value));
    }

    [TestMethod]
    public void Export_UnitEqualsQty_OnEveryRow()
    {
        var box = new FbaBox { FbaNo = "FBA-1", BoxSeq = 1, WeightG = 100 };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 1, Csku = "A", ItemName = "A", Qty = 3 },
        };

        FbaShipmentExporter.Export([box], items, [], _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["선적명세"];
        Assert.AreEqual(3, Convert.ToInt32(sheet.Cells[2, 7].Value)); // unit
        Assert.AreEqual(3, Convert.ToInt32(sheet.Cells[2, 8].Value)); // qty = unit
    }

    [TestMethod]
    public void Export_UnitConversions_RoundToSpecifiedDecimalPlaces()
    {
        // 500x280x280mm, 무게는 CSKU 행의 UnitWeightG×Qty = 12345g×1
        var box = new FbaBox { FbaNo = "FBA-1", BoxSeq = 1, WidthMm = 500, DepthMm = 280, HeightMm = 280 };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 1, Csku = "A", ItemName = "A", Qty = 1, UnitWeightG = 12345, ItemPrice = 9.999m },
        };

        FbaShipmentExporter.Export([box], items, [], _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["선적명세"];

        Assert.AreEqual(12.3, Convert.ToDouble(sheet.Cells[2, 10].Value)); // kg: 12345/1000=12.345 -> 12.3(소수1자리)
        Assert.AreEqual(27.12, Convert.ToDouble(sheet.Cells[2, 11].Value)); // lb: 12.3*2.20462=27.116... -> 27.12(소수2자리)
        Assert.AreEqual("50.0*28.0*28.0", sheet.Cells[2, 12].Value); // cm
        Assert.AreEqual("19.69*11.02*11.02", sheet.Cells[2, 13].Value); // inch
        Assert.AreEqual(10.00m, Convert.ToDecimal(sheet.Cells[2, 9].Value)); // item price 소수2자리
    }

    [TestMethod]
    public void Export_SortsByCartonNoThenItemSeq()
    {
        var boxes = new List<FbaBox>
        {
            new() { FbaNo = "FBA-1", BoxSeq = 2, WeightG = 10 },
            new() { FbaNo = "FBA-1", BoxSeq = 1, WeightG = 10 },
        };
        var items = new List<FbaBoxItem>
        {
            new() { FbaNo = "FBA-1", BoxSeq = 2, ItemSeq = 1, Csku = "C", ItemName = "C", Qty = 1, ItemPrice = 3m },
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 2, Csku = "B", ItemName = "B", Qty = 1, ItemPrice = 2m },
            new() { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 1, Csku = "A", ItemName = "A", Qty = 1, ItemPrice = 1m },
        };

        FbaShipmentExporter.Export(boxes, items, [], _filePath);

        ExcelLicense.Ensure();
        using var package = new ExcelPackage(new FileInfo(_filePath));
        var sheet = package.Workbook.Worksheets["선적명세"];
        // carton no. 오름차순(1,1,2), 같은 박스 안에서는 ItemSeq 순(1,2)이어야 한다 — item price로 식별.
        Assert.AreEqual(1, Convert.ToInt32(sheet.Cells[2, 5].Value));
        Assert.AreEqual(1.00m, Convert.ToDecimal(sheet.Cells[2, 9].Value));
        Assert.AreEqual(1, Convert.ToInt32(sheet.Cells[3, 5].Value));
        Assert.AreEqual(2.00m, Convert.ToDecimal(sheet.Cells[3, 9].Value));
        Assert.AreEqual(2, Convert.ToInt32(sheet.Cells[4, 5].Value));
        Assert.AreEqual(3.00m, Convert.ToDecimal(sheet.Cells[4, 9].Value));
    }
}
