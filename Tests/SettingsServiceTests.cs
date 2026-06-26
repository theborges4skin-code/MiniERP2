using MiniERP2.Config;

namespace MiniERP2.Tests;

[TestClass]
public class SettingsServiceTests
{
    private string _testFile = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"MiniERP2Tests_settings_{Guid.NewGuid()}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testFile))
        {
            File.Delete(_testFile);
        }
    }

    [TestMethod]
    public void SetLastFolder_ThenGet_ReturnsSavedPath()
    {
        var settings = new SettingsService(_testFile);

        settings.SetLastFolder("DbFile", @"C:\Data\Db");
        settings.SetLastFolder("OrderFile", @"C:\Data\Orders");

        Assert.AreEqual(@"C:\Data\Db", settings.GetLastFolder("DbFile"));
        Assert.AreEqual(@"C:\Data\Orders", settings.GetLastFolder("OrderFile"));
    }

    [TestMethod]
    public void GetLastFolder_PersistsAcrossInstances()
    {
        new SettingsService(_testFile).SetLastFolder("DbFile", @"C:\Data\Db");

        var reloaded = new SettingsService(_testFile);

        Assert.AreEqual(@"C:\Data\Db", reloaded.GetLastFolder("DbFile"));
    }

    [TestMethod]
    public void GetLastFolder_UnknownKey_ReturnsNull()
    {
        var settings = new SettingsService(_testFile);

        Assert.IsNull(settings.GetLastFolder("Unknown"));
    }
}
