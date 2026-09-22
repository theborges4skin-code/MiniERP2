using MiniERP2.Config;

namespace MiniERP2.Tests;

[TestClass]
public class SelectChannelDialogStateServiceTests
{
    private string _testFile = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"MiniERP2Tests_selectchannel_{Guid.NewGuid()}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [TestMethod]
    public void Load_NoFile_ReturnsEmptyStateWithoutHasState()
    {
        var state = new SelectChannelDialogStateService(_testFile).Load();

        Assert.IsFalse(state.HasState);
        Assert.AreEqual(0, state.ExpandedGroups.Count);
        Assert.IsNull(state.LastChannelCode);
    }

    [TestMethod]
    public void Save_ThenLoad_RoundTripsExpandedGroupsAndChannel()
    {
        var service = new SelectChannelDialogStateService(_testFile);
        service.Save(new SelectChannelDialogState
        {
            ExpandedGroups = new List<string> { "__FAV__", "온라인" },
            LastChannelCode = "9C5A5A47",
            HasState = true
        });

        var state = new SelectChannelDialogStateService(_testFile).Load();

        Assert.IsTrue(state.HasState);
        CollectionAssert.AreEquivalent(new[] { "__FAV__", "온라인" }, state.ExpandedGroups);
        Assert.AreEqual("9C5A5A47", state.LastChannelCode);
    }

    [TestMethod]
    public void Load_CorruptedFile_FallsBackToDefaultState()
    {
        File.WriteAllText(_testFile, "{ not json");

        var state = new SelectChannelDialogStateService(_testFile).Load();

        Assert.IsFalse(state.HasState);
    }
}
