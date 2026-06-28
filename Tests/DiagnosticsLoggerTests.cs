using MiniERP2.Config;
using MiniERP2.Utils;

namespace MiniERP2.Tests;

[TestClass]
public class DiagnosticsLoggerTests
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
    public void Log_WritesMessageImmediatelyToDisk()
    {
        DiagnosticsLogger.Log("첫 줄");
        DiagnosticsLogger.Log("둘째 줄");

        Assert.IsTrue(File.Exists(PathProvider.DiagnosticsLogFilePath));
        var content = File.ReadAllText(PathProvider.DiagnosticsLogFilePath);
        Assert.IsTrue(content.Contains("첫 줄"));
        Assert.IsTrue(content.Contains("둘째 줄"));
    }
}
