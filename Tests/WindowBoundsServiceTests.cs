using MiniERP2.Config;
using MiniERP2.Models;

namespace MiniERP2.Tests;

[TestClass]
public class WindowBoundsServiceTests
{
    private string _testFile = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"MiniERP2Tests_windowbounds_{Guid.NewGuid()}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [TestMethod]
    public void Save_ThenGet_ReturnsSavedBounds()
    {
        var service = new WindowBoundsService(_testFile);
        service.Save("OfsForm", new WindowBounds { Left = 10, Top = 20, Width = 800, Height = 600, WindowState = "Maximized" });

        var loaded = service.Get("OfsForm");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(800, loaded.Width);
        Assert.AreEqual("Maximized", loaded.WindowState);
    }

    [TestMethod]
    public void Save_PersistsAcrossInstances()
    {
        var service1 = new WindowBoundsService(_testFile);
        service1.Save("MasterSkuForm", new WindowBounds { Left = 1, Top = 2, Width = 300, Height = 400 });

        var service2 = new WindowBoundsService(_testFile);
        var loaded = service2.Get("MasterSkuForm");

        Assert.IsNotNull(loaded);
        Assert.AreEqual(300, loaded.Width);
    }

    [TestMethod]
    public void Get_UnknownKey_ReturnsNull()
    {
        var service = new WindowBoundsService(_testFile);

        Assert.IsNull(service.Get("Unknown"));
    }
}
