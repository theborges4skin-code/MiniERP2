using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Models;
using MiniERP2.Services;

namespace MiniERP2.Tests;

/// <summary>실제 Drive 접근 없이 폴링 로직만 검증하기 위한 가짜 클라이언트.</summary>
internal class FakeAutoOrderDriveClient : IAutoOrderDriveClient
{
    public bool CachedAuthorization { get; set; }
    public AutoOrderManifest? Manifest { get; set; }
    public int AuthorizeCallCount { get; private set; }

    public bool HasCachedAuthorization() => CachedAuthorization;

    public Task AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        AuthorizeCallCount++;
        CachedAuthorization = true;
        return Task.CompletedTask;
    }

    public Task<AutoOrderManifest?> FetchManifestAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Manifest);

    public Task<byte[]?> DownloadFileAsync(string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);
}

[TestClass]
public class AutoOrderPollingServiceTests
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

    private static AutoOrderManifest MakeManifest(params string[] ids) => new()
    {
        SchemaVersion = 1,
        GeneratedAt = DateTime.Now,
        Items = ids.Select(id => new AutoOrderManifestItem
        {
            Id = id,
            Subject = $"자동발주처리 - {id}",
            ReceivedAt = DateTime.Now,
            XlsxPath = $"pending/{id}.xlsx",
            XlsxSha256 = "hash",
            RowCount = 2,
            ParseStatus = "ok",
        }).ToList(),
    };

    [TestMethod]
    public async Task PollAsync_NotAuthorizedAndNotInteractive_ReturnsZeroWithoutAuthorizing()
    {
        var fakeClient = new FakeAutoOrderDriveClient { CachedAuthorization = false };
        var service = new AutoOrderPollingService(fakeClient);

        var newCount = await service.PollAsync(allowInteractiveAuth: false);

        Assert.AreEqual(0, newCount);
        Assert.AreEqual(0, fakeClient.AuthorizeCallCount);
    }

    [TestMethod]
    public async Task PollAsync_AlreadyAuthorized_RefreshesSilentlyEvenWithoutInteractiveFlag()
    {
        var fakeClient = new FakeAutoOrderDriveClient { CachedAuthorization = true, Manifest = MakeManifest("id-1") };
        var service = new AutoOrderPollingService(fakeClient);

        var newCount = await service.PollAsync(allowInteractiveAuth: false);

        Assert.AreEqual(1, newCount);
        Assert.AreEqual(1, fakeClient.AuthorizeCallCount);
    }

    [TestMethod]
    public async Task PollAsync_NewManifestItems_InsertsOnlyUnseenOnes()
    {
        var repository = new Database.AutoOrderInboxRepository();
        repository.InsertIfNew(new AutoOrderInboxItem { Id = "already-seen", ReceivedAt = DateTime.Now, SeenAt = DateTime.Now });

        var fakeClient = new FakeAutoOrderDriveClient
        {
            CachedAuthorization = true,
            Manifest = MakeManifest("already-seen", "new-1", "new-2"),
        };
        var service = new AutoOrderPollingService(fakeClient, repository);

        var newCount = await service.PollAsync(allowInteractiveAuth: true);

        Assert.AreEqual(2, newCount);
        Assert.HasCount(3, repository.GetAll());
        Assert.AreEqual("new", repository.GetById("new-1")!.Status);
    }

    [TestMethod]
    public async Task PollAsync_NoManifest_ReturnsZero()
    {
        var fakeClient = new FakeAutoOrderDriveClient { CachedAuthorization = true, Manifest = null };
        var service = new AutoOrderPollingService(fakeClient);

        var newCount = await service.PollAsync(allowInteractiveAuth: true);

        Assert.AreEqual(0, newCount);
    }
}
