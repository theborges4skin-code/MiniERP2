using MiniERP2.Config;

namespace MiniERP2.Tests;

[TestClass]
public class SplitterSettingsServiceTests
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
        Directory.Delete(_testFolder, recursive: true);
    }

    [TestMethod]
    public void SaveDistance_ThenLoad_ReturnsSavedValue()
    {
        var service = new SplitterSettingsService();

        service.SaveDistance("OfsForm.GridSplit", 350);

        Assert.AreEqual(350, service.LoadDistance("OfsForm.GridSplit"));
    }

    [TestMethod]
    public void LoadDistance_PersistsAcrossInstances()
    {
        new SplitterSettingsService().SaveDistance("MappingForm.UnmappedSplit", 220);

        var reloaded = new SplitterSettingsService();

        Assert.AreEqual(220, reloaded.LoadDistance("MappingForm.UnmappedSplit"));
    }

    [TestMethod]
    public void LoadDistance_UnknownKey_ReturnsNull()
    {
        var service = new SplitterSettingsService();

        Assert.IsNull(service.LoadDistance("Unknown"));
    }

    [TestMethod]
    public void SaveDistance_SameKeyTwice_UpdatesValue()
    {
        var service = new SplitterSettingsService();

        service.SaveDistance("MappingForm.CandidatesSplit", 430);
        service.SaveDistance("MappingForm.CandidatesSplit", 500);

        Assert.AreEqual(500, service.LoadDistance("MappingForm.CandidatesSplit"));
    }
}
