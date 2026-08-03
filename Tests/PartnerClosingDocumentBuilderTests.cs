using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerClosingDocumentBuilderTests
{
    // OutboundDetail.SupplyPrice(→UnitPrice)는 VAT포함 단가로 저장된다(2026-07-30 확인).
    // CSKU "A"는 7/1에 두 건(1개+3개), 7/2에 한 건(2개)으로 나뉘어 있고, CSKU "B"는 7/1에 1건.
    private static PartnerClosingSummary Summary() => new()
    {
        Period = "2026-07",
        PartyKey = "CH:CH01",
        PartyName = "테스트채널",
        Lines =
        [
            new PartnerClosingLine { LineDate = new DateTime(2026, 7, 1), CskuCode = "A", ItemName = "상품A", Qty = 1, UnitPrice = 11000m, CostPrice = 2200m },
            new PartnerClosingLine { LineDate = new DateTime(2026, 7, 1), CskuCode = "A", ItemName = "상품A", Qty = 3, UnitPrice = 11000m, CostPrice = 2200m },
            new PartnerClosingLine { LineDate = new DateTime(2026, 7, 2), CskuCode = "A", ItemName = "상품A", Qty = 2, UnitPrice = 11000m, CostPrice = 2200m },
            new PartnerClosingLine { LineDate = new DateTime(2026, 7, 1), CskuCode = "B", ItemName = "상품B", Qty = 1, UnitPrice = 5500m, CostPrice = 1100m },
        ],
    };

    [TestMethod]
    public void BuildTradeStatement_VatExcl_MergesSameDateCskuAndDividesOutVat()
    {
        var supplier = new DocParty { CompanyName = "공급자" };
        var buyer = new DocParty { CompanyName = "매입자" };

        var doc = PartnerClosingDocumentBuilder.BuildTradeStatement(Summary(), DocType.TradeStatementVatExcl, supplier, buyer);

        // 7/1 A(1+3=4개), 7/2 A(2개), 7/1 B(1개) — 날짜가 다른 7/2 A는 합쳐지지 않고 별도 줄.
        Assert.HasCount(3, doc.Lines);

        var jul1A = doc.Lines.Single(l => l.Day == 1 && l.Qty == 4);
        Assert.AreEqual(10000m, jul1A.UnitPrice); // 11000 / 1.1
        Assert.AreEqual(40000m, jul1A.SupplyAmount);

        var jul2A = doc.Lines.Single(l => l.Day == 2);
        Assert.AreEqual(2m, jul2A.Qty);
        Assert.AreEqual(10000m, jul2A.UnitPrice);

        // VAT포함 원래 총액(4*11000+2*11000+1*5500=71500)과 공급가+세액이 정확히 일치해야 한다.
        Assert.AreEqual(65000m, doc.TotalSupply);
        Assert.AreEqual(6500m, doc.TotalTax);
        Assert.AreEqual(71500m, doc.GrandTotal);
    }

    [TestMethod]
    public void BuildTradeStatement_VatIncl_KeepsRawPriceWithoutDividing()
    {
        var supplier = new DocParty { CompanyName = "공급자" };
        var buyer = new DocParty { CompanyName = "매입자" };

        var doc = PartnerClosingDocumentBuilder.BuildTradeStatement(Summary(), DocType.TradeStatementVatIncl, supplier, buyer);

        var jul1A = doc.Lines.Single(l => l.Day == 1 && l.Qty == 4);
        Assert.AreEqual(11000m, jul1A.UnitPrice); // VAT포함이므로 나누지 않음
        Assert.AreEqual(71500m, doc.TotalSupply); // 이 문서유형은 TotalSupply 자체가 합계금액(VAT포함)
    }

    [TestMethod]
    public void BuildSalesLedger_VatExcludedDefault_GroupsByDateAndCskuAndDividesOutVat()
    {
        var supplier = new DocParty { CompanyName = "공급자" };
        var buyer = new DocParty { CompanyName = "매입자" };

        var doc = PartnerClosingDocumentBuilder.BuildSalesLedger(Summary(), supplier, buyer, ignoreDate: false);

        Assert.IsTrue(doc.IsVatExcluded);
        Assert.HasCount(3, doc.Lines);
        Assert.AreEqual(65000m, doc.TotalSupply);
        Assert.AreEqual(13000m, doc.TotalCost); // (4+2)*2000 + 1*1000
        Assert.AreEqual(52000m, doc.TotalProfit);
    }

    [TestMethod]
    public void BuildSalesLedger_VatIncluded_KeepsRawCskuPriceWithoutDividing()
    {
        // 사용자 요청: CSKU 납품단가(VAT포함)를 그대로 보고 싶을 때는 나누지 않아야 하고,
        // 그래도 합계금액은 원본 라인 합계(11000*4+11000*2+5500*1=71500)와 정확히 맞아야 한다.
        var supplier = new DocParty { CompanyName = "공급자" };
        var buyer = new DocParty { CompanyName = "매입자" };

        var doc = PartnerClosingDocumentBuilder.BuildSalesLedger(Summary(), supplier, buyer, ignoreDate: false, vatExcluded: false);

        Assert.IsFalse(doc.IsVatExcluded);
        var jul1A = doc.Lines.Single(l => l.Day == 1 && l.Qty == 4);
        Assert.AreEqual(11000m, jul1A.UnitPrice); // VAT포함이므로 나누지 않음
        Assert.AreEqual(2200m, jul1A.CostPrice);
        Assert.AreEqual(71500m, doc.TotalSupply);
        Assert.AreEqual(14300m, doc.TotalCost); // (4+2)*2200 + 1*1100
        Assert.AreEqual(57200m, doc.TotalProfit);
    }

    [TestMethod]
    public void BuildSalesLedger_IgnoreDate_MergesAcrossDatesButKeepsSameTotals()
    {
        var supplier = new DocParty { CompanyName = "공급자" };
        var buyer = new DocParty { CompanyName = "매입자" };

        var doc = PartnerClosingDocumentBuilder.BuildSalesLedger(Summary(), supplier, buyer, ignoreDate: true);

        // CSKU "A"(7/1+7/2 통합 6개), "B"(1개) 두 줄로만 합산되고, 날짜 칸은 비워야 한다(일=0).
        Assert.HasCount(2, doc.Lines);
        var lineA = doc.Lines.Single(l => l.Qty == 6);
        Assert.AreEqual(0, lineA.Day);
        Assert.AreEqual(7, lineA.Month);
        Assert.AreEqual(10000m, lineA.UnitPrice);

        // 합산 단위만 바뀔 뿐 총액은 날짜별 합산과 같아야 한다.
        Assert.AreEqual(65000m, doc.TotalSupply);
        Assert.AreEqual(52000m, doc.TotalProfit);
    }

    [TestMethod]
    public void DefaultFileName_SanitizesInvalidCharactersAndIncludesPeriod()
    {
        var summary = Summary();
        summary.PartyName = "A/B:거래처";

        var name = PartnerClosingDocumentBuilder.DefaultFileName(summary, "거래명세표");

        Assert.AreEqual("A_B_거래처_2026-07_거래명세표.xlsx", name);
    }
}
