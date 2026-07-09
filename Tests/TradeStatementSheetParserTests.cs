using MiniERP2.Migration;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

/// <summary>
/// 실제 레거시 파일(거래명세표_문화리아츠 도로시.xlsx / 거래명세표_기타.xlsx, 133개 시트) 실측 레이아웃을
/// 그대로 재현해 검증한다 — 라벨은 병합 셀 폭이 라벨마다 달라 값 셀까지의 오프셋이 제각각이었다
/// (예: 등록번호 라벨 뒤 3열, 성명 라벨 뒤 1열). 헤더 행도 11/12/15/1행으로 실측상 흩어져 있었다.
/// </summary>
[TestClass]
public class TradeStatementSheetParserTests
{
    private static void AddPartyBlocks(ExcelWorksheet ws, int row, string buyerRegNo, string buyerCompany, string buyerCeo, string buyerAddress, string buyerBizType, string buyerBizItem)
    {
        // 공급자(자사) — 실측상 항상 왼쪽에 존재하지만 파서는 값을 저장하지 않는다(검증용, §1.5).
        ws.Cells[row, 1].Value = "공 급 자";
        ws.Cells[row, 2].Value = "등록번호";
        ws.Cells[row, 5].Value = "107-87-55466";
        ws.Cells[row + 2, 2].Value = "상   호\n(법인명)";
        ws.Cells[row + 2, 5].Value = "(주)신안코퍼레이션";

        // 공급받는자 — 라벨-값 오프셋을 실측처럼 라벨마다 다르게 배치.
        ws.Cells[row, 17].Value = "공급받는자";
        ws.Cells[row, 18].Value = "등록번호";
        ws.Cells[row, 21].Value = buyerRegNo;
        ws.Cells[row + 2, 18].Value = "상   호\n(법인명)";
        ws.Cells[row + 2, 21].Value = buyerCompany;
        ws.Cells[row + 2, 28].Value = "성명";
        ws.Cells[row + 2, 29].Value = buyerCeo;
        ws.Cells[row + 4, 18].Value = "사업장\n주  소";
        ws.Cells[row + 4, 21].Value = buyerAddress;
        ws.Cells[row + 6, 18].Value = "업   태";
        ws.Cells[row + 6, 21].Value = buyerBizType;
        ws.Cells[row + 6, 25].Value = "종목";
        ws.Cells[row + 6, 27].Value = buyerBizItem;
    }

    // ── 8종 시그니처 ────────────────────────────────────────────────────

    [TestMethod]
    public void Parse_YearAndVatIncluded_ResolvesYIncSignatureAndDerivesSupplyTax()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("와이피무역(주) 2401");
        AddPartyBlocks(ws, 3, "122-81-73025", "와이피무역(주)", "윤여경", "인천광역시 계양구", "도매", "화공약품 외");

        ws.Cells[11, 1].Value = "연"; ws.Cells[11, 2].Value = "월"; ws.Cells[11, 3].Value = "일";
        ws.Cells[11, 4].Value = "품목"; ws.Cells[11, 13].Value = "규격"; ws.Cells[11, 15].Value = "수량";
        ws.Cells[11, 17].Value = "단가(VAT포함)"; ws.Cells[11, 21].Value = "금액(VAT포함)"; ws.Cells[11, 27].Value = "비고";

        ws.Cells[12, 1].Value = 24; ws.Cells[12, 2].Value = 1; ws.Cells[12, 3].Value = 31;
        ws.Cells[12, 4].Value = "LG 명작스페셜 48호"; ws.Cells[12, 13].Value = "개";
        ws.Cells[12, 15].Value = 15; ws.Cells[12, 17].Value = 33000; ws.Cells[12, 21].Value = 495000;

        ws.Cells[30, 1].Value = "총계"; ws.Cells[30, 15].Value = 15; ws.Cells[30, 21].Value = 495000;

        var result = TradeStatementSheetParser.Parse(ws, "거래명세표_기타.xlsx");

        Assert.AreEqual("Y-INC", result.TemplateSignature);
        Assert.AreEqual(1, result.Lines.Count);
        var line = result.Lines[0];
        Assert.AreEqual(new DateTime(2024, 1, 31), result.IssueDate);
        Assert.AreEqual(495000m, line.Total);
        Assert.AreEqual(450000m, line.SupplyAmount); // round(495000/1.1)
        Assert.AreEqual(45000m, line.Tax);
        Assert.IsTrue(line.UnitPriceVatIncluded);
        Assert.AreEqual("122-81-73025", result.Buyer!.RegNo);
        Assert.AreEqual("와이피무역(주)", result.Buyer.CompanyName);
        Assert.AreEqual("윤여경", result.Buyer.CeoName);
        Assert.IsTrue(result.TotalsReconciled);
        CollectionAssert.DoesNotContain(result.Flags, "TOTALS_MISMATCH");
    }

    [TestMethod]
    public void Parse_NoYearVatIncluded_ResolvesNIncSignature_AndFallsBackToSheetNameDate()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("주식회사 금호로재 2404");
        AddPartyBlocks(ws, 3, "250-88-01811", "주식회사 금호로재", "이영일", "전라남도 광양시", "건설업 외", "내화물 시공");

        ws.Cells[11, 1].Value = "월"; ws.Cells[11, 2].Value = "일"; ws.Cells[11, 3].Value = "품목";
        ws.Cells[11, 9].Value = "규격"; ws.Cells[11, 13].Value = "수량";
        ws.Cells[11, 15].Value = "단가(VAT포함)"; ws.Cells[11, 21].Value = "금액(VAT포함)";

        ws.Cells[12, 1].Value = 4; ws.Cells[12, 2].Value = 15; ws.Cells[12, 3].Value = "비정제 피마자유 18L";
        ws.Cells[12, 9].Value = "개"; ws.Cells[12, 13].Value = 2; ws.Cells[12, 15].Value = 85000; ws.Cells[12, 21].Value = 170000;

        var result = TradeStatementSheetParser.Parse(ws, "거래명세표_기타.xlsx");

        Assert.AreEqual("N-INC", result.TemplateSignature);
        Assert.AreEqual(new DateTime(2024, 4, 1), result.IssueDate); // 연 컬럼 없음 -> 시트명 "2404"에서 YYMM 보완(일=1일)
        CollectionAssert.Contains(result.Flags, "NO_TOTALS_ROW");
    }

    [TestMethod]
    public void Parse_VatSeparateColumns_UsesRawValuesWithoutDerivation()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("(주)에스와이이엔씨");
        AddPartyBlocks(ws, 3, "401-81-36675", "(주)에스와이이엔씨", "이승엽", "전라북도 군산시", "건설업 외", "전기공사 외");

        ws.Cells[11, 1].Value = "월"; ws.Cells[11, 2].Value = "일"; ws.Cells[11, 3].Value = "품목";
        ws.Cells[11, 9].Value = "규격"; ws.Cells[11, 13].Value = "수량"; ws.Cells[11, 15].Value = "단가";
        ws.Cells[11, 19].Value = "공급가액"; ws.Cells[11, 25].Value = "세액"; ws.Cells[11, 30].Value = "비고";

        ws.Cells[12, 1].Value = 3; ws.Cells[12, 2].Value = 25; ws.Cells[12, 3].Value = "프로필렌글리콜(공업용) 18L";
        ws.Cells[12, 9].Value = "개"; ws.Cells[12, 13].Value = 23; ws.Cells[12, 15].Value = 59500;
        ws.Cells[12, 19].Value = 1368500; ws.Cells[12, 25].Value = 136850;

        var result = TradeStatementSheetParser.Parse(ws, "거래명세표_기타.xlsx");

        Assert.AreEqual("N-SEP", result.TemplateSignature);
        var line = result.Lines[0];
        Assert.AreEqual(1368500m, line.SupplyAmount);
        Assert.AreEqual(136850m, line.Tax);
        Assert.AreEqual(1505350m, line.Total); // 파생: 공급가액+세액
        Assert.IsFalse(line.UnitPriceVatIncluded);
    }

    [TestMethod]
    public void Parse_YearWithSeparateVat_ResolvesYSepSignature()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("제이스크린골프2402");
        AddPartyBlocks(ws, 3, "107-17-21527", "제이스크린골프", "유재철", "서울특별시 영등포구", "서비스", "스크린골프연습장");

        ws.Cells[11, 1].Value = "연"; ws.Cells[11, 2].Value = "월"; ws.Cells[11, 3].Value = "일"; ws.Cells[11, 4].Value = "품목";
        ws.Cells[11, 13].Value = "규격"; ws.Cells[11, 15].Value = "수량"; ws.Cells[11, 17].Value = "단가";
        ws.Cells[11, 20].Value = "공급가액"; ws.Cells[11, 24].Value = "세액"; ws.Cells[11, 28].Value = "비고";

        ws.Cells[12, 1].Value = 24; ws.Cells[12, 2].Value = 2; ws.Cells[12, 3].Value = 5; ws.Cells[12, 4].Value = "레슨권";
        ws.Cells[12, 13].Value = "개"; ws.Cells[12, 15].Value = 1; ws.Cells[12, 17].Value = 100000;
        ws.Cells[12, 20].Value = 100000; ws.Cells[12, 24].Value = 10000;

        var result = TradeStatementSheetParser.Parse(ws, "거래명세표_기타.xlsx");

        Assert.AreEqual("Y-SEP", result.TemplateSignature);
        Assert.AreEqual(new DateTime(2024, 2, 5), result.IssueDate);
    }

    [TestMethod]
    public void Parse_AmountOnlyNoUnitPriceColumn_TreatsAmountAsVatIncludedTotal()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("스펙표3행_금액만");
        AddPartyBlocks(ws, 3, "111-11-11111", "금액만거래처", "홍길동", "서울시", "도매", "잡화");

        // 스펙 §1.3 "3개 시트: 연·월·일·품목·규격·수량·금액·비고" — 단가/공급가액/세액 컬럼 자체가 없다.
        ws.Cells[11, 1].Value = "연"; ws.Cells[11, 2].Value = "월"; ws.Cells[11, 3].Value = "일"; ws.Cells[11, 4].Value = "품목";
        ws.Cells[11, 9].Value = "규격"; ws.Cells[11, 13].Value = "수량"; ws.Cells[11, 17].Value = "금액"; ws.Cells[11, 22].Value = "비고";

        ws.Cells[12, 1].Value = 24; ws.Cells[12, 2].Value = 5; ws.Cells[12, 3].Value = 1; ws.Cells[12, 4].Value = "잡화세트";
        ws.Cells[12, 13].Value = 1; ws.Cells[12, 17].Value = 11000;

        var result = TradeStatementSheetParser.Parse(ws, "거래명세표_기타.xlsx");

        Assert.AreEqual("Y-INC", result.TemplateSignature);
        var line = result.Lines[0];
        Assert.AreEqual(0m, line.UnitPrice); // 단가 컬럼 자체가 없음
        Assert.AreEqual(11000m, line.Total);
        Assert.AreEqual(10000m, line.SupplyAmount);
        Assert.AreEqual(1000m, line.Tax);
    }

    [TestMethod]
    public void Parse_SumLabelVariant_AliasesToTotalField()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("스펙표2행_합계라벨");
        AddPartyBlocks(ws, 3, "222-22-22222", "합계라벨거래처", "김철수", "부산시", "제조", "잡화");

        ws.Cells[11, 1].Value = "월"; ws.Cells[11, 2].Value = "일"; ws.Cells[11, 3].Value = "품목";
        ws.Cells[11, 9].Value = "규격"; ws.Cells[11, 13].Value = "수량"; ws.Cells[11, 15].Value = "단가"; ws.Cells[11, 19].Value = "합계금액";

        ws.Cells[12, 1].Value = 5; ws.Cells[12, 2].Value = 2; ws.Cells[12, 3].Value = "물품A";
        ws.Cells[12, 13].Value = 2; ws.Cells[12, 15].Value = 5500; ws.Cells[12, 19].Value = 11000;

        var result = TradeStatementSheetParser.Parse(ws, "거래명세표_기타.xlsx");

        Assert.AreEqual("N-INC", result.TemplateSignature);
        Assert.AreEqual(11000m, result.Lines[0].Total);
    }

    // ── 헤더 행 위치 가변(§1.4: 11/12/15/1행 실측) ──────────────────────────

    [TestMethod]
    public void Parse_HeaderRowAtRow1_FindsHeaderRegardlessOfPosition()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("헤더1행");
        ws.Cells[1, 1].Value = "품목"; ws.Cells[1, 2].Value = "수량"; ws.Cells[1, 3].Value = "금액";
        ws.Cells[2, 1].Value = "품목A"; ws.Cells[2, 2].Value = 1; ws.Cells[2, 3].Value = 1100;

        var result = TradeStatementSheetParser.Parse(ws, "f.xlsx");

        Assert.AreEqual(1, result.Lines.Count);
        Assert.AreEqual("품목A", result.Lines[0].ItemName);
    }

    [TestMethod]
    public void Parse_HeaderRowAtRow15_FindsHeaderRegardlessOfPosition()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("헤더15행");
        ws.Cells[15, 4].Value = "품목"; ws.Cells[15, 15].Value = "수량"; ws.Cells[15, 21].Value = "금액(VAT포함)";
        ws.Cells[16, 4].Value = "품목B"; ws.Cells[16, 15].Value = 3; ws.Cells[16, 21].Value = 3300;

        var result = TradeStatementSheetParser.Parse(ws, "f.xlsx");

        Assert.AreEqual(1, result.Lines.Count);
        Assert.AreEqual("품목B", result.Lines[0].ItemName);
    }

    // ── 익명 거래처 / 노이즈·사본·폐기 플래그(§1.2, §3.6) ───────────────────

    [TestMethod]
    public void Parse_AnonymousBuyerWithNoRegNoOrCompany_FlagsNoBuyerIdentity()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("미국손님");
        // 실측 그대로: 공급받는자 라벨/필드는 있지만 등록번호·상호는 공란, 성명만 존재.
        ws.Cells[3, 1].Value = "공 급 자"; ws.Cells[3, 17].Value = "공급받는자"; ws.Cells[3, 18].Value = "등록번호";
        ws.Cells[5, 18].Value = "상   호"; ws.Cells[5, 28].Value = "성명"; ws.Cells[5, 29].Value = "AIDA YEM";

        ws.Cells[11, 1].Value = "월"; ws.Cells[11, 2].Value = "일"; ws.Cells[11, 3].Value = "품목"; ws.Cells[11, 19].Value = "금액";
        ws.Cells[12, 1].Value = 6; ws.Cells[12, 2].Value = 1; ws.Cells[12, 3].Value = "선물세트"; ws.Cells[12, 19].Value = 5500;

        var result = TradeStatementSheetParser.Parse(ws, "거래명세표_기타.xlsx");

        Assert.IsNotNull(result.Buyer);
        Assert.AreEqual("", result.Buyer!.RegNo);
        Assert.AreEqual("AIDA YEM", result.Buyer.CeoName);
        CollectionAssert.Contains(result.Flags, "NO_BUYER_IDENTITY");
    }

    [TestMethod]
    public void Parse_CopySuffixInSheetName_FlagsCopySuspected()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("미국손님 (2)");
        ws.Cells[11, 1].Value = "품목";
        var result = TradeStatementSheetParser.Parse(ws, "f.xlsx");
        CollectionAssert.Contains(result.Flags, "COPY_SUSPECTED");
    }

    [TestMethod]
    public void Parse_DiscardedMarkerInSheetName_FlagsDiscarded()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("삼진그린푸드 가라");
        ws.Cells[11, 1].Value = "품목";
        var result = TradeStatementSheetParser.Parse(ws, "f.xlsx");
        CollectionAssert.Contains(result.Flags, "DISCARDED");
    }

    [TestMethod]
    public void Parse_BlankSheet1_FlagsNoiseAndDoesNotThrow()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("Sheet1");
        ws.Cells[15, 7].Value = "메모"; // 실측처럼 빈 시트에 잡다한 셀 하나만 있는 상태(G15:K20 유사)

        var result = TradeStatementSheetParser.Parse(ws, "f.xlsx");

        CollectionAssert.Contains(result.Flags, "NOISE_BLANK_SHEET_NAME");
    }

    [TestMethod]
    public void Parse_NoItemLabelAnywhere_FlagsNoiseNoHeaderWithoutThrowing()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("완전빈시트");
        ws.Cells[1, 1].Value = "아무거나";

        var result = TradeStatementSheetParser.Parse(ws, "f.xlsx");

        CollectionAssert.Contains(result.Flags, "NOISE_NO_HEADER");
        Assert.AreEqual(0, result.Lines.Count);
    }

    // ── 총계행 대조 & 연속 공백행 가드(§3.8, DataLoaders/SettlementLoader.cs와 동일한 200행 가드) ──

    [TestMethod]
    public void Parse_TotalsRowMismatch_FlagsTotalsMismatch()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("불일치");
        ws.Cells[11, 1].Value = "품목"; ws.Cells[11, 2].Value = "수량"; ws.Cells[11, 3].Value = "금액";
        ws.Cells[12, 1].Value = "품목A"; ws.Cells[12, 2].Value = 1; ws.Cells[12, 3].Value = 1100;
        ws.Cells[13, 1].Value = "총계"; ws.Cells[13, 3].Value = 9999; // 라인 합(1100)과 불일치

        var result = TradeStatementSheetParser.Parse(ws, "f.xlsx");

        CollectionAssert.Contains(result.Flags, "TOTALS_MISMATCH");
    }

    [TestMethod]
    public void Parse_ManyBlankTemplateRowsWithoutTotalsRow_StopsAtGuardWithoutHanging()
    {
        ExcelLicense.Ensure();
        using var pkg = new ExcelPackage();
        var ws = pkg.Workbook.Worksheets.Add("가드테스트");
        ws.Cells[11, 1].Value = "품목"; ws.Cells[11, 2].Value = "수량"; ws.Cells[11, 3].Value = "금액";
        ws.Cells[12, 1].Value = "품목A"; ws.Cells[12, 2].Value = 1; ws.Cells[12, 3].Value = 1100;
        for (int r = 13; r <= 13 + 250; r++) ws.Cells[r, 3].Value = "-"; // 실측처럼 대시 placeholder만 있는 빈 템플릿 행

        var result = TradeStatementSheetParser.Parse(ws, "f.xlsx");

        Assert.AreEqual(1, result.Lines.Count);
        CollectionAssert.Contains(result.Flags, "NO_TOTALS_ROW");
    }
}
