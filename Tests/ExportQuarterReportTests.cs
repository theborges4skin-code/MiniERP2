using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class ExportQuarterReportTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        ExcelLicense.Ensure();
        _folder = Path.Combine(Path.GetTempPath(), "QuarterReportTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, recursive: true);

    private string WriteText(string name, params string[] lines)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new System.Text.UTF8Encoding(true));
        return path;
    }

    private string WriteDeclarations(string name, params (string No, string Amount, string Currency, string Date)[] rows)
    {
        var path = Path.Combine(_folder, name);
        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("수출신고실적목록조회");
        string[] headers = ["신고번호", "결제금액", "통화코드", "신고일자", "수리일자", "선적일자", "세관", "과"];
        for (var i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
        for (var r = 0; r < rows.Length; r++)
        {
            ws.Cells[r + 2, 1].Value = rows[r].No;
            ws.Cells[r + 2, 2].Value = rows[r].Amount;
            ws.Cells[r + 2, 3].Value = rows[r].Currency;
            ws.Cells[r + 2, 4].Value = rows[r].Date;
        }
        package.SaveAs(new FileInfo(path));
        return path;
    }

    private static readonly string[] AmazonCsv =
    [
        "\"Includes Amazon Marketplace, Fulfillment by Amazon (FBA), and Amazon Webstore transactions\"",
        "\"All amounts in USD, unless specified\"",
        "\"date/time\",\"type\",\"order id\",\"description\",\"product sales\",\"shipping credits\",\"gift wrap credits\",\"promotional rebates\",\"other\",\"total\"",
        "\"Jun 1, 2026 12:49:34 AM PDT\",\"Order\",\"1\",\"Serum\",\"100.00\",\"5.00\",\"0\",\"-10.00\",\"0\",\"80.00\"",
        "\"Jun 2, 2026 1:00:00 PM PDT\",\"Refund\",\"2\",\"Serum\",\"-20.00\",\"0\",\"0\",\"2.00\",\"1.80\",\"-13.20\"",
        "\"Jun 3, 2026 1:00:00 PM PDT\",\"Adjustment\",\"\",\"FBA Inventory Reimbursement - Customer Return\",\"0\",\"0\",\"0\",\"0\",\"17.56\",\"17.56\"",
        "\"Jun 4, 2026 1:00:00 PM PDT\",\"Adjustment\",\"\",\"FBA Inventory Reimbursement - General Adjustment\",\"0\",\"0\",\"0\",\"0\",\"-2.39\",\"-2.39\"",
        "\"Jun 5, 2026 1:00:00 PM PDT\",\"Transfer\",\"\",\"To account ending in: 123\",\"0\",\"0\",\"0\",\"0\",\"-500.00\",\"-500.00\"",
        "\"Jul 1, 2026 2:00:00 AM PDT\",\"Order\",\"3\",\"Serum\",\"50.00\",\"0\",\"0\",\"0\",\"0\",\"40.00\"",
    ];

    private static readonly string[] PaypalCsv =
    [
        "\"Date\",\"Time\",\"Type\",\"Status\",\"Currency\",\"Gross\",\"Fee\",\"Net\"",
        "\"2026/09/27\",\"19:00:24\",\"Express Checkout Payment\",\"Completed\",\"USD\",\"93.00\",\"-4.39\",\"88.61\"",
    ];

    /// <summary>요약 PDF Income 합계와 같은 규칙: 주문·환불 상품/배송/프로모션 + 양수 재고보상만(음수 조정·이체 제외), 게시월별.</summary>
    [TestMethod]
    public void LoadAmazonTransactions_ComputesIncomeLikeSummaryPdf()
    {
        var path = WriteText("2026Jun1-2026Jul31CustomUnifiedTransaction.csv", AmazonCsv);

        var months = ExportQuarterSourceLoader.LoadAmazonTransactions(path, new AmazonTransactionTrackConfig());

        Assert.HasCount(2, months);
        var june = months.Single(m => m.YearMonth == "2026-06");
        Assert.AreEqual("US", june.MarketCode);
        Assert.AreEqual("USD", june.Currency);
        Assert.AreEqual(17.56m, june.Reimbursement);
        Assert.AreEqual(100m + 5m - 10m - 20m + 2m + 1.80m + 17.56m, june.Income); // 환불 행 other(TCS 환급)도 Income
        Assert.AreEqual(50m, months.Single(m => m.YearMonth == "2026-07").Income);
    }

    private static readonly string[] AmazonJpCsv =
    [
        "\"Amazon 出品サービス、フルフィルメント by Amazon (FBA)\"",
        "\"指定のない場合、単位は円\"",
        "\"日付/時間\",\"トランザクションの種類\",\"注文番号\",\"説明\",\"商品売上\",\"商品の売上税\",\"配送料\",\"ギフト包装手数料\",\"Amazonポイントの費用\",\"プロモーション割引額\",\"手数料\",\"その他\",\"合計\"",
        "\"2026/07/01 12:23:29 JST\",\"注文\",\"1\",\"Serum\",\"3,546\",\"354\",\"418\",\"0\",\"-39\",\"-418\",\"-442\",\"0\",\"3,004\"",
        "\"2026/07/03 7:31:07 JST\",\"振込み\",\"\",\"末尾が668のアカウントへ\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"-34,702\",\"-34,702\"",
        "\"2026/07/15 20:48:39 JST\",\"返金\",\"2\",\"Serum\",\"-2,273\",\"-227\",\"-140\",\"0\",\"25\",\"140\",\"254\",\"0\",\"-2,221\"",
        "\"2026/07/15 22:48:47 JST\",\"注文外料金\",\"\",\"月額登録料\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"0\",\"-3,072\",\"-3,072\"",
    ];

    /// <summary>아마존 일본 거래내역(일본어 헤더·円): 소비세 제외, 포인트 비용 포함 — 월간 요약 PDF "収入 小計" 규칙.</summary>
    [TestMethod]
    public void LoadAmazonTransactions_JapaneseLayout_ComputesIncomeInYen()
    {
        var path = WriteText("2026Jul1-2026Sep30CustomTransaction.csv", AmazonJpCsv);

        Assert.AreEqual(ExportQuarterSourceLoader.SourceKind.AmazonTransaction, ExportQuarterSourceLoader.DetectSourceKind(path, new ExportSummaryConfig()));
        var july = ExportQuarterSourceLoader.LoadAmazonTransactions(path, new AmazonTransactionTrackConfig()).Single();

        Assert.AreEqual("2026-07", july.YearMonth);
        Assert.AreEqual("JP", july.MarketCode);
        Assert.AreEqual("JPY", july.Currency);
        Assert.AreEqual(3546m + 418m - 39m - 418m - 2273m - 140m + 25m + 140m, july.Income);
    }

    [TestMethod]
    public void DetectSourceKind_RecognizesAllSourcesInCsvAndExcel()
    {
        var config = new ExportSummaryConfig();
        var amazon = WriteText("a.csv", AmazonCsv);
        var paypal = WriteText("b.csv", PaypalCsv);
        var payoneer = WriteText("c.csv", "Date,Description,Amount,Currency,Status,Transaction ID", "\"30 Sep, 2026\",Payment from Shopee VN,14.92,USD,Completed,1");
        var wix = WriteText("d.csv", "Order number,Date created,Total,Payment status", "10069,\"Sep 30, 2026\",62.00,Paid");
        var wixPayments = WriteText("e.csv", "Date,Payment status,Payment type,Total revenue", "5/29/2026,Paid,Regular Payment,$25.00");

        // 페이오니어 엑셀(머리말 줄이 있어도 헤더 행을 찾는다)
        var payoneerXlsx = Path.Combine(_folder, "payoneer.xlsx");
        using (var package = new ExcelPackage())
        {
            var ws = package.Workbook.Worksheets.Add("Transactions");
            ws.Cells[1, 1].Value = "Transactions report";
            string[] h = ["Date", "Description", "Amount", "Currency", "Status"];
            for (var i = 0; i < h.Length; i++) ws.Cells[3, i + 1].Value = h[i];
            ws.Cells[4, 1].Value = "30 Sep, 2026";
            ws.Cells[4, 2].Value = "Payment from Shopee VN";
            ws.Cells[4, 3].Value = 14.92;
            ws.Cells[4, 4].Value = "USD";
            package.SaveAs(new FileInfo(payoneerXlsx));
        }

        Assert.AreEqual(ExportQuarterSourceLoader.SourceKind.AmazonTransaction, ExportQuarterSourceLoader.DetectSourceKind(amazon, config));
        Assert.AreEqual(ExportQuarterSourceLoader.SourceKind.Paypal, ExportQuarterSourceLoader.DetectSourceKind(paypal, config));
        Assert.AreEqual(ExportQuarterSourceLoader.SourceKind.Payoneer, ExportQuarterSourceLoader.DetectSourceKind(payoneer, config));
        Assert.AreEqual(ExportQuarterSourceLoader.SourceKind.WixOrders, ExportQuarterSourceLoader.DetectSourceKind(wix, config));
        Assert.AreEqual(ExportQuarterSourceLoader.SourceKind.Unknown, ExportQuarterSourceLoader.DetectSourceKind(wixPayments, config));
        Assert.AreEqual(ExportQuarterSourceLoader.SourceKind.Payoneer, ExportQuarterSourceLoader.DetectSourceKind(payoneerXlsx, config));

        var table = ExportQuarterSourceLoader.ReadSourceTable(payoneerXlsx, ExportQuarterSourceLoader.SourceKind.Payoneer, config);
        var rows = ExportQuarterSourceLoader.LoadPayoneer(table, new ExportSummaryConfigService(Path.Combine(_folder, "cfg.json")).Load().RemittanceTrack);
        Assert.AreEqual("VN", rows.Single().MarketCode);
        Assert.AreEqual(14.92m, rows.Single().Amount);
    }

    /// <summary>폴더 하나로: 신고(통화→마켓, USD=아마존 미국, 중복 신고번호 1회), 페이오니어(통화 컬럼·이체 제외), PayPal, Wix 대조, 아마존 CSV, 분기 경계.</summary>
    [TestMethod]
    public void Build_AggregatesFolderIntoQuarterTidy()
    {
        WriteDeclarations("수출신고실적목록조회_202607.xlsx",
            ("D1", "100", "SGD", "2026-07-03"), ("D2", "2,750", "USD", "2026-07-03"));
        WriteDeclarations("수출신고_사본.xlsx", ("D1", "100", "SGD", "2026-07-03")); // 같은 신고번호 → 한 번만
        WriteText("report_payoneer.csv",
            "Date,Description,Amount,Currency,Status,Transaction ID",
            "\"30 Sep, 2026\",Payment from Shopee VN,14.92,USD,Completed,1",
            "\"29 Sep, 2026\",\"Withdrawal to Woori (5178)\",\"-7,000.00\",USD,Completed,2",
            "\"28 Sep, 2026\",Payment from Amazon - XX-770668,\"50,019\",JPY,Completed,3",
            "\"1 Oct, 2026\",Payment from Shopee VN,9.00,USD,Completed,4");
        WriteText("Download_paypal.CSV", PaypalCsv);
        WriteText("Orders_wix.csv",
            "Order number,Date created,Time,Total,Currency,Payment status",
            "10069,\"Sep 30, 2026\",1:00:00 AM,62.00,USD,Paid",
            "10070,\"Sep 30, 2026\",9:01:06 AM,40.00,USD,Paid",   // 미국 9/30 오전 = 한국 10/1 → 다음 분기
            "10068,\"Sep 27, 2026\",2:57:55 AM,93.00,USD,Paid",
            "10068,\"Sep 27, 2026\",2:57:55 AM,93.00,USD,Paid");
        WriteText("2026Jun1-2026Jul31CustomUnifiedTransaction.csv", AmazonCsv);

        var config = new ExportSummaryConfigService(Path.Combine(_folder, "export_summary_config.json")).Load(); // 파일 없으면 기본값
        var report = ExportQuarterReportBuilder.Build(_folder, config);

        Assert.AreEqual(new DateTime(2026, 7, 1), report.PeriodStart);
        Assert.AreEqual(new DateTime(2026, 10, 1), report.PeriodEndExclusive);

        decimal Amount(string market, string ind, string cur) =>
            report.Tidy.Where(t => t.MarketCode == market && t.Indicator == ind && t.Currency == cur).Sum(t => t.Amount);

        Assert.AreEqual(100m, Amount("SG", ExportQuarterReportBuilder.Declared, "SGD"));
        Assert.AreEqual(2750m, Amount("US", ExportQuarterReportBuilder.Declared, "USD"));
        Assert.AreEqual(14.92m, Amount("VN", ExportQuarterReportBuilder.Settled, "USD"));      // 10월분 제외
        Assert.AreEqual(50019m, Amount("JP", ExportQuarterReportBuilder.Settled, "JPY"));      // 통화 컬럼 반영
        Assert.AreEqual(93m, Amount("OWN_US", ExportQuarterReportBuilder.Sales, "USD"));
        Assert.AreEqual(88.61m, Amount("OWN_US", ExportQuarterReportBuilder.Settled, "USD"));
        Assert.AreEqual(50m, Amount("US", ExportQuarterReportBuilder.Sales, "USD"));            // 6월분은 기간 밖
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("중복된 수출신고 1건")));
        Assert.IsTrue(report.Warnings.Any(w => w.StartsWith("수출신고 내역이 없는 달: 2026-08, 2026-09")));
        // Wix 10068(93)은 PayPal과 짝, 10069(62)는 PayPal에 없음, 10070은 한국시간으로 10월이라 다음 분기 안내
        var wixWarnings = report.Warnings.Where(w => w.StartsWith("Wix 주문")).ToList();
        Assert.HasCount(2, wixWarnings);
        Assert.IsTrue(wixWarnings.Any(w => w.Contains("#10069") && w.Contains("PayPal 내역에 없음")));
        Assert.IsTrue(wixWarnings.Any(w => w.Contains("#10070") && w.Contains("한국 10-01 01:01") && w.Contains("다음 분기")));

        using var package = new ExcelPackage();
        ExportQuarterReportExcelWriter.Write(package, report, config.Markets.ToDictionary(m => m.MarketCode, m => m.MarketName));
        Assert.IsNotNull(package.Workbook.Worksheets["전체 요약 26q3"]);
        Assert.IsNotNull(package.Workbook.Worksheets["자사몰주문(Wix)"]);
        Assert.IsNotNull(package.Workbook.Worksheets["신고대사_요약"]);
    }
}
