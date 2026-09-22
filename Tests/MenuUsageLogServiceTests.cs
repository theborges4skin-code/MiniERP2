using MiniERP2.Config;

namespace MiniERP2.Tests;

[TestClass]
public class MenuUsageLogServiceTests
{
    private string _testFile = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"MiniERP2Tests_menuusage_{Guid.NewGuid()}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [TestMethod]
    public void RecordUse_FirstTime_StartsCountAtOne()
    {
        var service = new MenuUsageLogService(_testFile);
        service.RecordUse("OFS (발주처리)");

        var stat = service.GetAll()["OFS (발주처리)"];

        Assert.AreEqual(1, stat.Count);
    }

    [TestMethod]
    public void RecordUse_Repeated_IncrementsCountAndUpdatesTimestamp()
    {
        var service = new MenuUsageLogService(_testFile);
        service.RecordUse("OFS (발주처리)");
        service.RecordUse("OFS (발주처리)");
        service.RecordUse("OFS (발주처리)");

        var stat = service.GetAll()["OFS (발주처리)"];

        Assert.AreEqual(3, stat.Count);
    }

    [TestMethod]
    public void RecordUse_PersistsAcrossInstances()
    {
        var service1 = new MenuUsageLogService(_testFile);
        service1.RecordUse("배송지 주소록 관리");

        var service2 = new MenuUsageLogService(_testFile);
        var stat = service2.GetAll()["배송지 주소록 관리"];

        Assert.AreEqual(1, stat.Count);
    }

    [TestMethod]
    public void GetAll_NoUsageYet_ReturnsEmpty()
    {
        var service = new MenuUsageLogService(_testFile);

        Assert.AreEqual(0, service.GetAll().Count);
    }
}
