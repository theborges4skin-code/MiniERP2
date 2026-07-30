using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerClosingDocumentBuilderTests
{
    private static PartnerClosingSummary Summary() => new()
    {
        Period = "2026-07",
        PartyKey = "CH:CH01",
        PartyName = "테스트채널",
        Lines =
        [
            new PartnerClosingLine { LineDate = new DateTime(2026, 7, 10), ItemName = "상품A", Qty = 2, UnitPrice = 10000m, CostPrice = 4000m, Profit = 12000m },
            new PartnerClosingLine { LineDate = new DateTime(2026, 7, 20), ItemName = "상품B", Qty = 1, UnitPrice = 5000m, CostPrice = 2000m, Profit = 3000m },
        ],
    };

    [TestMethod]
    public void BuildTradeStatement_MapsLinesAndComputesVatExcludedTotal()
    {
        var supplier = new DocParty { CompanyName = "공급자" };
        var buyer = new DocParty { CompanyName = "매입자" };

        var doc = PartnerClosingDocumentBuilder.BuildTradeStatement(Summary(), DocType.TradeStatementVatExcl, supplier, buyer);

        Assert.HasCount(2, doc.Lines);
        Assert.AreEqual(25000m, doc.TotalSupply); // 2*10000 + 1*5000
        Assert.AreEqual(2500m, doc.TotalTax);     // 10% VAT excl
        Assert.AreEqual(27500m, doc.GrandTotal);
        Assert.AreEqual(7, doc.Lines[0].Month);
        Assert.AreEqual(10, doc.Lines[0].Day);
    }

    [TestMethod]
    public void BuildSalesLedger_ComputesProfitFromCostPrice()
    {
        var supplier = new DocParty { CompanyName = "공급자" };
        var buyer = new DocParty { CompanyName = "매입자" };

        var doc = PartnerClosingDocumentBuilder.BuildSalesLedger(Summary(), supplier, buyer);

        Assert.AreEqual(25000m, doc.TotalSupply);
        Assert.AreEqual(2 * 4000m + 1 * 2000m, doc.TotalCost);
        Assert.AreEqual(doc.TotalSupply - doc.TotalCost, doc.TotalProfit);
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
