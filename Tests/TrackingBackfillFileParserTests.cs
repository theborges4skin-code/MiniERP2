using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

/// <summary>
/// 운송장 결과 파일 파서의 헤더 열 해석을 검증한다. 특히 로젠택배 양식처럼 헤더가 2줄
/// (그룹행 "수하인"/"송하인" + 항목행 "이름"/"주소")로 나뉘어 항목행 기준으로는 같은 헤더명이
/// 수하인·송하인 양쪽에 중복으로 나타나는 경우, 뒤에 오는 송하인 열을 잡으면 발송인이 수령인으로
/// 들어가 매칭이 전부 어긋난다.
/// </summary>
[TestClass]
public class TrackingBackfillFileParserTests
{
    /// <summary>실제 로젠택배 결과 파일과 같은 구조(1행 공백, 2행 그룹, 3행 항목, 4행부터 데이터).</summary>
    private static ExcelPackage MakeRosenPackage()
    {
        ExcelLicense.Ensure();
        var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("주문등록_출력");

        string[] groupRow = ["운송장번호", "수하인", "수하인", "수하인", "물품명", "주문번호", "송하인", "송하인", "송하인"];
        string[] itemRow = ["운송장번호", "이름", "주소", "전화", "물품명", "주문번호", "이름", "주소", "전화"];
        for (int col = 1; col <= groupRow.Length; col++)
        {
            sheet.Cells[2, col].Value = groupRow[col - 1];
            sheet.Cells[3, col].Value = itemRow[col - 1];
        }

        string[] data = ["45168327960", "유진상사 귀중", "경기 파주시 장지산로 396-24", "010-6352-9668",
                         "LG 명작 오브제 59호 [2개]", "ORD-1", "와이피무역㈜ 윤여경", "인천 계양구 오조산로89번길 6", "010-5231-9012"];
        for (int col = 1; col <= data.Length; col++) sheet.Cells[4, col].Value = data[col - 1];

        return package;
    }

    private static CourierMaster RosenCourier() => new()
    {
        CourierName = "로젠택배",
        TrackingImportHeaderRow = 3,
        TrackingImportRecipientHeader = "이름",
        TrackingImportTrackingNoHeader = "운송장번호",
        TrackingImportOrderNoHeader = "주문번호",
        TrackingImportAddressHeader = "주소",
        TrackingImportProductNameHeader = "물품명",
    };

    [TestMethod]
    public void Parse_TwoRowHeaderWithDuplicateNames_UsesFirstMatchingColumn()
    {
        using var package = MakeRosenPackage();

        var result = TrackingBackfillFileParser.Parse(package.Workbook.Worksheets[0], RosenCourier(), "9월15일발송.xlsx");

        Assert.IsNull(result.Error);
        Assert.AreEqual(1, result.Rows.Count);
        var row = result.Rows[0];
        // 송하인("와이피무역㈜ 윤여경")이 아니라 수하인을 읽어야 한다.
        Assert.AreEqual("유진상사 귀중", row.Recipient);
        Assert.AreEqual("경기 파주시 장지산로 396-24", row.Address);
        Assert.AreEqual("45168327960", row.TrackingNo);
        Assert.AreEqual("LG 명작 오브제 59호 [2개]", row.ProductName);
        Assert.AreEqual("ORD-1", row.OrderNoMemo);
    }

    [TestMethod]
    public void Parse_SkipsRowsWithoutTrackingNo()
    {
        using var package = MakeRosenPackage();
        var sheet = package.Workbook.Worksheets[0];
        sheet.Cells[5, 2].Value = "진근태 귀하";   // 운송장번호 없이 수하인만 있는 행

        var result = TrackingBackfillFileParser.Parse(sheet, RosenCourier(), "9월15일발송.xlsx");

        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual(1, result.SkippedNoTrackingNo);
    }
}
