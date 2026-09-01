using MiniERP2.DataLoaders;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class CskuStatFileParserTests
{
    private static readonly string[] Headers =
        ["채널", "상품그룹", "상품명", "옵션명", "매핑SKU", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액", "상태"];

    private static ExcelPackage BuildPackage(string sheetName, IEnumerable<object?[]> rows)
    {
        ExcelLicense.Ensure();
        var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add(sheetName);
        for (int i = 0; i < Headers.Length; i++) sheet.Cells[1, i + 1].Value = Headers[i];

        int row = 2;
        foreach (var values in rows)
        {
            for (int col = 0; col < values.Length; col++) sheet.Cells[row, col + 1].Value = values[col];
            row++;
        }
        return package;
    }

    [TestMethod]
    public void Parse_MissingDetailSheet_ReturnsError()
    {
        using var package = BuildPackage("다른시트", []);

        var result = CskuStatFileParser.Parse(package, "a.xlsx", CskuFileKind.General);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage, "분석결과상세");
    }

    [TestMethod]
    public void Parse_MissingRequiredHeader_ReturnsError()
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("분석결과상세");
        // "상태" 헤더를 빼먹은 경우.
        var partial = Headers.Take(Headers.Length - 1).ToArray();
        for (int i = 0; i < partial.Length; i++) sheet.Cells[1, i + 1].Value = partial[i];

        var result = CskuStatFileParser.Parse(package, "a.xlsx", CskuFileKind.General);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ErrorMessage, "상태");
    }

    [TestMethod]
    public void Parse_ClassifiesAllEightStatusStrings()
    {
        using var package = BuildPackage("분석결과상세",
        [
            ["COUPANG", "그룹", "상품", "옵션", "CSKU1", 1, 1000, 900, 100, 50, 300, "매핑(1:1)"],
            ["COUPANG", "그룹", "상품", "옵션", "CSKU2", 1, 1000, 900, 100, 50, 300, "매핑(조건)"],
            ["COUPANG", "그룹", "상품", "옵션", "CSKU3", 1, 1000, 900, 100, 50, 300, "매핑(임시)"],
            ["COUPANG", "그룹", "상품", "옵션", "CSKU4", 1, 1000, 900, 100, 50, 300, "매핑(예외)"],
            ["COUPANG", "그룹", "상품", "옵션", "", 1, 1000, 900, 100, 50, 300, "제외(배송비 등)"],
            ["COUPANG", "그룹", "상품", "옵션", "", 1, 1000, 900, 100, 50, 300, "매핑 키 없음"],
            ["COUPANG", "그룹", "상품", "옵션", "", 1, 1000, 900, 100, 50, 300, "매핑 실패"],
            ["COUPANG", "그룹", "상품", "옵션", "CSKU8", 1, 1000, 900, 100, 50, 300, "원가 정보 없음"],
        ]);

        var result = CskuStatFileParser.Parse(package, "a.xlsx", CskuFileKind.General);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(8, result.Rows.Count);
        Assert.AreEqual(4, result.Rows.Count(r => r.RowClass == CskuStatRowClass.Normal));
        Assert.AreEqual(1, result.Rows.Count(r => r.RowClass == CskuStatRowClass.Excluded));
        Assert.AreEqual(3, result.Rows.Count(r => r.RowClass == CskuStatRowClass.Unmapped));
    }

    [TestMethod]
    public void Parse_ExcludedRow_WithBlankMappedSku_IsClassifiedByStatusNotBlankCheck()
    {
        using var package = BuildPackage("분석결과상세",
        [
            ["COUPANG", "그룹", "상품", "옵션", "", 1, 1000, 900, 100, 50, 300, "제외(배송비 등)"],
        ]);

        var result = CskuStatFileParser.Parse(package, "a.xlsx", CskuFileKind.General);

        Assert.AreEqual(CskuStatRowClass.Excluded, result.Rows.Single().RowClass);
        Assert.AreEqual(string.Empty, result.Rows.Single().CskuCode);
    }

    [TestMethod]
    public void Parse_NonNumericAmount_ForcesUnmappedAndWarns()
    {
        using var package = BuildPackage("분석결과상세",
        [
            ["COUPANG", "그룹", "상품", "옵션", "CSKU1", 1, "N/A", 900, 100, 50, 300, "매핑(1:1)"],
        ]);

        var result = CskuStatFileParser.Parse(package, "a.xlsx", CskuFileKind.General);

        Assert.AreEqual(CskuStatRowClass.Unmapped, result.Rows.Single().RowClass);
        Assert.AreEqual(1, result.Warnings.Count);
    }

    [TestMethod]
    public void Parse_BlankRow_IsSkipped()
    {
        using var package = BuildPackage("분석결과상세",
        [
            ["COUPANG", "그룹", "상품", "옵션", "CSKU1", 1, 1000, 900, 100, 50, 300, "매핑(1:1)"],
            [null, null, null, null, null, null, null, null, null, null, null, null],
        ]);

        var result = CskuStatFileParser.Parse(package, "a.xlsx", CskuFileKind.General);

        Assert.AreEqual(1, result.Rows.Count);
    }

    [TestMethod]
    public void Parse_HeaderMatchedByName_NotPosition()
    {
        // 열 순서를 뒤바꿔도(구버전 파일 호환, §1.1) 헤더 문자열로 정확히 매칭돼야 한다.
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("분석결과상세");
        var shuffled = new[] { "상태", "매핑SKU", "채널", "상품그룹", "상품명", "옵션명", "수량", "매출액", "정산액", "배송비", "입출고비", "이익액" };
        for (int i = 0; i < shuffled.Length; i++) sheet.Cells[1, i + 1].Value = shuffled[i];
        sheet.Cells[2, 1].Value = "매핑(1:1)";
        sheet.Cells[2, 2].Value = "CSKU1";
        sheet.Cells[2, 3].Value = "COUPANG";
        sheet.Cells[2, 4].Value = "그룹";
        sheet.Cells[2, 5].Value = "상품";
        sheet.Cells[2, 6].Value = "옵션";
        sheet.Cells[2, 7].Value = 3;
        sheet.Cells[2, 8].Value = 1000;

        var result = CskuStatFileParser.Parse(package, "a.xlsx", CskuFileKind.General);

        Assert.IsTrue(result.Success);
        var row = result.Rows.Single();
        Assert.AreEqual("COUPANG", row.ChannelCode);
        Assert.AreEqual("CSKU1", row.CskuCode);
        Assert.AreEqual(3, row.Qty);
        Assert.AreEqual(1000m, row.Revenue);
    }
}
