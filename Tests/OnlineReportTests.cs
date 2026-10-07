using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Exporters;
using MiniERP2.Models;
using MiniERP2.Services;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

[TestClass]
public class OnlineReportTests
{
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static ProfitFactRow Fact(string channel, string group, int qty, decimal revenue, decimal profit, decimal shipping = 0, string? name = null) =>
        new() { ChannelCode = channel, ChannelName = name ?? channel, ProductGroup = group, Qty = qty, Revenue = revenue, GrossProfit = profit, ShippingFee = shipping };

    private static AdFactRow Ad(string channel, string group, decimal cost) =>
        new() { ChannelCode = channel, ChannelName = channel, ProductGroup = group, AdCost = cost };

    /// <summary>2026년 9월 실제 운송장이력 파일 2개를 운송장번호 있는 행만 센 결과.</summary>
    private static List<OnlineReportFreightTier> SeptemberTiers() =>
    [
        new() { Rate = 2450, Count = 1338 }, new() { Rate = 2790, Count = 308 }, new() { Rate = 3050, Count = 5 },
        new() { Rate = 3870, Count = 28 }, new() { Rate = 4650, Count = 369 }, new() { Rate = 5450, Count = 4 },
        new() { Rate = 5790, Count = 1 }, new() { Rate = 7650, Count = 4 },
    ];

    private static OnlineReportBuilder.Result Build(
        IReadOnlyList<ProfitFactRow> facts,
        IReadOnlyList<AdFactRow>? ads = null,
        OnlineReportMonthInput? month = null,
        IReadOnlyList<ProfitFactRow>? previous = null,
        IReadOnlyList<OnlineReportPartnerClosing>? partners = null,
        IReadOnlyList<OnlineReportCskuRow>? csku = null,
        IReadOnlyList<OnlineReportCskuRow>? previousCsku = null,
        OnlineReportConfig? config = null) =>
        OnlineReportBuilder.Build("2026-09", config ?? OnlineReportConfigService.CreateDefault(), month ?? new OnlineReportMonthInput(),
            facts, ads ?? [], previous ?? [], partners ?? [], csku ?? [], previousCsku ?? []);

    // ── 품목 열 매핑 ──

    [TestMethod]
    public void ResolveColumnIndex_MergesFragranceIntoDbAndPgIntoSurfactant()
    {
        var columns = OnlineReportConfigService.CreateDefault().GroupColumns;
        var db = columns.FindIndex(c => c.Number == 10);
        var surfactant = columns.FindIndex(c => c.Number == 19);

        Assert.AreEqual(db, OnlineReportBuilder.ResolveColumnIndex(columns, "08.캔디"));
        Assert.AreEqual(db, OnlineReportBuilder.ResolveColumnIndex(columns, "10.DB"));
        Assert.AreEqual(surfactant, OnlineReportBuilder.ResolveColumnIndex(columns, "04.PG"));
        Assert.AreEqual(columns.FindIndex(c => c.Number == 14), OnlineReportBuilder.ResolveColumnIndex(columns, "14. 면도"));
        Assert.AreEqual(columns.FindIndex(c => c.Number == 23), OnlineReportBuilder.ResolveColumnIndex(columns, "23.옵시디앙"));
        Assert.AreEqual(-1, OnlineReportBuilder.ResolveColumnIndex(columns, "41.아마존미국"));
        Assert.AreEqual(-1, OnlineReportBuilder.ResolveColumnIndex(columns, "(미지정)"));
    }

    [TestMethod]
    public void Build_UnknownGroupsGoToFallbackColumnWithWarning()
    {
        var report = Build([Fact("CH074", "41.아마존미국", 1, 15000, 9359), Fact("1F80429D", "(미지정)", 1, 15000, 7633)]);
        var fallback = report.Columns.ToList().FindIndex(c => c.Number == 22);

        Assert.AreEqual(15000m, report.Blocks.Single(b => b.Block.Name == "기타").Cells[fallback].Revenue);
        Assert.AreEqual(15000m, report.Blocks.Single(b => b.Block.Name == "스마트스토어").Cells[fallback].Revenue);
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("41.아마존미국") && w.Contains("(미지정)")));
    }

    // ── 블록 집계 ──

    [TestMethod]
    public void Build_SumsChannelsIntoBlocksWithShippingCount()
    {
        var report = Build(
        [
            Fact("9A2C5962", "14.면도", 45, 656000, 403505, shipping: 48000),
            Fact("275B7A29", "14.면도", 25, 259700, 143366, shipping: 54000),
            Fact("CH004", "14.면도", 2542, 58043959, 26816863, shipping: 6199902),
        ],
        [Ad("275B7A29", "14.면도", 69432), Ad("CH004", "14.면도", 11083561)]);

        var etc = report.Blocks.Single(b => b.Block.Name == "기타");
        var shave = report.Columns.ToList().FindIndex(c => c.Number == 14);
        Assert.AreEqual(70m, etc.Cells[shave].Qty);
        Assert.AreEqual(915700m, etc.Cells[shave].Revenue);
        Assert.AreEqual(69432m, etc.Cells[shave].AdCost);
        Assert.AreEqual(34m, etc.ShippingCount); // (48,000 + 54,000) ÷ 3,000
        Assert.AreEqual(102000m, etc.ShippingRevenue);
        Assert.AreEqual(915700m + 102000m, etc.Revenue);

        // 그로스는 CFS 배송비라 택배비 열에 넣지 않는다.
        var growth = report.Blocks.Single(b => b.Block.Name == "쿠팡그로스");
        Assert.AreEqual(0m, growth.ShippingCount);
        Assert.AreEqual(58043959m, growth.Revenue);
    }

    [TestMethod]
    public void Build_ExtraAdCostGoesToFallbackColumnOfBlock()
    {
        var month = new OnlineReportMonthInput
        {
            ExtraAdCosts = [new() { BlockName = "쿠팡로켓", Amount = 671880, Memo = "밀크런" }, new() { BlockName = "쿠팡로켓", Amount = 701630, Memo = "CJ택배입고" }],
        };
        var report = Build([Fact("4D4A484A", "12.핸드", 156, 850980, 194111)], [Ad("4D4A484A", "12.핸드", 219829)], month);

        var rocket = report.Blocks.Single(b => b.Block.Name == "쿠팡로켓");
        Assert.AreEqual(219829m + 671880m + 701630m, rocket.AdCost);
        Assert.AreEqual(1373510m, rocket.Cells[report.Columns.ToList().FindIndex(c => c.Number == 22)].AdCost);
        Assert.AreEqual(2, rocket.ExtraAdNotes.Count);
    }

    [TestMethod]
    public void Build_WarnsMissingBlockDataAndUnassignedChannels_IgnoresPartnerChannels()
    {
        var report = Build(
        [
            Fact("1F80429D", "14.면도", 1, 1000, 500, shipping: 3000),
            Fact("ZZ9", "14.면도", 1, 1000, 500, name: "새채널"),
            Fact("CH069", "12.핸드", 1, 1000, 500, name: "이공그로스수동마감"),
            Fact("CH071", "12.핸드", 1, 1000, 500, name: "온_한결_스마트"),
        ]);

        Assert.IsTrue(report.Warnings.Any(w => w.Contains("쿠팡:") && w.Contains("데이터가 없습니다")));
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("새채널(ZZ9)")));
        Assert.IsFalse(report.Warnings.Any(w => w.Contains("이공그로스수동마감") || w.Contains("온_한결_스마트")));
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("오늘의집:") && w.Contains("데이터가 없습니다")));
    }

    [TestMethod]
    public void Build_WarnsWhenShippingFeeMissing()
    {
        var report = Build([Fact("1F80429D", "14.면도", 1, 1000, 500)]);
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("스마트스토어") && w.Contains("배송비 정보가 없습니다")));
    }

    // ── 실택배비 조정 ──

    [TestMethod]
    public void Build_FreightAdjustment_MatchesSeptemberHandCalculation()
    {
        var report = Build([], month: new OnlineReportMonthInput { FreightTiers = SeptemberTiers(), FulfillmentCount = 600 });

        // Σ(3,000 − 운임) = 135,930, 부자재비 = 1,651×400 + 406×1,100 = 1,107,000, 풀필 600×(3,000−4,000) = −600,000.
        Assert.AreEqual(135930m, report.FreightLines.Sum(l => l.BaseDiff));
        Assert.AreEqual(-1107000m, report.FreightLines.Sum(l => l.Packing));
        Assert.AreEqual(-600000m, report.FulfillmentSubtotal);
        Assert.AreEqual(-1571070m, report.FreightAdjustment);
        Assert.AreEqual(-1571070m, report.Total.ShippingNet);
        Assert.AreEqual(-400m, report.FreightLines.Single(l => l.Rate == 3050).Packing / 5); // 3,200 이하 = 400
        Assert.AreEqual(-1100m, report.FreightLines.Single(l => l.Rate == 3870).Packing / 28); // 3,200 초과 = 1,100
    }

    [TestMethod]
    public void Build_FreightOverrideWins()
    {
        var report = Build([], month: new OnlineReportMonthInput { FreightTiers = SeptemberTiers(), FreightAdjustmentOverride = -2000000 });
        Assert.AreEqual(-2000000m, report.FreightAdjustment);
        Assert.AreEqual(-971070m, report.FreightCalculated);
        Assert.IsTrue(report.FreightOverridden);
    }

    [TestMethod]
    public void Build_WarnsWhenFreightTiersEmpty()
    {
        var report = Build([]);
        Assert.IsTrue(report.Warnings.Any(w => w.Contains("운임표가 비어 있습니다")));
    }

    // ── 요약 ──

    [TestMethod]
    public void Build_PartnersMatchByKeywordAndManualOverrideWins()
    {
        var report = Build([],
            month: new OnlineReportMonthInput { PartnerOverrides = [new() { DisplayName = "툴스엠알오", Revenue = 2260760, Profit = 912691 }] },
            partners:
            [
                new("이공그로스수동마감", 18172110, 11199574),
                new("주식회사 이공이공인터내셔널", -5549515, -6828557.1m),
                new("주식회사 한결관리", 8914850, 2889099.41m),
                new("투유", 611600, 213995.1m),
            ]);

        var igong = report.Partners.Single(p => p.DisplayName == "이공인터");
        Assert.AreEqual(12622595m, igong.Revenue);
        Assert.AreEqual(4371016.9m, igong.Profit);
        Assert.AreEqual(2, igong.MatchedParties.Count);
        Assert.AreEqual(8914850m, report.Partners.Single(p => p.DisplayName == "한결쇼핑").Revenue);
        var tools = report.Partners.Single(p => p.DisplayName == "툴스엠알오");
        Assert.IsTrue(tools.IsManual);
        Assert.AreEqual(2260760m, tools.Revenue);
        Assert.AreEqual(0m, report.Partners.Single(p => p.DisplayName == "펩투나인").Revenue);
    }

    [TestMethod]
    public void Build_SummarySplitsRocketGrowthAndAppliesCostsAndExports()
    {
        var month = new OnlineReportMonthInput
        {
            ExchangeRate = 1359.20m,
            Exports = [new() { Market = "아마존", RevenueUsd = 100, ProfitUsd = 20 }],
            Costs = new() { ["포천"] = 11000000 },
            FreightTiers = [new() { Rate = 2450, Count = 10 }],
        };
        var report = Build(
        [
            Fact("1F80429D", "14.면도", 10, 1000000, 400000, shipping: 30000),
            Fact("CH004", "14.면도", 10, 2000000, 900000),
            Fact("4D4A484A", "12.핸드", 1, 100000, 20000),
        ],
        [Ad("1F80429D", "14.면도", 100000), Ad("CH004", "14.면도", 300000)], month);

        // 실택배비 조정 = 10 × (3,000 − 2,450 − 400) = 1,500.
        Assert.AreEqual(1500m, report.FreightAdjustment);
        Assert.AreEqual(1030000m, report.OnlineRevenue); // 스마트 1,000,000 + 택배비 30,000
        Assert.AreEqual(300000m + 1500m, report.OnlineNet);
        Assert.AreEqual(2100000m, report.RocketRevenue);
        Assert.AreEqual(620000m, report.RocketNet);
        Assert.AreEqual(135920m, report.ExportRevenue);
        Assert.AreEqual(27184m, report.ExportProfit);
        Assert.AreEqual(11000000m, report.CostTotal);
        Assert.AreEqual(report.SubtotalProfit - 11000000m, report.FinalProfit);
        Assert.AreEqual(0m, report.Costs.Single(c => c.Item == "일용직").Amount);
    }

    [TestMethod]
    public void Build_GroupChanges_TopAndBottomAgainstPreviousMonth()
    {
        var report = Build(
            [Fact("1F80429D", "21.선물", 1, 73341800, 1), Fact("1F80429D", "14.면도", 1, 76618339, 1), Fact("1F80429D", "03.보르", 1, 9914644, 1)],
            previous: [Fact("1F80429D", "21.선물", 1, 1824800, 1), Fact("1F80429D", "14.면도", 1, 79769408, 1), Fact("1F80429D", "03.보르", 1, 11926323, 1)]);

        Assert.AreEqual(1, report.GroupTop.Count);
        Assert.AreEqual("21.선물세트", report.GroupTop[0].Key);
        Assert.AreEqual(71517000m, report.GroupTop[0].Delta);
        Assert.AreEqual(2, report.GroupBottom.Count);
        Assert.AreEqual("14.면도세정액", report.GroupBottom[0].Key);
        Assert.AreEqual(-3151069m, report.GroupBottom[0].Delta);
    }

    [TestMethod]
    public void Build_CskuChanges_WithoutPreviousShowsCurrentTopOnly()
    {
        var current = new List<OnlineReportCskuRow>
        {
            new("1F80429D", "26c_lgsp5x5", "21.선물", 35659300),
            new("CH004", "cpgrz1000ph_2", "14.면도", 17038598),
            new("CH071", "partner_only", "12.핸드", 99999999), // 보고서 채널이 아니라 제외
        };
        var report = Build([], csku: current);

        Assert.IsTrue(report.HasCurrentCsku);
        Assert.IsFalse(report.HasPreviousCsku);
        CollectionAssert.AreEqual(new[] { "26c_lgsp5x5", "cpgrz1000ph_2" }, report.CskuTop.Select(r => r.Key).ToArray());
        Assert.AreEqual(0, report.CskuBottom.Count);
    }

    [TestMethod]
    public void Build_CskuChanges_WithPreviousRanksByDelta()
    {
        var report = Build([],
            csku: [new("1F80429D", "A", "14.면도", 500), new("1F80429D", "B", "14.면도", 100), new("1F80429D", "C", "12.핸드", 50)],
            previousCsku: [new("1F80429D", "A", "14.면도", 100), new("1F80429D", "B", "14.면도", 400), new("1F80429D", "D", "12.핸드", 80)]);

        CollectionAssert.AreEqual(new[] { "A", "C" }, report.CskuTop.Select(r => r.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "B", "D" }, report.CskuBottom.Select(r => r.Key).ToArray());
        Assert.IsNull(report.CskuTop.Single(r => r.Key == "C").Rate); // 전월 0 → 신규
    }

    // ── 기간 표기 ──

    [TestMethod]
    public void PeriodHelpers()
    {
        Assert.AreEqual("2026.09.01~2026.09.30. 정산완료", OnlineReportBuilder.FormatPeriodLabel("2026-09"));
        Assert.AreEqual("2026-08", OnlineReportBuilder.PreviousPeriod("2026-09"));
        Assert.AreEqual("2025-12", OnlineReportBuilder.PreviousPeriod("2026-01"));
        Assert.AreEqual("2609", OnlineReportBuilder.ToCskuStatPeriod("2026-09"));
    }

    // ── 운임 파일 ──

    [TestMethod]
    public void FreightFileAggregator_CountsRowsWithTrackingNumberOnly()
    {
        ExcelLicense.Ensure();
        var path = Path.Combine(_testFolder, "운송장이력 조회_test.xlsx");
        using (var package = new ExcelPackage())
        {
            var ws = package.Workbook.Worksheets.Add("Sheet1");
            string[] headers = ["순번", "운송장번호", "고객주문번호", "운임구분", "운임"];
            for (int i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];
            object?[][] rows =
            [
                ["1", "6000-0000-0001", "#스마1", "신용", 2450.0],
                ["2", "6000-0000-0002", "#쿠팡1", "신용", 2450.0],
                ["3", "6000-0000-0003", null, "신용", 4650.0],
                ["4", null, "#스마2", "신용", 2790.0], // 접수만 된 건 — 제외
                ["5", "6000-0000-0005", "#11번", "신용", null], // 운임 없음 — 제외
            ];
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    ws.Cells[r + 2, c + 1].Value = rows[r][c];
            package.SaveAs(new FileInfo(path));
        }

        var result = FreightFileAggregator.Aggregate([path]);

        Assert.AreEqual(3, result.TotalCount);
        Assert.AreEqual(2, result.Tiers.Single(t => t.Rate == 2450).Count);
        Assert.AreEqual(1, result.Tiers.Single(t => t.Rate == 4650).Count);
        Assert.AreEqual(1, result.Files[0].SkippedNoTrackingNo);
        Assert.AreEqual(1, result.Files[0].SkippedNoFreight);
        Assert.AreEqual(9550m, result.TotalAmount);
    }

    // ── 하나은행 응답 ──

    private const string HanaSample = """
        <table><thead><tr><th>통화</th><th>현찰</th></tr></thead><tbody>
            <tr>
                <td class="tc">
                    <a href="#//HanaBank" title="일일변동내역 보기" onclick="pbk.foreign.rate.pbld.avg.goFluctuation('USD','20260901','20260930','2','0');" >
                    <u>미국 USD</u>
                    </a>
                </td>
                <td class="txtAr">1,382.98</td>
                <td class="txtAr">1,335.42</td>
                <td class="txtAr">1,372.48</td>
                <td class="txtAr">1,345.93</td>

                <td class="txtAr">1,343.79</td>
                <td class="txtAr">1,359.20</td>
                <td class="txtAr">5.76522</td>
                <td class="txtAr">1.0000</td>
            </tr>
            <tr>
                <td class="tc"><a href="#"><u>유로 EUR</u></a></td>
                <td class="txtAr">1,596.70</td><td class="txtAr">1,534.40</td><td class="txtAr">1,581.20</td><td class="txtAr">1,549.90</td>
                <td class="txtAr">1,548.01</td><td class="txtAr">1,565.55</td><td class="txtAr">4.46300</td><td class="txtAr">1.1519</td>
            </tr>
        </tbody></table>
        """;

    [TestMethod]
    public void HanaParser_ReadsMonthlyBaseRate()
    {
        Assert.AreEqual(1359.20m, HanaExchangeRateClient.ParseBaseRate(HanaSample, "USD"));
        Assert.AreEqual(1565.55m, HanaExchangeRateClient.ParseBaseRate(HanaSample, "EUR"));
        Assert.ThrowsExactly<InvalidOperationException>(() => HanaExchangeRateClient.ParseBaseRate(HanaSample, "JPY"));
    }

    // ── 인쇄 나눔 ──

    [TestMethod]
    public void PlanColumnBreaks_KeepsTailOnLastPageWithAtLeastOneGroup()
    {
        CollectionAssert.AreEqual(new[] { 11 }, OnlineReportExcelWriter.PlanColumnBreaks(14, 9, 4)); // C~K | L~P + 꼬리
        CollectionAssert.AreEqual(new[] { 7 }, OnlineReportExcelWriter.PlanColumnBreaks(6, 9, 4));  // C~G | H + 꼬리
        CollectionAssert.AreEqual(Array.Empty<int>(), OnlineReportExcelWriter.PlanColumnBreaks(3, 9, 4));
        CollectionAssert.AreEqual(new[] { 11, 20 }, OnlineReportExcelWriter.PlanColumnBreaks(20, 9, 4));
    }

    [TestMethod]
    public void ComputeChannelLayout_ShrinksOnlyWhenRowsExceedOnePage()
    {
        var config = OnlineReportConfigService.CreateDefault();
        var sixBlocks = 18 + 20.25 + 22.5 + 43 * 21.95;
        var tenBlocks = 18 + 20.25 + 22.5 + 67 * 21.95;

        Assert.AreEqual(65, OnlineReportExcelWriter.ComputeChannelLayout(config, sixBlocks, 14).ScalePercent);
        Assert.IsTrue(OnlineReportExcelWriter.ComputeChannelLayout(config, tenBlocks, 14).ScalePercent < 65);
    }

    // ── DB ──

    [TestMethod]
    public void ProfitFactRepository_RoundTripsShippingFee()
    {
        var repo = new ProfitFactRepository();
        repo.SaveProfitFacts("2026-09", "1F80429D", "스마트", [Fact("1F80429D", "14.면도", 892, 16540320, 10187962, shipping: 2208000)]);

        var row = repo.GetProfitFacts(["2026-09"]).Single();
        Assert.AreEqual(2208000m, row.ShippingFee);
    }

    [TestMethod]
    public void OnlineReportMonthRepository_SavesAndOverwrites()
    {
        var repo = new OnlineReportMonthRepository();
        Assert.IsNull(repo.Get("2026-09"));

        repo.Save("2026-08", new OnlineReportMonthInput { ExchangeRate = 1380 });
        repo.Save("2026-09", new OnlineReportMonthInput { ExchangeRate = 1, FreightTiers = SeptemberTiers() });
        repo.Save("2026-09", new OnlineReportMonthInput { ExchangeRate = 1359.20m, FreightTiers = SeptemberTiers(), FulfillmentCount = 600 });

        var loaded = repo.Get("2026-09")!;
        Assert.AreEqual(1359.20m, loaded.ExchangeRate);
        Assert.AreEqual(600, loaded.FulfillmentCount);
        Assert.AreEqual(2057, loaded.FreightTiers.Sum(t => t.Count));
        Assert.AreEqual("2026-08", repo.GetLatestPeriodBefore("2026-09"));
    }

    // ── 엑셀 출력 ──

    [TestMethod]
    public void ExcelWriter_SummaryFormulasMatchBuilderTotals()
    {
        var month = new OnlineReportMonthInput
        {
            ExchangeRate = 1359.20m,
            Exports = [new() { Market = "아마존", RevenueUsd = 100, ProfitUsd = 20 }],
            Costs = new() { ["포천"] = 11000000 },
            FreightTiers = SeptemberTiers(),
            FulfillmentCount = 600,
            ExtraAdCosts = [new() { BlockName = "쿠팡로켓", Amount = 671880, Memo = "밀크런" }],
        };
        var report = Build(
        [
            Fact("1F80429D", "14.면도", 892, 16540320, 10187962, shipping: 2208000),
            Fact("1F80429D", "41.아마존미국", 1, 15000, 9000, shipping: 3000),
            Fact("443F05E4", "21.선물", 485, 41215400, 2540579, shipping: 3000),
            Fact("4D4A484A", "12.핸드", 156, 850980, 194111),
            Fact("CH004", "14.면도", 2542, 58043959, 26816863),
            Fact("9A2C5962", "21.선물", 304, 15634400, 601250, shipping: 88500),
        ],
        [Ad("1F80429D", "14.면도", 3603859), Ad("CH004", "14.면도", 11083561)],
        month,
        partners: [new("주식회사 한결관리", 8914850, 2889099.41m), new("투유", 611600, 213995.1m)]);

        var path = Path.Combine(_testFolder, "report.xlsx");
        var layout = OnlineReportExcelWriter.Write(path, report, OnlineReportConfigService.CreateDefault());
        Assert.IsTrue(layout.PageCount >= 2);

        using var package = new ExcelPackage(new FileInfo(path));
        CollectionAssert.AreEqual(new[] { "요약", "채널별", "택배비" }, package.Workbook.Worksheets.Select(w => w.Name).ToArray());
        var summary = package.Workbook.Worksheets["요약"];
        var finalRow = Enumerable.Range(1, 60).First(r => summary.Cells[r, 1].Text == "최종");
        var onlineRow = Enumerable.Range(1, 60).First(r => summary.Cells[r, 1].Text == "온라인(로켓 제외)");

        Assert.AreEqual((double)report.OnlineRevenue, Convert.ToDouble(summary.Cells[onlineRow, 3].Value), 0.5);
        Assert.AreEqual((double)report.OnlineNet, Convert.ToDouble(summary.Cells[onlineRow, 4].Value), 0.5);
        Assert.AreEqual((double)report.RocketNet, Convert.ToDouble(summary.Cells[onlineRow + 1, 4].Value), 0.5);
        Assert.AreEqual((double)report.PartnerRevenue, Convert.ToDouble(summary.Cells[onlineRow + 2, 3].Value), 0.5);
        Assert.AreEqual((double)report.ExportRevenue, Convert.ToDouble(summary.Cells[onlineRow + 3, 3].Value), 0.5);
        Assert.AreEqual((double)report.FinalProfit, Convert.ToDouble(summary.Cells[finalRow, 4].Value), 0.5);

        var channel = package.Workbook.Worksheets["채널별"];
        Assert.AreEqual(eOrientation.Portrait, channel.PrinterSettings.Orientation);
        Assert.IsFalse(channel.PrinterSettings.FitToPage);
        Assert.AreEqual("판매처", channel.Cells[3, 1].Text);
        Assert.AreEqual("스마트스토어", channel.Cells[4, 1].Text);
        Assert.AreEqual(eOrientation.Landscape, summary.PrinterSettings.Orientation);
    }
}
