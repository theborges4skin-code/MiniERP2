using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.Models;

namespace MiniERP2.Tests;

/// <summary>
/// 쿠팡일반 광고 채널 분리 — 판매방식 선판정 → 광고집행 옵션ID 인벤토리 → (옵션ID가 "-"/0이면)
/// 캠페인명 인벤토리 순으로 판정되는지 검증한다.
/// </summary>
[TestClass]
public class AdChannelSplitResolverTests
{
    private const string Channel = "CH-CPG";
    private static readonly List<string> SourceHeaders = ["광고집행 옵션ID", "캠페인명", "캠페인 이름"];
    private string _testFolder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;

        var repository = new AdChannelSplitRepository();
        repository.AddPreruleWithDetails(Channel, 1, "쿠팡로켓", "Retail이면 로켓", true,
            [new AdChannelSplitPreruleDetail { HeaderName = "판매방식", Operator = AdConditionOperator.Equals, TargetValue = "Retail", Logic = ConditionLogic.And }]);
        repository.UpsertInventoryEntry(Channel, "광고집행 옵션ID", "94834958707", "쿠팡그로스", "2610", 0m);
        repository.UpsertInventoryEntry(Channel, "광고집행 옵션ID", "70057411665", "쿠팡일반", "2610", 0m);
        repository.UpsertInventoryEntry(Channel, "캠페인 이름", "신규구매고객_보르헤스", "쿠팡그로스", "2610", 0m);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    private static AdSpendItem Row(params (string Header, string Value)[] values) =>
        new() { RawValues = values.ToDictionary(v => v.Header, v => v.Value) };

    private static AdChannelSplitResolver Resolver() => new(new AdChannelSplitRepository(), Channel, SourceHeaders);

    [TestMethod]
    public void Resolve_SameCampaign_SplitsByOptionId()
    {
        var resolver = Resolver();
        var growth = Row(("판매방식", "3P"), ("광고집행 옵션ID", "94834958707"), ("캠페인명", "11스마트상품관리"));
        var general = Row(("판매방식", "3P"), ("광고집행 옵션ID", "70057411665"), ("캠페인명", "11스마트상품관리"));

        resolver.Resolve(growth);
        resolver.Resolve(general);

        Assert.AreEqual("쿠팡그로스", growth.ResolvedChannel);
        Assert.AreEqual("인벤토리", growth.ChannelMatchType);
        Assert.AreEqual("쿠팡일반", general.ResolvedChannel);
    }

    [TestMethod]
    public void Resolve_RetailRow_UsesPreruleBeforeOptionId()
    {
        var item = Row(("판매방식", "Retail"), ("광고집행 옵션ID", "70057411665"));
        Resolver().Resolve(item);
        Assert.AreEqual("쿠팡로켓", item.ResolvedChannel);
        Assert.AreEqual("선규칙", item.ChannelMatchType);
    }

    [TestMethod]
    public void Resolve_PlaceholderOptionId_FallsBackToCampaign()
    {
        var resolver = Resolver();
        foreach (var placeholder in new[] { "-", "0", " " })
        {
            var item = Row(("판매방식", "3P"), ("광고집행 옵션ID", placeholder), ("캠페인 이름", "신규구매고객_보르헤스"));
            resolver.Resolve(item);
            Assert.AreEqual("캠페인 이름", item.CampaignSrc, $"옵션ID '{placeholder}'");
            Assert.AreEqual("쿠팡그로스", item.ResolvedChannel, $"옵션ID '{placeholder}'");
        }
    }

    [TestMethod]
    public void Resolve_UnknownOptionId_IsUnclassified()
    {
        var item = Row(("판매방식", "3P"), ("광고집행 옵션ID", "99999999999"), ("캠페인명", "11스마트상품관리"));
        Resolver().Resolve(item);
        Assert.AreEqual(AdChannelSplitResolver.DefaultChannel, item.ResolvedChannel);
        Assert.AreEqual("광고집행 옵션ID", item.CampaignSrc);
    }
}
