using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

/// <summary>
/// 운송장 라벨 수동 정정(TrackingLabelOverrideTable)과, 그 정정이 필요해진 원인이었던 로켓 분류
/// 누락(수령인에만 "로켓"이 들어있는 행)을 함께 검증한다.
/// </summary>
[TestClass]
public class TrackingLabelOverrideTests
{
    private string _testFolder = string.Empty;
    private TrackingLabelOverrideRepository _repository = new();

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _repository = new TrackingLabelOverrideRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void SaveMany_ThenGet_ReturnsOnlyRequestedTrackingNos()
    {
        _repository.SaveMany(["1111", "2222"], TrackingLabelClassifier.CoupangRocket);
        _repository.SaveMany(["3333"], TrackingLabelClassifier.Partner);

        var found = _repository.GetForTrackingNos(["1111", "3333", "9999"]);

        Assert.AreEqual(2, found.Count);
        Assert.AreEqual(TrackingLabelClassifier.CoupangRocket, found["1111"]);
        Assert.AreEqual(TrackingLabelClassifier.Partner, found["3333"]);
        Assert.IsFalse(found.ContainsKey("9999"));
    }

    [TestMethod]
    public void SaveMany_SameTrackingNoTwice_OverwritesLabel()
    {
        _repository.SaveMany(["1111"], TrackingLabelClassifier.Partner);
        _repository.SaveMany(["1111"], TrackingLabelClassifier.NaverFulfillment);

        var found = _repository.GetForTrackingNos(["1111"]);

        Assert.AreEqual(1, found.Count);
        Assert.AreEqual(TrackingLabelClassifier.NaverFulfillment, found["1111"]);
    }

    [TestMethod]
    public void DeleteMany_RemovesOverrideSoAutoLabelAppliesAgain()
    {
        _repository.SaveMany(["1111", "2222"], TrackingLabelClassifier.CoupangRocket);

        _repository.DeleteMany(["1111"]);

        var found = _repository.GetForTrackingNos(["1111", "2222"]);
        Assert.IsFalse(found.ContainsKey("1111"));
        Assert.IsTrue(found.ContainsKey("2222"));
    }

    [TestMethod]
    public void GetForTrackingNos_EmptyInput_DoesNotTouchDatabase()
    {
        Assert.AreEqual(0, _repository.GetForTrackingNos([]).Count);
    }

    [TestMethod]
    public void Classify_RocketKeywordOnlyInRecipient_ReturnsCoupangRocket()
    {
        // 품목명이 비어 있고 수령인만 "이천2 (로켓...)"인 실제 파일 형태 — 예전에는 기타로 빠졌다.
        var row = new TrackingBackfillRow { Recipient = "이천2 (로켓배송)", ProductName = "", OrderNoMemo = "" };

        Assert.AreEqual(TrackingLabelClassifier.CoupangRocket, TrackingLabelClassifier.Classify(row));
    }

    [TestMethod]
    public void Classify_NewCenterNameWithNumber_ReturnsCoupangRocket()
    {
        var row = new TrackingBackfillRow { Recipient = "안산3", ProductName = "", OrderNoMemo = "" };

        Assert.AreEqual(TrackingLabelClassifier.CoupangRocket, TrackingLabelClassifier.Classify(row));
    }

    [TestMethod]
    public void Classify_FbaCenterRecipient_StillReturnsAmazonFba()
    {
        // 로켓 센터명 목록에 "인천"이 있지만 뒤에 숫자가 없으므로 FBA 판정이 그대로 유지돼야 한다.
        var row = new TrackingBackfillRow { Recipient = "KW 인천센터", ProductName = "", OrderNoMemo = "[SEND]" };

        Assert.AreEqual(TrackingLabelClassifier.AmazonFba, TrackingLabelClassifier.Classify(row));
    }
}
