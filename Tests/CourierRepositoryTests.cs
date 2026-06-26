using Microsoft.Data.Sqlite;
using MiniERP2.Config;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class CourierRepositoryTests
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
    public void Upsert_ThenGetAll_ReturnsCourier()
    {
        var repository = new CourierRepository();
        repository.Upsert(new CourierMaster { CourierName = "CJ대한통운", HeaderMappingJson = """{"받는분":"Recipient"}""" });

        var couriers = repository.GetAll();

        Assert.HasCount(1, couriers);
        Assert.AreEqual("CJ대한통운", couriers[0].CourierName);
    }

    [TestMethod]
    public void Upsert_WithSameName_UpdatesExistingMapping()
    {
        var repository = new CourierRepository();
        repository.Upsert(new CourierMaster { CourierName = "CJ대한통운", HeaderMappingJson = """{"받는분":"Recipient"}""" });
        repository.Upsert(new CourierMaster { CourierName = "CJ대한통운", HeaderMappingJson = """{"받는분":"Recipient","연락처":"Phone"}""" });

        var couriers = repository.GetAll();

        Assert.HasCount(1, couriers);
        Assert.Contains("Phone", couriers[0].HeaderMappingJson);
    }

    [TestMethod]
    public void Delete_RemovesCourier()
    {
        var repository = new CourierRepository();
        repository.Upsert(new CourierMaster { CourierName = "CJ대한통운", HeaderMappingJson = "{}" });
        repository.Upsert(new CourierMaster { CourierName = "한진택배", HeaderMappingJson = "{}" });

        repository.Delete("CJ대한통운");
        var couriers = repository.GetAll();

        Assert.HasCount(1, couriers);
        Assert.AreEqual("한진택배", couriers[0].CourierName);
    }
}
