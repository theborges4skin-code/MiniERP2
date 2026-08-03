using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class AddressBookRepositoryTests
{
    private string _testFolder = string.Empty;
    private AddressBookRepository _repository = new();

    [TestInitialize]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testFolder);
        PathProvider.AppDataFolder = _testFolder;
        _repository = new AddressBookRepository();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void Upsert_NewEntry_AssignsAddressIdAndPersistsFields()
    {
        var entry = new AddressBookEntry
        {
            Label = "쿠팡 대구센터",
            ReceiverName = "홍길동",
            Phone = "010-1234-5678",
            Address = "대구시 어딘가 1번지",
            Memo = "평일만 입고",
            IsActive = true,
            DisplayOrder = 1,
        };

        var saved = _repository.Upsert(entry);

        Assert.AreNotEqual(0, saved.AddressId);
        var all = _repository.GetAll();
        Assert.HasCount(1, all);
        Assert.AreEqual("쿠팡 대구센터", all[0].Label);
        Assert.AreEqual("홍길동", all[0].ReceiverName);
        Assert.AreEqual("대구시 어딘가 1번지", all[0].Address);
    }

    [TestMethod]
    public void Upsert_WithChannelTags_RoundTripsTags()
    {
        var entry = new AddressBookEntry
        {
            Label = "쿠팡 서울센터",
            ReceiverName = "김철수",
            ChannelTags = ["COUPANG", "COUPANG_ROCKET"],
        };

        var saved = _repository.Upsert(entry);

        var reloaded = _repository.GetAll().Single(a => a.AddressId == saved.AddressId);
        CollectionAssert.AreEquivalent(new[] { "COUPANG", "COUPANG_ROCKET" }, reloaded.ChannelTags);
    }

    [TestMethod]
    public void Upsert_ExistingEntry_ReplacesTagsInsteadOfAccumulating()
    {
        var entry = new AddressBookEntry { Label = "주소1", ChannelTags = ["A", "B"] };
        var saved = _repository.Upsert(entry);

        saved.ChannelTags = ["C"];
        _repository.Upsert(saved);

        var reloaded = _repository.GetAll().Single(a => a.AddressId == saved.AddressId);
        CollectionAssert.AreEquivalent(new[] { "C" }, reloaded.ChannelTags);
    }

    [TestMethod]
    public void Delete_RemovesEntryAndItsTags()
    {
        var entry = new AddressBookEntry { Label = "삭제될 주소", ChannelTags = ["A"] };
        var saved = _repository.Upsert(entry);

        _repository.Delete(saved.AddressId);

        Assert.HasCount(0, _repository.GetAll());
    }
}
