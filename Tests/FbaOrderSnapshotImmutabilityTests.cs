using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

/// <summary>
/// 기획서 §12 테스트 필수 항목 5 — 박스규격 마스터 치수를 수정해도 기존 발주의 재출력 치수(박스
/// 스냅샷)는 불변이어야 한다. CSKU 마스터를 수정해도 과거 발주의 품목 스냅샷(ASIN/단가/개당무게 등)이
/// 불변이어야 한다는 §3.4 요구사항도 함께 검증한다.
/// </summary>
[TestClass]
public class FbaOrderSnapshotImmutabilityTests
{
    private string _testFolder = string.Empty;
    private FbaOrderRepository _orderRepository = new();
    private FbaBoxSpecRepository _boxSpecRepository = new();
    private FbaCskuRepository _cskuRepository = new();

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _orderRepository = new FbaOrderRepository();
        _boxSpecRepository = new FbaBoxSpecRepository();
        _cskuRepository = new FbaCskuRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void ReprintedBoxDimensions_AreUnaffectedByLaterBoxSpecMasterEdits()
    {
        _boxSpecRepository.Upsert(new FbaBoxSpec { BoxName = "4Lx4개", WidthMm = 500, DepthMm = 280, HeightMm = 300 });

        var order = new FbaOrder { FbaNo = "FBA-1", OrderDate = DateTime.Today, ReceiverName = "R", Phone = "P", Address = "A" };
        var box = new FbaBox { FbaNo = "FBA-1", BoxSeq = 1, BoxSpecName = "4Lx4개", WidthMm = 500, DepthMm = 280, HeightMm = 300 };
        var item = new FbaBoxItem { FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 1, Csku = "CSKU-1", ItemName = "A", Qty = 1 };
        _orderRepository.SaveOrder(order, [box], [item]);

        // 마스터 치수를 나중에 수정한다(사용자가 박스규격을 재실측했다고 가정).
        _boxSpecRepository.Upsert(new FbaBoxSpec { BoxName = "4Lx4개", WidthMm = 999, DepthMm = 999, HeightMm = 999 });

        var (_, boxes, _) = _orderRepository.GetOrder("FBA-1");
        var reprintedBox = boxes.Single();
        Assert.AreEqual(500, reprintedBox.WidthMm);
        Assert.AreEqual(280, reprintedBox.DepthMm);
        Assert.AreEqual(300, reprintedBox.HeightMm);
    }

    [TestMethod]
    public void ReprintedItemFields_AreUnaffectedByLaterCskuMasterEdits()
    {
        _cskuRepository.Upsert(new FbaCskuModel { Csku = "CSKU-1", ItemName = "샴푸", Asin = "ASIN-1", ItemPrice = 9.99m, UnitWeightG = 100 });

        var order = new FbaOrder { FbaNo = "FBA-1", OrderDate = DateTime.Today, ReceiverName = "R", Phone = "P", Address = "A" };
        var box = new FbaBox { FbaNo = "FBA-1", BoxSeq = 1 };
        var item = new FbaBoxItem
        {
            FbaNo = "FBA-1", BoxSeq = 1, ItemSeq = 1, Csku = "CSKU-1", ItemName = "샴푸",
            Asin = "ASIN-1", ItemPrice = 9.99m, UnitWeightG = 100, Qty = 1,
        };
        _orderRepository.SaveOrder(order, [box], [item]);

        // 마스터 값을 나중에 수정한다(단가 인상, ASIN 정정 등).
        _cskuRepository.Upsert(new FbaCskuModel { Csku = "CSKU-1", ItemName = "샴푸(리뉴얼)", Asin = "ASIN-2", ItemPrice = 15.00m, UnitWeightG = 120 });

        var (_, _, items) = _orderRepository.GetOrder("FBA-1");
        var reprintedItem = items.Single();
        Assert.AreEqual("샴푸", reprintedItem.ItemName);
        Assert.AreEqual("ASIN-1", reprintedItem.Asin);
        Assert.AreEqual(9.99m, reprintedItem.ItemPrice);
        Assert.AreEqual(100, reprintedItem.UnitWeightG);
    }
}
