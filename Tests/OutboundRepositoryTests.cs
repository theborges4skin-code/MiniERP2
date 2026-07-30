using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class OutboundRepositoryTests
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

    [TestMethod]
    public void SaveOutbound_SameOrderAndSkuTwice_UpdatesInsteadOfDuplicating()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-1", TrackingNo = "T001", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        // 같은 주문/SKU를 다시 저장(예: 같은 발주서를 중복 처리) — 중복 적재가 아니라 갱신되어야 한다.
        // 단, 이미 출고확정이므로(P1) SupplyPrice는 그대로 유지되고 나머지 필드만 갱신된다.
        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-1", TrackingNo = "T002", MskuCode = "SKU-1", Qty = 2, SupplyPrice = 1500m },
        });

        var to = DateTime.Now.AddMinutes(5);
        var results = repository.GetByChannel(channelCode, from, to);

        Assert.HasCount(1, results);
        Assert.AreEqual("T002", results[0].TrackingNo);
        Assert.AreEqual(2, results[0].Qty);
        Assert.AreEqual(1000m, results[0].SupplyPrice);
    }

    [TestMethod]
    public void SaveOutbound_AlreadyShipped_LocksSupplyPrice()
    {
        // 견적기록관리_개발기획서_확정본.md P1: 이미 출고확정된 건을 같은 키로 재저장해도(가격조정
        // 등 무관한 이유로 재실행되는 경우 포함) 단가가 조용히 최신값으로 덮이면 안 된다.
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-P1", TrackingNo = "T900", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-P1", TrackingNo = "T900", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 2000m },
        });

        var result = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual(1000m, result.SupplyPrice);
    }

    [TestMethod]
    public void SaveOutbound_NotYetShipped_StillAllowsSupplyPriceChange()
    {
        // 발주확정 상태(아직 운송장번호 없음)에서는 P1 가드가 적용되지 않고 자유롭게 갱신되어야 한다.
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-P1B", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-P1B", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1200m },
        });

        var result = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual(1200m, result.SupplyPrice);
    }

    [TestMethod]
    public void SaveOutbound_SameOrderAndSkuTwice_ReturnsNoConflict()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-1", TrackingNo = "T001", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        var conflicts = repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-1", TrackingNo = "T002", MskuCode = "SKU-1", Qty = 2, SupplyPrice = 1500m },
        });

        Assert.IsEmpty(conflicts);
    }

    [TestMethod]
    public void SaveOutbound_DifferentOrdersSameShipmentGroupKeyAndSku_ReportsConflict()
    {
        // ShipmentGroupKey 재사용(예: 근본 결함으로 두 발주서가 우연히 같은 키를 갖게 된 경우)으로
        // 서로 다른 주문이 같은 (ShipmentGroupKey, MskuCode)에 충돌하면, 조용히 덮어쓰지 않고
        // 호출 측에 알려야 한다.
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-A", ShipmentGroupKey = "SAME-KEY", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        var conflicts = repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-B", ShipmentGroupKey = "SAME-KEY", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        Assert.HasCount(1, conflicts);
        Assert.AreEqual("ORDER-A", conflicts[0].ExistingOrderNo);
        Assert.AreEqual("ORDER-B", conflicts[0].NewOrderNo);
    }

    [TestMethod]
    public void SaveOutbound_DifferentSkusSameOrder_SavesBothRows()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-2", TrackingNo = "T010", MskuCode = "SKU-A", Qty = 1, SupplyPrice = 1000m },
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-2", TrackingNo = "T010", MskuCode = "SKU-B", Qty = 1, SupplyPrice = 2000m },
        });

        var to = DateTime.Now.AddMinutes(5);
        var results = repository.GetByChannel(channelCode, from, to);

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void SaveOutbound_WithoutTrackingNo_StartsAsWaitingForShipment()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-3", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        var results = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5));

        Assert.AreEqual("발주확정", results[0].Status);
        Assert.IsNull(results[0].ConfirmedAt);
    }

    [TestMethod]
    public void SaveOutbound_WithTrackingNo_StartsAsShippedWithConfirmedAt()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-4", TrackingNo = "T100", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        var results = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5));

        Assert.AreEqual("출고확정", results[0].Status);
        Assert.IsNotNull(results[0].ConfirmedAt);
    }

    [TestMethod]
    public void SaveOutbound_ReconfirmingWithoutTrackingNo_DoesNotDowngradeAlreadyShippedStatus()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-5", TrackingNo = "T200", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });
        // 같은 건을 운송장번호 없이 다시 저장(예: 발주서 재로딩 후 재확정) — 이미 출고확정이었던
        // 상태가 발주확정으로 후퇴하면 안 된다.
        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-5", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        var results = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5));

        Assert.AreEqual("출고확정", results[0].Status);
        Assert.IsNotNull(results[0].ConfirmedAt);
    }

    [TestMethod]
    public void MarkAsShipped_UpdatesStatusAndConfirmedAt()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-6", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });
        var saved = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();

        repository.MarkAsShipped([saved.Id]);

        var updated = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual("출고확정", updated.Status);
        Assert.IsNotNull(updated.ConfirmedAt);
    }

    [TestMethod]
    public void SaveOutbound_PersistsRecipientAddressAndProductName()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail
            {
                ChannelCode = channelCode, OrderNo = "ORDER-7", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m,
                Recipient = "홍길동", Address = "서울시 강남구", ProductName = "테스트상품", Remark = "문 앞에 놔주세요",
            },
        });

        var result = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();

        Assert.AreEqual("홍길동", result.Recipient);
        Assert.AreEqual("서울시 강남구", result.Address);
        Assert.AreEqual("테스트상품", result.ProductName);
        Assert.AreEqual("문 앞에 놔주세요", result.Remark);
    }

    [TestMethod]
    public void ApplyTrackingNo_SetsTrackingNoAndConfirmsShipment()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-8", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m, Recipient = "김철수" },
        });
        var saved = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();

        repository.ApplyTrackingNo(saved.Id, "T400");

        var updated = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual("T400", updated.TrackingNo);
        Assert.AreEqual("출고확정", updated.Status);
        Assert.IsNotNull(updated.ConfirmedAt);
    }

    [TestMethod]
    public void UpdateDetail_PersistsEditedFields()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-9", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });
        var saved = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();

        saved.Qty = 5;
        saved.SupplyPrice = 9999m;
        saved.TrackingNo = "T500";
        saved.Status = "출고확정";
        saved.ConfirmedAt = DateTime.Now;
        saved.PurchaseChannelCode = "VENDOR_A";
        saved.PurchasePrice = 700m;
        saved.WeightKg = 12.5m;
        repository.UpdateDetail(saved);

        var updated = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual(5, updated.Qty);
        Assert.AreEqual(9999m, updated.SupplyPrice);
        Assert.AreEqual("T500", updated.TrackingNo);
        Assert.AreEqual("출고확정", updated.Status);
        Assert.AreEqual("VENDOR_A", updated.PurchaseChannelCode);
        Assert.AreEqual(700m, updated.PurchasePrice);
        Assert.AreEqual(12.5m, updated.WeightKg);
    }

    [TestMethod]
    public void UpdateDetail_AlreadyShipped_LocksSupplyPriceAndReturnsTrue()
    {
        // 견적기록관리_개발기획서_확정본.md P3: 이미 출고확정인 행의 납품가를 발주/출고 이력 관리창
        // 인라인 편집으로 바꿔도 반영되지 않아야 한다(§4.2 소급 경로로만 허용). 반환값(true)으로
        // 호출 측이 "잠겨서 반영 안 됨"을 사용자에게 안내할 수 있어야 한다.
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-P3", TrackingNo = "T600", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });
        var saved = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual("출고확정", saved.Status);

        saved.SupplyPrice = 5000m;
        var wasLocked = repository.UpdateDetail(saved);

        Assert.IsTrue(wasLocked);
        var updated = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual(1000m, updated.SupplyPrice);
    }

    [TestMethod]
    public void UpdateDetail_NotYetShipped_AllowsSupplyPriceChangeAndReturnsFalse()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-P3B", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });
        var saved = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual("발주확정", saved.Status);

        saved.SupplyPrice = 1300m;
        var wasLocked = repository.UpdateDetail(saved);

        Assert.IsFalse(wasLocked);
        var updated = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5)).Single();
        Assert.AreEqual(1300m, updated.SupplyPrice);
    }

    [TestMethod]
    public void DeleteByIds_RemovesSelectedRowsOnly()
    {
        var repository = new OutboundRepository();
        var channelCode = "TESTCH";
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-10", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
            new OutboundDetail { ChannelCode = channelCode, OrderNo = "ORDER-11", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });
        var saved = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5));
        var toDelete = saved.First(d => d.OrderNo == "ORDER-10").Id;

        repository.DeleteByIds([toDelete]);

        var results = repository.GetByChannel(channelCode, from, DateTime.Now.AddMinutes(5));
        Assert.HasCount(1, results);
        Assert.AreEqual("ORDER-11", results[0].OrderNo);
    }

    [TestMethod]
    public void GetHistory_NullChannelCode_ReturnsAllChannels()
    {
        var repository = new OutboundRepository();
        var from = DateTime.Now.AddMinutes(-5);

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = "CH-A", OrderNo = "ORDER-12", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
            new OutboundDetail { ChannelCode = "CH-B", OrderNo = "ORDER-13", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        var results = repository.GetHistory(null, from, DateTime.Now.AddMinutes(5));

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void FindByOrderNos_ReturnsOnlyMatchingOrders_IgnoringChannel()
    {
        var repository = new OutboundRepository();

        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = "CH-A", OrderNo = "ORDER-14", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
            new OutboundDetail { ChannelCode = "CH-B", OrderNo = "ORDER-15", TrackingNo = "T900", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
            new OutboundDetail { ChannelCode = "CH-A", OrderNo = "ORDER-16", TrackingNo = "", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 1000m },
        });

        var results = repository.FindByOrderNos(["ORDER-14", "ORDER-15", "ORDER-NOT-EXIST"]);

        Assert.HasCount(2, results);
        Assert.IsTrue(results.Any(r => r.OrderNo == "ORDER-14" && r.Status == "발주확정"));
        Assert.IsTrue(results.Any(r => r.OrderNo == "ORDER-15" && r.Status == "출고확정"));
    }

    [TestMethod]
    public void FindByOrderNos_EmptyInput_ReturnsEmptyWithoutQuerying()
    {
        var repository = new OutboundRepository();

        var results = repository.FindByOrderNos([]);

        Assert.HasCount(0, results);
    }

    [TestMethod]
    public void UpdateProductName_ChangesOnlyThatColumn()
    {
        var repository = new OutboundRepository();
        var conflicts = repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = "CH-A", OrderNo = "ORDER-20", TrackingNo = "T020", MskuCode = "SKU-1", Qty = 3, SupplyPrice = 5000m, ProductName = "" },
        });
        Assert.IsEmpty(conflicts);
        var saved = repository.FindByOrderNos(["ORDER-20"]).Single();

        repository.UpdateProductName(saved.Id, "SLES 18L 말통");

        var reloaded = repository.FindByOrderNos(["ORDER-20"]).Single();
        Assert.AreEqual("SLES 18L 말통", reloaded.ProductName);
        Assert.AreEqual(3, reloaded.Qty); // 다른 열은 안 건드려야 한다
        Assert.AreEqual(5000m, reloaded.SupplyPrice);
    }

    [TestMethod]
    public void CorrectSupplyPrice_BypassesConfirmedLock()
    {
        // 거래처마감보드 §4.2 ② 소급 경로 — 정상 UpdateDetail은 출고확정 건의 SupplyPrice를 잠그지만
        // (SaveOutbound_AlreadyShipped_LocksSupplyPrice 참고), 이 정정 경로는 의도적으로 뚫고 지나가야 한다.
        var repository = new OutboundRepository();
        repository.SaveOutbound(new[]
        {
            new OutboundDetail { ChannelCode = "CH-A", OrderNo = "ORDER-21", TrackingNo = "T021", MskuCode = "SKU-1", Qty = 1, SupplyPrice = 65000m },
        });
        var saved = repository.FindByOrderNos(["ORDER-21"]).Single();
        Assert.AreEqual("출고확정", saved.Status);

        repository.CorrectSupplyPrice(saved.Id, 59091m);

        var reloaded = repository.FindByOrderNos(["ORDER-21"]).Single();
        Assert.AreEqual(59091m, reloaded.SupplyPrice);
    }

    [TestMethod]
    public void CorrectSupplyPriceForCskuFromDate_UpdatesOnlyMatchingChannelCskuAndDateRange()
    {
        var repository = new OutboundRepository();
        var oldDate = DateTime.Now.AddDays(-10);
        var newDate = DateTime.Now.AddDays(-2);

        // 같은 CSKU, 기준일 이전 건(안 바뀌어야 함)
        repository.SaveOutbound(new[] { new OutboundDetail { ChannelCode = "CH-A", OrderNo = "ORDER-22", TrackingNo = "T022", MskuCode = "SLES18", CskuCode = "SLES18", Qty = 1, SupplyPrice = 60000m } });
        // 같은 CSKU, 기준일 이후 건(바뀌어야 함)
        repository.SaveOutbound(new[] { new OutboundDetail { ChannelCode = "CH-A", OrderNo = "ORDER-23", TrackingNo = "T023", MskuCode = "SLES18", CskuCode = "SLES18", Qty = 1, SupplyPrice = 60000m } });
        // 다른 채널의 같은 CSKU(안 바뀌어야 함)
        repository.SaveOutbound(new[] { new OutboundDetail { ChannelCode = "CH-B", OrderNo = "ORDER-24", TrackingNo = "T024", MskuCode = "SLES18", CskuCode = "SLES18", Qty = 1, SupplyPrice = 60000m } });

        // 기준일 이전 건은 ConfirmedAt을 강제로 과거로 되돌려 조건 검증(SaveOutbound는 항상 지금 시각으로 확정한다).
        var beforeRow = repository.FindByOrderNos(["ORDER-22"]).Single();
        BackdateConfirmedAt(beforeRow.Id, oldDate);

        var updatedCount = repository.CorrectSupplyPriceForCskuFromDate("CH-A", "SLES18", newDate.Date, 59091m);

        Assert.AreEqual(1, updatedCount);
        Assert.AreEqual(60000m, repository.FindByOrderNos(["ORDER-22"]).Single().SupplyPrice); // 기준일 이전 — 안 바뀜
        Assert.AreEqual(59091m, repository.FindByOrderNos(["ORDER-23"]).Single().SupplyPrice); // 기준일 이후 — 바뀜
        Assert.AreEqual(60000m, repository.FindByOrderNos(["ORDER-24"]).Single().SupplyPrice); // 다른 채널 — 안 바뀜
    }

    private static void BackdateConfirmedAt(long id, DateTime confirmedAt)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE OutboundDetailTable SET ConfirmedAt = $confirmedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$confirmedAt", confirmedAt);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }
}
