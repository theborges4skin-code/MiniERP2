using MiniERP2.DataLoaders;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.Utils;
using OfficeOpenXml;
using static MiniERP2.DataLoaders.ExportSummaryLoader;

namespace MiniERP2.Tests;

[TestClass]
public class ExportDeclarationReconcileEngineTests
{
    private static readonly DateTime Q3Start = new(2026, 7, 1);
    private static readonly DateTime Q3End = new(2026, 10, 1);
    private static readonly Dictionary<string, decimal> NoConfiguredRates = new();

    private static DeclarationRow Decl(string date, decimal amount, string currency = "PHP", string no = "")
        => new(no, DateTime.Parse(date), currency, amount, "decl.xlsx");

    private static SalesOrderRecord Order(string id, string shipped, string completed, decimal paid, decimal sellerBase, string currency = "PHP")
        => new(id, "PH", currency, "orders.xlsx", DateTime.Parse(completed), DateTime.Parse(shipped), paid, sellerBase, "sku×1");

    /// <summary>그날 신고 1건·주문 1건인 날 3개 이상 → 비율 중앙값을 k로 쓴다.</summary>
    [TestMethod]
    public void Run_EstimatesRateFromUnambiguousDays()
    {
        var decls = new[] { Decl("2026-07-02", 78m), Decl("2026-07-03", 156m), Decl("2026-07-06", 390m) };
        var orders = new[]
        {
            Order("A", "2026-07-02 10:00", "2026-07-10", 70m, 100m),
            Order("B", "2026-07-03 10:00", "2026-07-11", 150m, 200m),
            Order("C", "2026-07-06 10:00", "2026-07-12", 400m, 500m),
        };

        var result = ExportDeclarationReconcileEngine.Run(decls, orders, Q3Start, Q3End, NoConfiguredRates);

        var rate = result.Rates.Single();
        Assert.AreEqual(0.78m, rate.Rate);
        Assert.AreEqual(3, rate.SampleCount);
        Assert.HasCount(3, result.Matches);
        Assert.IsTrue(result.Matches.All(m => m.MatchType == "금액 일치"));
    }

    /// <summary>판매액(완료일 기준)에서 각 항목을 더하면 신고액이 정확히 나와야 한다(검산 0).</summary>
    [TestMethod]
    public void Run_BridgeAddsUpToDeclaredTotal()
    {
        var decls = new[]
        {
            Decl("2026-07-01", 612m),                       // 짝 없음(주문파일 이전)
            Decl("2026-07-02", 78m), Decl("2026-07-03", 156m), Decl("2026-07-06", 390m),
            Decl("2026-09-29", 78m),                        // 9월 출고·10월 완료 주문과 짝
        };
        var orders = new[]
        {
            Order("A", "2026-07-02 10:00", "2026-07-10", 70m, 100m),
            Order("B", "2026-07-03 10:00", "2026-07-11", 150m, 200m),
            Order("C", "2026-07-06 10:00", "2026-07-12", 400m, 500m),
            Order("D", "2026-09-29 10:00", "2026-10-05", 60m, 100m),
            Order("E", "2026-10-02 10:00", "2026-10-09", 90m, 100m), // 기간 이후 출고
        };

        var result = ExportDeclarationReconcileEngine.Run(decls, orders, Q3Start, Q3End, NoConfiguredRates);
        var s = result.Summaries.Single();

        Assert.AreEqual(620m, s.SalesCompleted);              // A+B+C (D·E는 10월 완료)
        Assert.AreEqual(60m, s.DeclaredButCompletedOutside);   // D
        Assert.AreEqual(612m, s.UnmatchedDeclarations);
        Assert.AreEqual(1314m, s.Declared);
        Assert.AreEqual(0m, s.BridgeCheck);
        Assert.AreEqual("기간 이후 출고 — 다음 분기 신고분", result.UnmatchedOrders.Single().Reason);
        StringAssert.StartsWith(result.UnmatchedDeclarations.Single().Reason, "주문파일 기간 이전");
    }

    /// <summary>신고번호가 비어 같은 날 같은 금액 신고가 record 값으로 같아도 각각 따로 짝지어야 한다.</summary>
    [TestMethod]
    public void Run_IdenticalSameDayDeclarations_EachMatchedSeparately()
    {
        var decls = new[] { Decl("2026-07-06", 454m), Decl("2026-07-06", 454m), Decl("2026-07-06", 454m) };
        var orders = new[]
        {
            Order("A", "2026-07-06 09:45", "2026-07-12", 465m, 580m),
            Order("B", "2026-07-06 09:45", "2026-07-12", 503m, 580m),
            Order("C", "2026-07-06 09:45", "2026-07-12", 480m, 580m),
        };

        var result = ExportDeclarationReconcileEngine.Run(decls, orders, Q3Start, Q3End,
            new Dictionary<string, decimal> { ["PHP"] = 0.78m });

        Assert.HasCount(3, result.Matches);
        Assert.IsEmpty(result.UnmatchedDeclarations);
        Assert.IsEmpty(result.UnmatchedOrders);
        Assert.AreEqual("설정값(표본 부족)", result.Rates.Single().Source);
    }

    /// <summary>금요일 신고·다음 주 월요일 출고처리처럼 신고가 출고보다 먼저인 경우도 짝짓는다.</summary>
    [TestMethod]
    public void Run_DeclarationBeforeShipOverWeekend_Matched()
    {
        var decls = new[] { Decl("2026-07-31", 448m) };
        var orders = new[] { Order("A", "2026-08-03 13:06", "2026-08-09", 490m, 580m) };

        var result = ExportDeclarationReconcileEngine.Run(decls, orders, Q3Start, Q3End,
            new Dictionary<string, decimal> { ["PHP"] = 0.78m });

        Assert.HasCount(1, result.Matches);
    }

    /// <summary>판매 주문이 없는 통화(USD 등)는 대사하지 않고 신고액만 요약에 남긴다.</summary>
    [TestMethod]
    public void Run_CurrencyWithoutOrders_ReportedAsUnmatchedWithReason()
    {
        var decls = new[] { Decl("2026-07-10", 1000m, "USD"), Decl("2026-07-02", 78m) };
        var orders = new[] { Order("A", "2026-07-02 10:00", "2026-07-10", 70m, 100m) };

        var result = ExportDeclarationReconcileEngine.Run(decls, orders, Q3Start, Q3End,
            new Dictionary<string, decimal> { ["PHP"] = 0.78m });

        var usd = result.Summaries.Single(s => s.Currency == "USD");
        Assert.AreEqual(1000m, usd.Declared);
        Assert.AreEqual(0m, usd.BridgeCheck);
        StringAssert.StartsWith(result.UnmatchedDeclarations.Single().Reason, "판매 주문 데이터가 없는 통화");
    }

    [TestMethod]
    public void InferPeriod_SpansDeclarationMonths()
    {
        var (start, end) = ExportDeclarationReconcileEngine.InferPeriod(new[] { Decl("2026-08-15", 1m), Decl("2026-07-01", 1m), Decl("2026-09-30", 1m) });
        Assert.AreEqual(Q3Start, start);
        Assert.AreEqual(Q3End, end);
    }

    /// <summary>신고가 분기 일부 달에만 있어도 기간은 부가세 신고 단위인 분기 전체.</summary>
    [TestMethod]
    public void InferPeriod_RoundsToCalendarQuarter()
    {
        var (start, end) = ExportDeclarationReconcileEngine.InferPeriod(new[] { Decl("2026-07-15", 1m), Decl("2026-08-20", 1m) });
        Assert.AreEqual(Q3Start, start);
        Assert.AreEqual(Q3End, end);
    }

    /// <summary>판매 파일의 주문 단위 읽기: 여러 줄 주문은 1건, 주문 단위 금액·판매자쿠폰은 한 번만, 출고시각 시차 보정.</summary>
    [TestMethod]
    public void LoadSalesOrders_AggregatesLinesAndAppliesOffset()
    {
        ExcelLicense.Ensure();
        var folder = Path.Combine(Path.GetTempPath(), "ReconTests_" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "Order.completed.sg20260701_20260731.xlsx");
            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("orders");
                string[] headers = ["Order ID", "Ship Time", "Order Complete Time", "SKU Reference No.", "Deal Price", "Quantity", "Discount Voucher Amount Sponsored by Seller", "Grand Total", "Buyer Paid Shipping Fee"];
                for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
                object[][] rows =
                [
                    ["O1", "2026-07-05 23:30", "2026-07-12 10:00", "dp15", "13.18", "1", "15.82", "23.72", "0.00"],
                    ["O1", "2026-07-05 23:30", "2026-07-12 10:00", "mx15", "26.36", "1", "15.82", "23.72", "0.00"],
                    ["O2", "2026-07-06 10:00", "2026-07-13 10:00", "dp50", "28.33", "2", "11.34", "46.00", "1.99"],
                ];
                for (var r = 0; r < rows.Length; r++)
                    for (var c = 0; c < rows[r].Length; c++) ws.Cells[r + 2, c + 1].Value = rows[r][c];
                package.SaveAs(new FileInfo(path));
            }

            var mapping = new SalesMarketMapping
            {
                MarketCode = "SG", DateColumn = "Order Complete Time", AmountColumn = "Grand Total",
                OrderIdColumn = "Order ID", SubtractColumns = ["Buyer Paid Shipping Fee"],
                Recon = new SalesReconMapping
                {
                    ShipDateColumn = "Ship Time", ShipDateOffsetHours = 1, UnitPriceColumn = "Deal Price",
                    QuantityColumn = "Quantity", SellerVoucherColumn = "Discount Voucher Amount Sponsored by Seller", SkuColumn = "SKU Reference No.",
                },
            };

            var orders = ExportSummaryLoader.LoadSalesOrders(path, mapping, "SGD");

            Assert.HasCount(2, orders);
            var o1 = orders.Single(o => o.OrderId == "O1");
            Assert.AreEqual(23.72m, o1.PaidAmount);
            Assert.AreEqual(13.18m + 26.36m - 15.82m, o1.SellerBaseAmount);
            Assert.AreEqual(new DateTime(2026, 7, 6, 0, 30, 0), o1.ShippedAt); // 23:30 + 1시간 → 다음 날
            Assert.AreEqual("dp15×1, mx15×1", o1.Skus);
            var o2 = orders.Single(o => o.OrderId == "O2");
            Assert.AreEqual(46.00m - 1.99m, o2.PaidAmount);
            Assert.AreEqual(28.33m * 2 - 11.34m, o2.SellerBaseAmount);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
