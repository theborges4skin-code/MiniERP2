using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class PartnerStatementReconcileEngineTests
{
    private static readonly (string, string)[] Known =
    [
        ("아이스버블 면도기세정액 BR타입 500ml 2개+세척용 컵", "투유_rz2set_BR"),
        ("아이스버블 면도기세정액 PH타입 500ml 2개+세척용 컵", "투유_rz2set_PH"),
        ("아이스버블 면도기세정액 BR타입 500ml 1개", "투유_rz500_BR"),
        ("아이스버블 면도기세정액 BR타입 500ml 4개", "투유_rz500x4_BR"),
        ("이공이공 핸드워시 4L 1개 베이비파우더향", "투유_igfoam4000_baby"),
        ("이공이공 핸드워시 4L 1개 머스캣향", "투유_igfoam4000_muscat"),
    ];

    [TestMethod]
    [DataRow("아이스버블 면도기세정액_500ml 2개 + 세척용 컵 [BR타입]", "투유_rz2set_BR")]
    [DataRow("아이스버블 면도기세정액_500ml 2개 + 세척용 컵 [PH타입]", "투유_rz2set_PH")]
    [DataRow("아이스버블 면도기 세정액_500ml [BR타입]", "투유_rz500_BR")]
    [DataRow("아이스버블 면도기세정액 500ml 4개 [BR타입]", "투유_rz500x4_BR")]
    [DataRow("이공이공4L [베이비파우더향]", "투유_igfoam4000_baby")]
    [DataRow("이공이공4L", null)] // 향이 없어 어느 4L인지 판단 불가 → 사용자가 연결
    public void GuessCsku_TuyuStatementNames(string partnerName, string? expected) =>
        Assert.AreEqual(expected, PartnerStatementReconcileEngine.GuessCsku(partnerName, Known));

    [TestMethod]
    public void Parse_TuyuLayout_ReadsLinesShippingAndVatIncludedPrice()
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var s = package.Workbook.Worksheets.Add("구매현황내역");
        s.Cells[1, 1].Value = "회사명 : 주식회사 투유 / 2026/09/01 ~ 2026/09/30";
        string[] headers = ["일자", "품명 및 규격", "수량", "단가", "공급가액", "부가세", "합 계", "구매처명", "적요"];
        for (int i = 0; i < headers.Length; i++) s.Cells[2, i + 1].Value = headers[i];
        object[][] rows =
        [
            ["09/01-1", "아이스버블 면도기세정액 500ml 4개 [BR타입]", 2, 14182, 28364, 2836, 31200, "(주)신안", "스스"],
            ["09/01-1", "배송비(3000)", 2, 2727, 5455, 545, 6000, "(주)신안", ""],
            ["09 계", "", 4, "", 33819, 3381, 37200, "", ""],
        ];
        for (int r = 0; r < rows.Length; r++)
            for (int c = 0; c < rows[r].Length; c++) s.Cells[3 + r, c + 1].Value = rows[r][c];

        var parsed = PartnerStatementReconcileEngine.Parse(s, "2026-09");

        Assert.IsNull(parsed.Error);
        Assert.HasCount(2, parsed.Lines);
        Assert.AreEqual(new DateTime(2026, 9, 1), parsed.Lines[0].Date);
        Assert.AreEqual(15600m, parsed.Lines[0].UnitPriceVatIncluded);
        Assert.IsTrue(parsed.Lines[1].IsShipping);
    }

    private static PartnerStatementLine P(string date, string csku, decimal qty, decimal unit = 5100m) => new()
    {
        Date = DateTime.Parse(date), ItemName = csku, CskuCode = csku, Qty = qty, UnitPriceVatIncluded = unit, AmountVatIncluded = unit * qty,
    };

    private static OutboundDetail D(long id, string date, string csku, int qty, decimal price = 5100m) => new()
    {
        Id = id, ConfirmedAt = DateTime.Parse(date), MskuCode = csku, Qty = qty, SupplyPrice = price, ShipmentGroupKey = $"S{id}", Recipient = $"R{id}",
    };

    [TestMethod]
    public void Reconcile_MatchesWithinWindow_PrefersNearestDate()
    {
        // 거래처 9/2 1개, 9/8 1개 / 우리 9/7 1개(9/2분을 몰아서 출고), 9/8 1개. 9/8 거래처 줄이 9/8 우리 줄과 짝져야 한다.
        var partner = new List<PartnerStatementLine> { P("2026-09-02", "A", 1), P("2026-09-08", "A", 1) };
        var ours = new List<OutboundDetail> { D(1, "2026-09-07", "A", 1), D(2, "2026-09-08", "A", 1) };

        var r = PartnerStatementReconcileEngine.Reconcile(partner, ours, _ => false);

        Assert.IsTrue(r.Rows.All(x => x.Kind == ReconcileRow.KindMatched));
        Assert.AreEqual(2L, r.Rows.Single(x => x.PartnerDateText == "09-08").OutboundDetailId);
    }

    [TestMethod]
    public void Reconcile_ReportsPartnerOnlyOursOnlyPriceDiffAndUnmapped()
    {
        var partner = new List<PartnerStatementLine>
        {
            P("2026-09-01", "A", 2),
            P("2026-09-03", "B", 1, unit: 9000m),
            new() { Date = new DateTime(2026, 9, 4), ItemName = "모르는 품명", Qty = 1, UnitPriceVatIncluded = 100m, AmountVatIncluded = 100m },
            new() { Date = new DateTime(2026, 9, 4), ItemName = "배송비(3000)", Qty = 3, IsShipping = true, AmountVatIncluded = 9000m },
        };
        var ours = new List<OutboundDetail>
        {
            D(1, "2026-09-01", "A", 1),
            D(2, "2026-09-03", "B", 1, price: 9600m),
            D(3, "2026-09-20", "C", 1),
            D(4, "2026-09-20", "SHIP", 2),
        };

        var r = PartnerStatementReconcileEngine.Reconcile(partner, ours, c => c == "SHIP");

        Assert.AreEqual(1m, r.Rows.Single(x => x.Kind == ReconcileRow.KindPartnerOnly).Qty);
        Assert.AreEqual("C", r.Rows.Single(x => x.Kind == ReconcileRow.KindOursOnly).CskuCode);
        Assert.AreEqual("B", r.Rows.Single(x => x.Kind == ReconcileRow.KindPriceDiff).CskuCode);
        Assert.AreEqual("모르는 품명", r.Rows.Single(x => x.Kind == ReconcileRow.KindUnmapped).PartnerItemName);
        Assert.AreEqual(3m, r.PartnerShippingCount);
        Assert.AreEqual(2m, r.OurShippingLineQty);
        Assert.AreEqual(3, r.OurShipmentCount);
    }
}
